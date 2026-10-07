using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Parsing;

public enum CommandParseStatus
{
    Incomplete,
    Valid,
    Invalid
}

public sealed record ParsedCommand(
    CommandDefinition Definition,
    CommandActionDefinition Action,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record CommandParseResult(
    CommandParseStatus Status,
    ParsedCommand? ParsedCommand = null,
    string? Error = null);
