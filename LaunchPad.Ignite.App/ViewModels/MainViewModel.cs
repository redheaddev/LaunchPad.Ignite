using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
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
using System.Threading.Tasks;

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
    private readonly SecretCommandModel _secretCommandModel;
    private readonly SecretAccessGate _secretAccessGate;
    private readonly DispatcherTimer _secretAccessStateTimer;
    private bool _syncingInput;

    public MainViewModel()
    {
        var extensions = new ExtensionLoader().Load();
        var schemaLoader = new CommandSchemaLoader(extensions.Schema);
        var schema = schemaLoader.Load();
        schema = schema with
        {
            Commands = schema.Commands
                .Where(command => !string.Equals(command.Name, SecretCommandDefinition.Definition.Name, StringComparison.OrdinalIgnoreCase))
                .Append(VariableCommandModel.Definition)
                .Append(SecretCommandDefinition.Definition)
                .ToArray()
        };
        var secretStore = PlatformSecretStore.Create();
        _catalog = new CommandCatalog(schema, extensions.Executors.Keys.Append("shell").Append("variable").Append("secret"));
        _variableExpander = new CommandTextVariableExpander(schema.Variables);
        _shortcutResolver = new ShortcutResolver(schema.Commands);
        var valueProvider = new CommandSchemaValueProvider(schema, new SecretNamesArgumentValueProvider(secretStore));
        _completionSession = new CommandCompletionSession(
            _catalog,
            valueProvider,
            schema.Variables);
        _parser = new CommandLineParser(_catalog, valueProvider);
        _urlCommandModel = new UrlCommandModel(new SystemUrlTargetOpener(), schema.Variables);
        var processStarter = new SystemProcessStarter();
        var commandExecutors = new Dictionary<string, ICommandExecutor>(StringComparer.OrdinalIgnoreCase)
        {
            ["url"] = _urlCommandModel,
            ["shell"] = new ShellCommandModel(processStarter),
            ["variable"] = new VariableCommandModel(schemaLoader, updatedSchema =>
            {
                _variableExpander.UpdateVariables(updatedSchema.Variables);
                _urlCommandModel.UpdateVariables(updatedSchema.Variables);
                ApplyCompletionState(_completionSession.UpdateVariables(updatedSchema.Variables));
            })
        };
        foreach (var (kind, executor) in extensions.Executors)
        {
            if (!commandExecutors.TryAdd(kind, executor))
            {
                throw new InvalidOperationException($"Command kind '{kind}' is already registered.");
            }
        }

        _commandExecutors = commandExecutors;
        _secretAccessGate = new SecretAccessGate();
        _secretCommandModel = new SecretCommandModel(
            secretStore,
            _secretAccessGate,
            new AvaloniaSecretEntryDialogService(),
            new AvaloniaClipboardService(GetSystemClipboard),
            new SystemSecretTerminalLauncher(processStarter));
        _secretAccessStateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _secretAccessStateTimer.Tick += (_, _) => RefreshSecretAccessState();
        _secretAccessStateTimer.Start();
        RefreshSecretAccessState();
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

    [NotifyPropertyChangedFor(nameof(SecretLockIcon))]
    [NotifyPropertyChangedFor(nameof(SecretLockBrush))]
    [ObservableProperty]
    private bool _secretAccessLocked;

    public string SecretLockIcon => SecretAccessLocked ? "🔒" : "🔓";

    public string SecretLockTooltip => SecretAccessLocked
        ? "Secret access is locked"
        : _secretAccessGate.HasMasterPassword
            ? "Secret access is unlocked"
            : "Secret access is unlocked; no master password is set";

    public IBrush SecretLockBrush => SecretAccessLocked ? Brushes.Gold : Brushes.Gray;

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

    public async Task<bool> HandleInputKeyAsync(CommandInputKey key)
    {
        if (key == CommandInputKey.Enter)
        {
            TryExpandTypedShortcut();
            return await ExecuteCommandAsync();
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
                : item.DisplayText ?? item.Value;
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
        var previewText = commandText;
        var hasShortcutExpansion = _shortcutResolver.TryExpandAlias(commandText, out var shortcutExpansion);
        if (hasShortcutExpansion)
        {
            previewText = shortcutExpansion;
        }

        var expansion = _variableExpander.Expand(previewText);
        CommandExpansionPreview = hasShortcutExpansion || expansion.ResolvedVariables.Count > 0
            ? expansion.ExpandedText
            : string.Empty;
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

    private async Task<bool> ExecuteCommandAsync()
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
        CommandExecutionResult executionResult;
        if (string.Equals(parsedCommand.Definition.Kind, "secret", StringComparison.OrdinalIgnoreCase))
        {
            executionResult = await _secretCommandModel.ExecuteAsync(parsedCommand);
            RefreshSecretAccessState();
        }
        else if (_commandExecutors.TryGetValue(parsedCommand.Definition.Kind, out var commandExecutor))
        {
            executionResult = commandExecutor.Execute(parsedCommand);
        }
        else
        {
            StatusMessage = $"Command kind '{parsedCommand.Definition.Kind}' is not supported.";
            return false;
        }

        StatusMessage = executionResult.Message;
        if (!executionResult.Succeeded)
        {
            return false;
        }

        CommandText = string.Empty;
        return true;
    }

    private void RefreshSecretAccessState()
    {
        SecretAccessLocked = _secretAccessGate.HasMasterPassword && !_secretAccessGate.IsUnlocked;
        OnPropertyChanged(nameof(SecretLockTooltip));
    }

    private static Avalonia.Input.Platform.IClipboard? GetSystemClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }

        return null;
    }
}
