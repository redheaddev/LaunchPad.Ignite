using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.UrlTargets;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class UrlCommandModel : ICommandModel
{
    private readonly UrlTargetBuilder _targetBuilder;
    private readonly IUrlTargetOpener _targetOpener;

    public UrlCommandModel(IUrlTargetOpener targetOpener, IEnumerable<CommandVariableDefinition>? variables = null)
    {
        _targetOpener = targetOpener;
        _targetBuilder = new UrlTargetBuilder(variables);
    }

    public string Kind => "url";

    public void UpdateVariables(IEnumerable<CommandVariableDefinition> variables)
        => _targetBuilder.UpdateVariables(variables);

    public UrlTargetBuildResult BuildPreview(ParsedCommand command)
        => _targetBuilder.Build(command);

    public bool TryExecuteDirectUrl(string text, out CommandExecutionResult result, bool newWindow = false)
    {
        var candidate = text.Trim();
        if (!TryCreateDirectUri(candidate, out var uri))
        {
            result = new CommandExecutionResult(false, string.Empty);
            return false;
        }

        try
        {
            _targetOpener.Open([new UrlTarget(uri.AbsoluteUri)], newWindow);
            result = new CommandExecutionResult(true, "Opened URL.");
        }
        catch (Exception exception)
        {
            result = new CommandExecutionResult(false, $"Unable to open URL: {exception.Message}");
        }

        return true;
    }

    private static bool TryCreateDirectUri(string candidate, out Uri uri)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            uri = absoluteUri;
            return Uri.IsWellFormedUriString(candidate, UriKind.Absolute);
        }

        if (candidate.Length == 0
            || candidate.Any(char.IsWhiteSpace)
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || HasNonHttpScheme(candidate))
        {
            uri = null!;
            return false;
        }

        if (!Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out uri!)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.HostNameType != UriHostNameType.Dns
            || !uri.Host.Contains('.', StringComparison.Ordinal)
            || uri.UserInfo.Length > 0
            || !Uri.IsWellFormedUriString(uri.AbsoluteUri, UriKind.Absolute))
        {
            uri = null!;
            return false;
        }

        return true;
    }

    private static bool HasNonHttpScheme(string candidate)
    {
        var separatorIndex = candidate.IndexOf("://", StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return false;
        }

        var scheme = candidate[..separatorIndex];
        return scheme.Length > 0
            && char.IsAsciiLetter(scheme[0])
            && scheme.Skip(1).All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.');
    }

    public CommandExecutionResult Execute(ParsedCommand command)
        => Execute(command, newWindow: false);

    public CommandExecutionResult Execute(ParsedCommand command, bool newWindow)
    {
        var targetResult = _targetBuilder.Build(command);
        if (!targetResult.Succeeded)
        {
            return new CommandExecutionResult(false, targetResult.Error ?? "Unable to build URL target.");
        }

        try
        {
            _targetOpener.Open(targetResult.Targets, newWindow);
            return new CommandExecutionResult(
                true,
                targetResult.Targets.Count == 1 ? "Opened URL." : $"Opened {targetResult.Targets.Count} URLs.");
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to open URL: {exception.Message}");
        }
    }

    public static bool TryRemoveNewWindowOption(string text, out string commandText)
    {
        var tokens = new CommandLineTokenizer().Tokenize(text)
            .Where(token => string.Equals(token.Value, "--new-window", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(token => token.Start)
            .ToArray();
        if (tokens.Length == 0)
        {
            commandText = text;
            return false;
        }

        commandText = text;
        foreach (var token in tokens)
        {
            commandText = commandText.Remove(token.Start, token.Length);
        }

        commandText = commandText.Trim();
        return true;
    }
}
