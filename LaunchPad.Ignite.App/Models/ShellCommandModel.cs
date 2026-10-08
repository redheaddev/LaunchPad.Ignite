using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class ShellCommandModel : ICommandModel
{
    private static readonly Regex ArgumentTemplatePlaceholder = new("\\{([^{}]+)\\}", RegexOptions.Compiled);
    private readonly IProcessStarter _processStarter;

    public ShellCommandModel(IProcessStarter processStarter)
    {
        _processStarter = processStarter;
    }

    public string Kind => "shell";

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.Kind, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command kind '{command.Definition.Kind}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(command.Action.TargetTemplate))
        {
            return new CommandExecutionResult(false, "An executable target is required.");
        }

        var startInfo = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(command.Action.TargetTemplate))
        {
            UseShellExecute = false
        };

        if (command.Action.ArgumentTemplates.Count > 0)
        {
            var argumentNames = command.Action.Arguments
                .Select(argument => argument.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var template in command.Action.ArgumentTemplates)
            {
                var unknownPlaceholder = ArgumentTemplatePlaceholder.Matches(template)
                    .Select(match => match.Groups[1].Value)
                    .FirstOrDefault(name => !argumentNames.Contains(name));
                if (unknownPlaceholder is not null)
                {
                    return new CommandExecutionResult(false, $"Argument template contains unknown placeholder '{{{unknownPlaceholder}}}'.");
                }

                var value = ArgumentTemplatePlaceholder.Replace(template, match =>
                    command.Arguments.TryGetValue(match.Groups[1].Value, out var argumentValue)
                        ? argumentValue
                        : string.Empty);
                startInfo.ArgumentList.Add(Environment.ExpandEnvironmentVariables(value));
            }
        }
        else
        {
            foreach (var argument in command.Action.Arguments)
            {
                if (command.Arguments.TryGetValue(argument.Name, out var value))
                {
                    startInfo.ArgumentList.Add(Environment.ExpandEnvironmentVariables(value));
                }
            }
        }

        try
        {
            _processStarter.Start(startInfo);
            return new CommandExecutionResult(true, $"Started '{startInfo.FileName}'.");
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to start '{startInfo.FileName}': {exception.Message}");
        }
    }
}
