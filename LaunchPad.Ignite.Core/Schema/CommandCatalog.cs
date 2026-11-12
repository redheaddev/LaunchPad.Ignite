namespace LaunchPad.Ignite.Core.Schema;

public sealed class CommandCatalog
{
    private readonly Dictionary<string, CommandDefinition> _commands;

    public CommandCatalog(CommandSchema schema, IEnumerable<string>? supportedCommandKinds = null)
    {
        var errors = new CommandSchemaValidator().Validate(schema, supportedCommandKinds);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(schema));
        }

        Schema = schema;
        _commands = schema.Commands.ToDictionary(command => command.QualifiedName, StringComparer.OrdinalIgnoreCase);
    }

    public CommandSchema Schema { get; }

    public IReadOnlyCollection<CommandDefinition> Commands => _commands.Values;

    public bool TryGetCommand(string name, out CommandDefinition command)
    {
        if (_commands.TryGetValue(name, out command!))
        {
            return true;
        }

        var matches = Schema.Commands
            .Where(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length == 1)
        {
            command = matches[0];
            return true;
        }

        command = null!;
        return false;
    }

    public bool TryGetAction(CommandDefinition command, string name, out CommandActionDefinition action)
    {
        action = command.Actions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return action is not null;
    }

    public bool TryGetDefaultAction(CommandDefinition command, out CommandActionDefinition action)
        => TryGetAction(command, "default", out action);
}
