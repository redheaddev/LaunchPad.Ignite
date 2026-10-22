using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services.Macros;

public sealed record MacroInvocationParseResult(
    bool IsMacroInvocation,
    MacroDefinition? Macro = null,
    IReadOnlyDictionary<string, string>? Arguments = null,
    string? Error = null);

public sealed class MacroInvocationParser
{
    private readonly MacroStore _macroStore;
    private readonly MacroArgumentBinder _argumentBinder;
    private readonly CommandLineTokenizer _tokenizer = new();

    public MacroInvocationParser(MacroStore macroStore, MacroArgumentBinder argumentBinder)
    {
        _macroStore = macroStore;
        _argumentBinder = argumentBinder;
    }

    public MacroInvocationParseResult Parse(
        string input,
        IEnumerable<CommandVariableDefinition> variables)
    {
        var trimmedInput = input.TrimStart();
        if (!trimmedInput.StartsWith('%'))
        {
            return new MacroInvocationParseResult(false);
        }

        var tokens = _tokenizer.Tokenize(trimmedInput);
        if (tokens.Count == 0 || tokens[0].Value.Length < 2)
        {
            return new MacroInvocationParseResult(true, Error: "A macro name is required after '%'.");
        }

        var macro = _macroStore.Find(tokens[0].Value[1..]);
        if (macro is null)
        {
            return new MacroInvocationParseResult(true, Error: $"Macro '{tokens[0].Value[1..]}' was not found.");
        }

        var argumentText = trimmedInput[(tokens[0].Start + tokens[0].Length)..];
        var binding = _argumentBinder.Bind(argumentText, macro, variables);
        return binding.Succeeded
            ? new MacroInvocationParseResult(true, macro, binding.Arguments)
            : new MacroInvocationParseResult(true, macro, Error: binding.Error);
    }
}
