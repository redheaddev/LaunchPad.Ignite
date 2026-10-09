using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Completion;

internal enum CompletionKind
{
    None,
    Command,
    Action,
    Argument,
    Variable,
    Function
}

internal sealed record CompletionResolution(
    CompletionKind Kind,
    string Prefix,
    int TokenStart,
    IReadOnlyList<string> Candidates,
    bool IsChecklist,
    ListSelectionMode ListMode,
    bool AppendSpaceOnAccept,
    bool HasNextArgument,
    CommandArgumentDefinition? Argument = null,
    int ArgumentIndex = -1,
    bool AwaitingSeparator = false)
{
    public IReadOnlyList<string>? CandidateDisplayValues { get; init; }
}
