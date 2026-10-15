using System;
using System.Linq;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class SecretListCommandModel : ICommandModel
{
    private readonly ISecretStore _secretStore;

    public SecretListCommandModel(ISecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    public string Kind => "secretList";

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        var names = _secretStore.GetNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        return new CommandExecutionResult(true, names.Length == 0
            ? "No saved secrets."
            : string.Join(Environment.NewLine, names));
    }
}