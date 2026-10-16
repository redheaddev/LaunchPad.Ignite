using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public sealed class MacroCompletionSession
{
    private readonly CommandCompletionSession _session;
    private readonly HashSet<string> _macroNames;
    private string _inputText = string.Empty;

    public MacroCompletionSession(
        IEnumerable<MacroDefinition> macros,
        IArgumentValueProvider valueProvider,
        IEnumerable<CommandVariableDefinition> variables,
        IEnumerable<CommandFunctionDefinition> functions)
    {
        var macroList = macros.ToArray();
        _macroNames = macroList
            .Select(macro => macro.Metadata.QualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commands = macroList.Select(CreateCompletionCommand).ToArray();
        var catalog = new CommandCatalog(
            new CommandSchema([], commands),
            ["macro"]);
        _session = new CommandCompletionSession(catalog, valueProvider, variables, functions);
        State = WrapState(_session.State);
    }

    public CommandCompletionState State { get; private set; }

    public CommandCompletionState UpdateVariables(IEnumerable<CommandVariableDefinition> variables)
    {
        _session.UpdateVariables(variables);
        State = WrapState(_session.State);
        return State;
    }

    public CommandCompletionState UpdateInputText(string inputText)
    {
        _inputText = inputText;
        _session.UpdateInputText(inputText.Length > 0 ? inputText[1..] : string.Empty);
        State = WrapState(_session.State);
        return State;
    }

    public CommandCompletionState HandleKey(CommandInputKey key)
    {
        var state = _session.HandleKey(key);
        _inputText = $"%{state.InputText}";
        State = WrapState(_session.State);
        return State;
    }

    private CommandCompletionState WrapState(CommandCompletionState state)
    {
        var innerTokens = new LaunchPad.Ignite.Core.Parsing.CommandLineTokenizer().Tokenize(state.InputText);
        var completingName = innerTokens.Count == 0
            || !_macroNames.Contains(innerTokens[0].Value);
        var items = completingName
            ? state.Items.Select(item => item with
            {
                Value = $"%{item.Value}",
                DisplayText = item.DisplayText is null ? null : $"%{item.DisplayText}"
            }).ToArray()
            : state.Items;
        return state with
        {
            InputText = _inputText,
            Items = items
        };
    }

    private static CommandDefinition CreateCompletionCommand(MacroDefinition macro)
    {
        var arguments = macro.Metadata.Arguments.Select(argument => new CommandArgumentDefinition(
            argument.Name,
            Ref: argument.Type switch
            {
                MacroArgumentType.List => $"list:{argument.ListName}",
                MacroArgumentType.SecretList => "list:secrets",
                _ => null
            },
            InputType: argument.Type == MacroArgumentType.ValueList ? "csv" : null,
            ListMode: argument.ListMode));
        var usage = $"%{macro.Metadata.QualifiedName}";
        return new CommandDefinition(
            macro.Metadata.Name,
            "macro",
            $"Macro {macro.Metadata.QualifiedName}",
            [new CommandActionDefinition("default", "Run macro", usage, "macro", arguments.ToArray())],
            macro.Metadata.Namespace);
    }
}
