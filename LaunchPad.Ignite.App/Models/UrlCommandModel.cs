using System;
using System.Collections.Generic;
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

    public bool TryExecuteDirectUrl(string text, out CommandExecutionResult result)
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
            _targetOpener.Open([new UrlTarget(uri.AbsoluteUri)]);
            result = new CommandExecutionResult(true, "Opened URL.");
        }
        catch (Exception exception)
        {
            result = new CommandExecutionResult(false, $"Unable to open URL: {exception.Message}");
        }

        return true;
    }

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        var targetResult = _targetBuilder.Build(command);
        if (!targetResult.Succeeded)
        {
            return new CommandExecutionResult(false, targetResult.Error ?? "Unable to build URL target.");
        }

        try
        {
            _targetOpener.Open(targetResult.Targets);
            return new CommandExecutionResult(
                true,
                targetResult.Targets.Count == 1 ? "Opened URL." : $"Opened {targetResult.Targets.Count} URLs.");
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to open URL: {exception.Message}");
        }
    }
}
