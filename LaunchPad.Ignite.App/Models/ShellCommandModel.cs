using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Platform.ShellCommands;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class ShellCommandModel : ICommandModel
{
    private static readonly Regex ArgumentTemplatePlaceholder = new("\\{([^{}]+)\\}", RegexOptions.Compiled);
    private readonly IProcessStarter _processStarter;
    private readonly IPlatformShellCommandLauncher _platformLauncher;

    public ShellCommandModel(IProcessStarter processStarter)
    {
        _processStarter = processStarter;
        _platformLauncher = PlatformShellCommandLauncherFactory.Create();
    }

    public string Kind => "shell";

    public CommandExecutionResult Execute(ParsedCommand command)
        => Execute(command, CommandLaunchMode.Normal);

    public CommandExecutionResult Execute(ParsedCommand command, CommandLaunchMode launchMode)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(command.Action.TargetTemplate))
        {
            return new CommandExecutionResult(false, "An executable target is required.");
        }

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

        var target = Environment.ExpandEnvironmentVariables(command.Action.TargetTemplate);
        var workingDirectory = string.IsNullOrWhiteSpace(command.Action.WorkingDirectoryTemplate)
            ? null
            : Environment.ExpandEnvironmentVariables(command.Action.WorkingDirectoryTemplate);
        var startInfo = _platformLauncher.CreateStartInfo(target, workingDirectory, arguments, launchMode);

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

}
