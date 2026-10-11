using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Services;
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
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !Uri.IsWellFormedUriString(candidate, UriKind.Absolute))
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
