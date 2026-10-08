using CommunityToolkit.Mvvm.ComponentModel;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly CommandCompletionSession _completionSession;
    private readonly CommandCatalog _catalog;
    private readonly CommandLineParser _parser;
    private readonly ShortcutResolver _shortcutResolver;
    private readonly CommandTextVariableExpander _variableExpander;
    private readonly CommandLineTokenizer _tokenizer = new();
    private readonly IReadOnlyDictionary<string, ICommandExecutor> _commandExecutors;
    private readonly UrlCommandModel _urlCommandModel;
    private bool _syncingInput;

    public MainViewModel()
    {
        var extensions = new ExtensionLoader().Load();
        var schema = new CommandSchemaLoader(extensions.Schema).Load();
        _catalog = new CommandCatalog(schema, extensions.Executors.Keys.Append("shell"));
        _variableExpander = new CommandTextVariableExpander(schema.Variables);
        _shortcutResolver = new ShortcutResolver(schema.Commands);
        var valueProvider = new CommandSchemaValueProvider(schema);
        _completionSession = new CommandCompletionSession(_catalog, valueProvider);
        _parser = new CommandLineParser(_catalog, valueProvider);
        _urlCommandModel = new UrlCommandModel(new SystemUrlTargetOpener(), schema.Variables);
        var commandExecutors = new Dictionary<string, ICommandExecutor>(StringComparer.OrdinalIgnoreCase)
        {
            ["url"] = _urlCommandModel,
            ["shell"] = new ShellCommandModel(new SystemProcessStarter())
        };
        foreach (var (kind, executor) in extensions.Executors)
        {
            if (!commandExecutors.TryAdd(kind, executor))
            {
                throw new InvalidOperationException($"Command kind '{kind}' is already registered.");
            }
        }

        _commandExecutors = commandExecutors;
        ApplyCompletionState(_completionSession.State);
    }

    [ObservableProperty]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private string _completionGhostText = string.Empty;

    private string _commandExpansionPreview = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<CompletionOptionViewModel> _completionItems = Array.Empty<CompletionOptionViewModel>();

    [NotifyPropertyChangedFor(nameof(HasUsageText))]
    [ObservableProperty]
    private string _usageText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Start typing to see commands";

    public bool IsChecklistActive => _completionSession.State.IsChecklistActive;

    public bool HasCompletionOptions => _completionSession.State.Items.Count > 0;

    public bool HasUsageText => !string.IsNullOrEmpty(UsageText);

    public string CommandExpansionPreview
    {
        get => _commandExpansionPreview;
        private set
        {
            if (SetProperty(ref _commandExpansionPreview, value))
            {
                OnPropertyChanged(nameof(HasCommandExpansionPreview));
            }
        }
    }

    public bool HasCommandExpansionPreview => !string.IsNullOrEmpty(CommandExpansionPreview);

    public bool CanComplete => _completionSession.State.CanComplete;

    public bool CanExpandTypedShortcut => _shortcutResolver.TryExpandAlias(CommandText, out _);

    public bool TryGetKeyboardShortcutExpansion(KeyboardShortcutChord chord, out string expansion)
        => _shortcutResolver.TryExpandChord(chord, out expansion!);

    public bool IsCommandTextEmpty => string.IsNullOrEmpty(CommandText);

    public bool HandleInputKey(CommandInputKey key)
    {
        if (key == CommandInputKey.Enter)
        {
            TryExpandTypedShortcut();
            return ExecuteCommand();
        }

        if (key == CommandInputKey.Tab && TryExpandTypedShortcut())
        {
            return false;
        }

        ApplyCompletionState(_completionSession.HandleKey(key));
        return false;
    }

    partial void OnCommandTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsCommandTextEmpty));
        UpdateCommandExpansionPreview(value);
        if (!_syncingInput)
        {
            ApplyCompletionState(_completionSession.UpdateInputText(value));
        }
    }

    private void ApplyCompletionState(CommandCompletionState state)
    {
        if (!string.Equals(CommandText, state.InputText, StringComparison.Ordinal))
        {
            _syncingInput = true;
            CommandText = state.InputText;
            _syncingInput = false;
        }

        UpdateCommandExpansionPreview(state.InputText);
        CompletionGhostText = state.InputText.Length == 0 ? string.Empty : state.GhostText;
        UsageText = GetUsageText(CommandText);
        var tokens = _tokenizer.Tokenize(state.InputText);
        var isCommandCompletion = tokens.Count == 0
            || (tokens.Count == 1 && !_tokenizer.EndsWithWhitespace(state.InputText));
        CompletionItems = state.Items.Select(item =>
        {
            var displayText = state.IsChecklistActive
                ? $"{(item.IsSelected ? "[x]" : "[ ]")} {item.Value}"
                : item.Value;
            if (!state.IsChecklistActive
                && isCommandCompletion
                && _catalog.TryGetCommand(item.Value, out var command))
            {
                displayText = $"{item.Value} [{command.Kind}]";
            }

            return new CompletionOptionViewModel(displayText, item.IsHighlighted, item.IsSelected);
        }).ToArray();
        OnPropertyChanged(nameof(IsChecklistActive));
        OnPropertyChanged(nameof(HasCompletionOptions));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanExpandTypedShortcut));
    }

    private bool TryExpandTypedShortcut()
    {
        if (!_shortcutResolver.TryExpandAlias(CommandText, out var expansion))
        {
            return false;
        }

        CommandText = expansion;
        return true;
    }

    private void UpdateCommandExpansionPreview(string commandText)
    {
        var expansion = _variableExpander.Expand(commandText);
        CommandExpansionPreview = expansion.ResolvedVariables.Count == 0
            ? string.Empty
            : expansion.ExpandedText;
    }

