using System.Collections.Generic;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public enum MacroArgumentType
{
    Value,
    Variable,
    List,
    SecretList,
    ValueList
}

public enum MacroErrorBehavior
{
    Break,
    Continue
}

public sealed record MacroArgumentDefinition(
    string Name,
    MacroArgumentType Type,
    string? ListName = null,
    ListSelectionMode ListMode = ListSelectionMode.Multiple);

public sealed record MacroMetadata(
    string Id,
    string Name,
    string? Namespace,
    IReadOnlyList<MacroArgumentDefinition> Arguments,
    MacroErrorBehavior OnError)
{
    public string QualifiedName => string.IsNullOrWhiteSpace(Namespace) ? Name : $"{Namespace}:{Name}";
}

public sealed record MacroDefinition(MacroMetadata Metadata, string Script);
