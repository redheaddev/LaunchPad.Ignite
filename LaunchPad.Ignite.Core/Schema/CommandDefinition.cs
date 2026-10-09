namespace LaunchPad.Ignite.Core.Schema;

public sealed record CommandSchema(
    IReadOnlyList<CommandListDefinition> Lists,
    IReadOnlyList<CommandDefinition> Commands)
{
    public IReadOnlyList<CommandVariableDefinition> Variables { get; init; } = [];
    public IReadOnlyList<CommandFunctionDefinition> Functions { get; init; } = [];
}

public sealed record CommandVariableDefinition(
    string Name,
    string Value,
    string? Namespace = null)
{
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}:{Name}";
}

public sealed record CommandFunctionDefinition(
    string Name,
    string? Kind,
    IReadOnlyList<CommandFunctionParameterDefinition> Parameters,
    string? TargetTemplate = null,
    string? JsonPath = null,
    string? Interpreter = null,
    string? ScriptPath = null,
    int TimeoutSeconds = 10,
    string? Namespace = null)
{
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}:{Name}";
}

public sealed record CommandFunctionParameterDefinition(
    string Name,
    bool Optional = false);

public sealed record CommandListDefinition(
    string Type,
    string Name,
    IReadOnlyList<string> Values);

public sealed record CommandDefinition(
    string Name,
    string Kind,
    string Description,
    IReadOnlyList<CommandActionDefinition> Actions,
    string? Namespace = null,
    string? KeyboardShortcut = null)
{
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}:{Name}";
}

public sealed record CommandActionDefinition(
    string Name,
    string Description,
    string Usage,
    string TargetTemplate,
    IReadOnlyList<CommandArgumentDefinition> Arguments,
    CommandExecutionDefinition? Execution = null)
{
    public IReadOnlyList<string> ArgumentTemplates { get; init; } = [];
}

public sealed record CommandArgumentDefinition(
    string Name,
    bool Optional = false,
    string? Ref = null,
    string? InputType = null,
    ListSelectionMode ListMode = ListSelectionMode.Multiple,
    bool AllowCustomValues = false);

public sealed record CommandExecutionDefinition(
    CommandExecutionMode Mode,
    IReadOnlyList<string> Arguments);
