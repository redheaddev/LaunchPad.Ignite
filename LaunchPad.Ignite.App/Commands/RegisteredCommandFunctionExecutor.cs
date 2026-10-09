using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed class RegisteredCommandFunctionExecutor : ICommandFunctionExecutor
{
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();
    private static readonly Regex ParameterPlaceholder = new("<([^<>]+)>", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyDictionary<string, ICommandFunctionExecutor> _extensionFunctionExecutors;

    public RegisteredCommandFunctionExecutor(
        HttpClient? httpClient = null,
        IReadOnlyDictionary<string, ICommandFunctionExecutor>? extensionFunctionExecutors = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _extensionFunctionExecutors = extensionFunctionExecutors
            ?? new Dictionary<string, ICommandFunctionExecutor>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> ExecuteAsync(
        CommandFunctionDefinition function,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (_extensionFunctionExecutors.TryGetValue(function.QualifiedName, out var extensionExecutor))
        {
            return await extensionExecutor.ExecuteAsync(function, arguments, cancellationToken);
        }

        if (function.Kind is null)
        {
            throw new InvalidOperationException($"No extension handler is registered for function '{function.QualifiedName}'.");
        }

        var parameters = function.Parameters
            .Take(arguments.Count)
            .Select((parameter, index) => (name: parameter.Name, value: arguments[index]))
            .ToDictionary(parameter => parameter.name, parameter => parameter.value, StringComparer.OrdinalIgnoreCase);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(function.TimeoutSeconds));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        if (string.Equals(function.Kind, "httpGet", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteHttpGetAsync(function, parameters, linkedCancellation.Token, cancellationToken);
        }

        if (string.Equals(function.Kind, "script", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteScriptAsync(function, arguments, linkedCancellation.Token, cancellationToken, timeout.Token);
        }

        throw new InvalidOperationException($"Function '{function.QualifiedName}' has unsupported kind '{function.Kind}'.");
    }

    private async Task<string> ExecuteHttpGetAsync(
        CommandFunctionDefinition function,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken,
        CancellationToken callerCancellationToken)
    {
        var target = ParameterPlaceholder.Replace(function.TargetTemplate!, match =>
        {
            if (!parameters.TryGetValue(match.Groups[1].Value, out var value))
            {
                var optionalParameter = function.Parameters.FirstOrDefault(parameter =>
                    string.Equals(parameter.Name, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                if (optionalParameter?.Optional == true)
                {
                    return string.Empty;
                }

                throw new InvalidOperationException(
                    $"Function '{function.QualifiedName}' target contains unknown parameter '{match.Groups[1].Value}'.");
            }

            return Uri.EscapeDataString(value);
        });
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Function '{function.QualifiedName}' did not produce a valid HTTP or HTTPS URL.");
        }

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(function.JsonPath) ? body : SelectJsonValue(body, function.JsonPath);
        }
        catch (OperationCanceledException exception) when (!callerCancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"REST function '{function.QualifiedName}' exceeded its {function.TimeoutSeconds}-second timeout.",
                exception);
        }
    }

    private static async Task<string> ExecuteScriptAsync(
        CommandFunctionDefinition function,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        CancellationToken callerCancellationToken,
        CancellationToken timeoutToken)
    {
        if (string.IsNullOrWhiteSpace(function.ScriptPath) || !Path.IsPathFullyQualified(function.ScriptPath))
        {
            throw new InvalidOperationException($"Script function '{function.QualifiedName}' has no resolved script path.");
        }

        var startInfo = new ProcessStartInfo(function.Interpreter!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(function.ScriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start interpreter '{function.Interpreter}' for function '{function.QualifiedName}'.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            if (callerCancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (timeoutToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Script function '{function.QualifiedName}' exceeded its {function.TimeoutSeconds}-second timeout.");
            }

            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Script function '{function.QualifiedName}' exited with code {process.ExitCode}: {error.Trim()}");
        }

        return output.TrimEnd('\r', '\n');
    }

    private static string SelectJsonValue(string json, string jsonPath)
    {
        using var document = JsonDocument.Parse(json);
        var current = document.RootElement;
        var index = 0;
        if (jsonPath.Length == 0 || jsonPath[0] != '$')
        {
            throw new FormatException("JSON selectors must start with '$'.");
        }

        index++;
        while (index < jsonPath.Length)
        {
            if (jsonPath[index] == '.')
            {
                var start = ++index;
                while (index < jsonPath.Length && jsonPath[index] is not '.' and not '[')
                {
                    index++;
                }

                if (start == index || current.ValueKind != JsonValueKind.Object
                    || !current.TryGetProperty(jsonPath[start..index], out current))
                {
                    throw new InvalidOperationException($"JSON selector '{jsonPath}' did not match a value.");
                }
            }
            else if (jsonPath[index] == '[')
            {
                var closeBracket = jsonPath.IndexOf(']', index + 1);
                if (closeBracket < 0
                    || !int.TryParse(jsonPath[(index + 1)..closeBracket], out var itemIndex)
                    || itemIndex < 0
                    || current.ValueKind != JsonValueKind.Array
                    || itemIndex >= current.GetArrayLength())
                {
                    throw new InvalidOperationException($"JSON selector '{jsonPath}' did not match an array item.");
                }

                current = current[itemIndex];
                index = closeBracket + 1;
            }
            else
            {
                throw new FormatException($"JSON selector '{jsonPath}' uses unsupported syntax.");
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString()!,
            JsonValueKind.Null => throw new InvalidOperationException($"JSON selector '{jsonPath}' resolved to null."),
            _ => current.GetRawText()
        };
    }

    private static HttpClient CreateSharedHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        return client;
    }
}
