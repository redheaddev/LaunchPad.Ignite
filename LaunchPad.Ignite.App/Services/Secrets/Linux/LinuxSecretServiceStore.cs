using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Tmds.DBus.Protocol;

namespace LaunchPad.Ignite.App.Services.Secrets.Linux;

internal sealed class LinuxSecretServiceStore : ISecretStore
{
    private const string SecretToolName = "secret-tool";
    private const string ServiceName = "LaunchPad.Ignite";
    private const string SecretAttributeName = "name";
    private const string AppAttributeName = "app";

    private readonly Func<string, string[], ProcessStartInfo> _processFactory;
    private readonly Func<bool> _isSecretToolAvailable;

    public LinuxSecretServiceStore()
        : this(BuildProcessStartInfo, IsSecretToolAvailable)
    {
    }

    internal LinuxSecretServiceStore(
        Func<string, string[], ProcessStartInfo>? processFactory = null,
        Func<bool>? isSecretToolAvailable = null)
    {
        _processFactory = processFactory ?? BuildProcessStartInfo;
        _isSecretToolAvailable = isSecretToolAvailable ?? IsSecretToolAvailable;

        if (!HasSessionBus() || !_isSecretToolAvailable())
        {
            IsAvailable = false;
            UnavailableReason = "Secret storage is unavailable on this Linux session because the desktop secret service is not installed, the session bus is unavailable, or the secret service CLI is not accessible.";
            return;
        }

        IsAvailable = true;
        UnavailableReason = null;
    }

    public bool IsAvailable { get; }

    public string? UnavailableReason { get; }

    public IReadOnlyList<string> GetNames()
    {
        EnsureAvailable();

        try
        {
            using var process = CreateProcess(new[] { "search", "--all", AppAttributeName, ServiceName });
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return Array.Empty<string>();
            }

            return ParseNames(output);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public bool TryGet(string name, out string value)
    {
        EnsureAvailable();
        if (string.IsNullOrWhiteSpace(name))
        {
            value = string.Empty;
            return false;
        }

        try
        {
            using var process = CreateProcess(new[] { "search", "--all", AppAttributeName, ServiceName });
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                value = string.Empty;
                return false;
            }

            foreach (var record in ParseSecretRecords(output))
            {
                if (!TryGetFieldValue(record, AppAttributeName, out var appValue)
                    || !string.Equals(appValue, ServiceName, StringComparison.OrdinalIgnoreCase)
                    || !TryGetFieldValue(record, SecretAttributeName, out var hiddenName)
                    || !string.Equals(hiddenName, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (record.TryGetValue("secret", out var secretValue))
                {
                    value = NormalizeSecretValue(secretValue);
                    return !string.IsNullOrEmpty(value);
                }
            }

            value = string.Empty;
            return false;
        }
        catch (Exception)
        {
            value = string.Empty;
            return false;
        }
    }

    public void Set(string name, string value)
    {
        EnsureAvailable();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Secret names cannot be empty.", nameof(name));
        }

        var normalizedValue = NormalizeSecretValue(value);
        if (string.IsNullOrEmpty(normalizedValue))
        {
            throw new ArgumentException("Secret values cannot be empty.", nameof(value));
        }

        try
        {
            using var process = CreateProcess(new[]
            {
                "store",
                "--label=LaunchPad.Ignite:" + name,
                AppAttributeName,
                ServiceName,
                SecretAttributeName,
                name
            });
            process.Start();
            process.StandardInput.Write(normalizedValue);
            process.StandardInput.Close();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("The Linux secret service rejected the secret value.");
            }

            if (!TryGet(name, out var storedValue) || !string.Equals(storedValue, normalizedValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The Linux secret service did not confirm the stored value.");
            }
        }
        catch (Exception)
        {
            throw;
        }
    }

    public bool Remove(string name)
    {
        EnsureAvailable();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            using var process = CreateProcess(new[] { "clear", AppAttributeName, ServiceName, SecretAttributeName, name });
            process.Start();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ProcessStartInfo BuildProcessStartInfo(string command, string[] arguments)
    {
        var startInfo = new ProcessStartInfo(command)
        {
            RedirectStandardInput = command == SecretToolName,
            RedirectStandardOutput = command == SecretToolName,
            RedirectStandardError = command == SecretToolName,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private Process CreateProcess(string[] arguments)
    {
        return Process.Start(_processFactory(SecretToolName, arguments))
            ?? throw new InvalidOperationException("Unable to start the Linux secret service CLI.");
    }

    private static bool HasSessionBus()
    {
        var sessionBus = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (!string.IsNullOrWhiteSpace(sessionBus))
        {
            return true;
        }

        var fallback = DBusAddress.Session;
        return fallback is not null && !string.IsNullOrWhiteSpace(fallback);
    }

    private static bool IsSecretToolAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(SecretToolName, "--help")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static string NormalizeSecretValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", string.Empty)
            .Trim();
    }

    private static IReadOnlyList<string> ParseNames(string output)
    {
        var names = new List<string>();

        foreach (var record in ParseSecretRecords(output))
        {
            if (!TryGetFieldValue(record, AppAttributeName, out var appValue)
                || !string.Equals(appValue, ServiceName, StringComparison.OrdinalIgnoreCase)
                || !TryGetFieldValue(record, SecretAttributeName, out var name)
                || string.IsNullOrWhiteSpace(name)
                || names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            names.Add(name);
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetFieldValue(IReadOnlyDictionary<string, string> record, string key, out string value)
    {
        var normalizedKey = key.Trim();
        if (record.TryGetValue(normalizedKey, out var directValue) && !string.IsNullOrWhiteSpace(directValue))
        {
            value = directValue;
            return true;
        }

        var prefixedKey = "attribute." + normalizedKey;
        if (record.TryGetValue(prefixedKey, out var attributeValue) && !string.IsNullOrWhiteSpace(attributeValue))
        {
            value = attributeValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static IReadOnlyList<Dictionary<string, string>> ParseSecretRecords(string output)
    {
        var records = new List<Dictionary<string, string>>();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                if (current.Count > 0)
                {
                    records.Add(current);
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex < 0)
            {
                continue;
            }

            var key = line.Substring(0, equalsIndex).Trim();
            var value = line.Substring(equalsIndex + 1).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            current[key] = value;
        }

        if (current.Count > 0)
        {
            records.Add(current);
        }

        return records;
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException(UnavailableReason ?? "Secret storage is unavailable on this Linux session.");
        }
    }
}
