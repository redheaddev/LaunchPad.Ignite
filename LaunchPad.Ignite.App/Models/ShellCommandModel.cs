using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
        => Execute(command, CommandLaunchMode.Normal);

    public CommandExecutionResult Execute(ParsedCommand command, CommandLaunchMode launchMode)
    {
        if (!string.Equals(command.Definition.Kind, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command kind '{command.Definition.Kind}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(command.Action.TargetTemplate))
        {
            return new CommandExecutionResult(false, "An executable target is required.");
        }

        var startInfo = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(command.Action.TargetTemplate));
        var arguments = new List<string>();

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
                arguments.Add(Environment.ExpandEnvironmentVariables(value));
            }
        }
        else
        {
            foreach (var argument in command.Action.Arguments)
            {
                if (command.Arguments.TryGetValue(argument.Name, out var value))
                {
                    arguments.Add(Environment.ExpandEnvironmentVariables(value));
                }
            }
        }

        if (launchMode == CommandLaunchMode.Normal)
        {
            startInfo.UseShellExecute = false;
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            startInfo.UseShellExecute = true;
            startInfo.Verb = launchMode switch
            {
                CommandLaunchMode.Elevated => "runas",
                CommandLaunchMode.DifferentUser => "runasuser",
                _ => throw new ArgumentOutOfRangeException(nameof(launchMode))
            };
            startInfo.Arguments = string.Join(" ", arguments.Select(QuoteArgument));
        }

        try
        {
            _processStarter.Start(startInfo);
            var launchDescription = launchMode switch
            {
                CommandLaunchMode.Elevated => " elevated",
                CommandLaunchMode.DifferentUser => " as another user",
                _ => string.Empty
            };
            return new CommandExecutionResult(true, $"Started '{startInfo.FileName}'{launchDescription}.");
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to start '{startInfo.FileName}': {exception.Message}");
        }
    }

    private static string QuoteArgument(string argument)
    {
        var builder = new StringBuilder("\"");
        var backslashCount = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
            }
            else if (character == '\"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append(character);
                backslashCount = 0;
            }
            else
            {
                builder.Append('\\', backslashCount);
                builder.Append(character);
                backslashCount = 0;
            }
        }

        builder.Append('\\', backslashCount * 2);
        builder.Append('\"');
        return builder.ToString();
    }
}
