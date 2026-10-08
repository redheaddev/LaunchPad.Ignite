namespace LaunchPad.Ignite.Core.Completion;

public enum CommandInputKey
{
    Tab,
    Space,
    Enter,
    Escape,
    Up,
    Down,
    SelectAll,
    DeselectAll,
    Clear
}

public sealed record CompletionItem(
    string Value,
    bool IsSelected,
    bool IsHighlighted,
    string? DisplayText = null);

public sealed record CommandCompletionState(
    string InputText,
    string GhostText,
    IReadOnlyList<CompletionItem> Items,
    bool IsChecklistActive,
    string? Message = null,
    bool CanComplete = false);