public sealed record CompletionOptionViewModel(string DisplayText, bool IsHighlighted, bool IsSelected);

    private string GetUsageText(string inputText)
    {
        var tokens = _tokenizer.Tokenize(inputText);
        if (tokens.Count == 0 || !_catalog.TryGetCommand(tokens[0].Value, out var command))
        {
            return string.Empty;
        }

        var action = command.Actions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "default", StringComparison.OrdinalIgnoreCase));
        if (tokens.Count > 1 && _catalog.TryGetAction(command, tokens[1].Value, out var explicitAction)
            && !string.Equals(explicitAction.Name, "default", StringComparison.OrdinalIgnoreCase))
        {
            action = explicitAction;
        }

        return action is null ? string.Empty : $"Usage: {action.Usage}";
    }

    private bool ExecuteCommand()
    {
        var expansion = _variableExpander.Expand(CommandText);
        if (expansion.UnresolvedVariables.Count > 0)
        {
            StatusMessage = $"Unknown variable(s): {string.Join(", ", expansion.UnresolvedVariables.Select(name => $"${name}"))}";
            return false;
        }

        if (_urlCommandModel.TryExecuteDirectUrl(expansion.ExpandedText, out var directUrlResult))
        {
            StatusMessage = directUrlResult.Message;
            if (!directUrlResult.Succeeded)
            {
                return false;
            }

            CommandText = string.Empty;
            return true;
        }

        var parseResult = _parser.Parse(expansion.ExpandedText);
        if (parseResult.Status == CommandParseStatus.Incomplete)
        {
            StatusMessage = "Command is incomplete.";
            return false;
        }

        if (parseResult.Status == CommandParseStatus.Invalid)
        {
            StatusMessage = parseResult.Error ?? "Command is invalid.";
            return false;
        }

        var parsedCommand = parseResult.ParsedCommand!;
        if (!_commandExecutors.TryGetValue(parsedCommand.Definition.Kind, out var commandExecutor))
        {
            StatusMessage = $"Command kind '{parsedCommand.Definition.Kind}' is not supported.";
            return false;
        }

        var executionResult = commandExecutor.Execute(parsedCommand);
        StatusMessage = executionResult.Message;
        if (!executionResult.Succeeded)
        {
            return false;
        }

        CommandText = string.Empty;
        return true;
    }
}
