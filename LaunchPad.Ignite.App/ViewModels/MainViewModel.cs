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
    private static readonly IBrush ValueOutputBrush = new SolidColorBrush(Color.FromRgb(0x67, 0xE8, 0xF9));
    private static readonly IBrush StatusMessageBrushDefault = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    private CommandCompletionSession _completionSession = null!;
    private CommandCatalog _catalog = null!;
    private CommandLineParser _parser = null!;
    private ShortcutResolver _shortcutResolver = null!;
    private CommandTextVariableExpander _variableExpander = null!;
    private CommandTextFunctionExpander _functionExpander = null!;
    private readonly CommandLineTokenizer _tokenizer = new();
    private IReadOnlyDictionary<string, ICommandExecutor> _commandExecutors = null!;
    private UrlCommandModel _urlCommandModel = null!;
    private readonly SecretCommandModel _secretCommandModel;
    private readonly SecretAccessGate _secretAccessGate;
    private readonly DispatcherTimer _secretAccessStateTimer;
    private readonly ExtensionStateStore _extensionStateStore;
    private LoadedExtensions _extensions = null!;
    private readonly ISecretStore _secretStore;
    private readonly IProcessStarter _processStarter;
    private bool _isExtensionListVisible;
    private bool _syncingInput;

    public MainViewModel()
    {
        _extensionStateStore = new ExtensionStateStore();
        _secretStore = PlatformSecretStore.Create();
        _processStarter = new SystemProcessStarter();
        _secretAccessGate = new SecretAccessGate();
        _secretCommandModel = new SecretCommandModel(
            _secretStore,
            _secretAccessGate,
            new AvaloniaSecretEntryDialogService(),
            new AvaloniaClipboardService(GetSystemClipboard),
            new SystemSecretTerminalLauncher(_processStarter));
        InitializeCommandRuntime();
        _secretAccessStateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _secretAccessStateTimer.Tick += (_, _) => RefreshSecretAccessState();
        _secretAccessStateTimer.Start();
        RefreshSecretAccessState();
    }

    private void InitializeCommandRuntime()
    {
        var extensions = new ExtensionLoader(_extensionStateStore).Load();
        var extensionIds = extensions.Extensions.Select(extension => extension.Id).ToArray();
        var schemaLoader = new CommandSchemaLoader(extensions.Schema);
        var schema = schemaLoader.Load();
        var commands = schema.Commands
            .Where(command => !string.Equals(command.Name, SecretCommandDefinition.Definition.Name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.QualifiedName, ExtensionCommandModel.Definition.QualifiedName, StringComparison.OrdinalIgnoreCase))
            .Append(VariableCommandModel.Definition);
        commands = commands.Append(ExtensionCommandModel.Definition);

        schema = schema with
        {
            Commands = commands.Append(SecretCommandDefinition.Definition).ToArray()
        };
        var builtInKinds = new[] { "shell", "variable", "secret" };
        builtInKinds = [.. builtInKinds, "extension"];

        var catalog = new CommandCatalog(schema, extensions.Executors.Keys.Concat(builtInKinds));
        var variableExpander = new CommandTextVariableExpander(schema.Variables);
        var shortcutResolver = new ShortcutResolver(schema.Commands);
        var valueProvider = new CommandSchemaValueProvider(
            schema,
            new CompositeArgumentValueProvider(
                new SecretNamesArgumentValueProvider(_secretStore),
                new ExtensionIdsArgumentValueProvider(extensionIds)));
        var functionExpander = new CommandTextFunctionExpander(
            schema.Functions,
            new RegisteredCommandFunctionExecutor(extensionFunctionExecutors: extensions.FunctionExecutors));
        var completionSession = new CommandCompletionSession(
            catalog,
            valueProvider,
            schema.Variables,
            schema.Functions);
        var parser = new CommandLineParser(catalog, valueProvider);
        var urlCommandModel = new UrlCommandModel(new SystemUrlTargetOpener(), schema.Variables);
        var commandExecutors = new Dictionary<string, ICommandExecutor>(StringComparer.OrdinalIgnoreCase)
        {
            ["url"] = urlCommandModel,
            ["shell"] = new ShellCommandModel(_processStarter),
            ["variable"] = new VariableCommandModel(schemaLoader, updatedSchema =>
            {
                _variableExpander.UpdateVariables(updatedSchema.Variables);
                _urlCommandModel.UpdateVariables(updatedSchema.Variables);
                ApplyCompletionState(_completionSession.UpdateVariables(updatedSchema.Variables));
            })
        };
        commandExecutors["extension"] = new ExtensionCommandModel(
            _extensionStateStore,
            extensionIds,
            InitializeCommandRuntime);

        foreach (var (kind, executor) in extensions.Executors)
        {
            if (!commandExecutors.TryAdd(kind, executor))
            {
                throw new InvalidOperationException($"Command kind '{kind}' is already registered.");
            }
        }

        _extensions = extensions;
        _catalog = catalog;
        _variableExpander = variableExpander;
        _shortcutResolver = shortcutResolver;
        _functionExpander = functionExpander;
        _completionSession = completionSession;
        _parser = parser;
        _urlCommandModel = urlCommandModel;
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

    private bool _isValueOutput;

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

    public IBrush StatusMessageBrush => IsValueOutput ? ValueOutputBrush : StatusMessageBrushDefault;

    public bool IsValueOutput
    {
        get => _isValueOutput;
        private set
        {
            if (SetProperty(ref _isValueOutput, value))
            {
                OnPropertyChanged(nameof(StatusMessageBrush));
                OnPropertyChanged(nameof(IsStatusMessage));
            }
        }
    }

    public bool IsStatusMessage => !IsValueOutput;

    public bool IsChecklistActive => _completionSession.State.IsChecklistActive;

    public bool HasCompletionOptions => CompletionItems.Count > 0;

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

    public bool CanComplete => _isExtensionListVisible || _completionSession.State.CanComplete;

    public bool CanExpandTypedShortcut => _shortcutResolver.TryExpandAlias(CommandText, out _);

    public bool TryGetKeyboardShortcutExpansion(KeyboardShortcutChord chord, out string expansion)
        => _shortcutResolver.TryExpandChord(chord, out expansion!);

    public bool IsCommandTextEmpty => string.IsNullOrEmpty(CommandText);

    public async Task<bool> HandleInputKeyAsync(CommandInputKey key)
    {
        if (key == CommandInputKey.Enter)
        {
            if (!string.Equals(CommandText.Trim(), "!", StringComparison.Ordinal))
            {
                TryExpandTypedShortcut();
            }

            return await ExecuteCommandAsync();
        }

        if (_isExtensionListVisible
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.SelectAll or CommandInputKey.DeselectAll)
        {
            return false;
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
            if (string.Equals(value.Trim(), "!", StringComparison.Ordinal))
            {
                ShowExtensionList();
            }
        }
    }

    private void ApplyCompletionState(CommandCompletionState state)
    {
        _isExtensionListVisible = false;
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

    private void ShowExtensionList()
    {
        var displayLines = ExtensionCommandModel.FormatList(_extensions.Extensions, _extensionStateStore)
            .Split(Environment.NewLine, StringSplitOptions.None);
        UsageText = "Installed extensions";
        CompletionItems = displayLines
            .Select(line => new CompletionOptionViewModel(line, false, false))
            .ToArray();
        _isExtensionListVisible = true;
        OnPropertyChanged(nameof(HasCompletionOptions));
        OnPropertyChanged(nameof(CanComplete));
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
        if (string.Equals(CommandText.Trim(), "!", StringComparison.Ordinal))
        {
            SetStatusMessage(ExtensionCommandModel.FormatList(_extensions.Extensions, _extensionStateStore), isValueOutput: true);
            CommandText = string.Empty;
            return true;
        }

        var isPrintCommand = PrintValueCommandParser.TryParse(CommandText, out var printExpression);
        if (isPrintCommand && string.IsNullOrWhiteSpace(printExpression))
        {
            SetStatusMessage("A value expression is required.");
            return false;
        }

        var expansion = _variableExpander.Expand(isPrintCommand ? printExpression : CommandText);
        if (expansion.UnresolvedVariables.Count > 0)
        {
            SetStatusMessage($"Unknown variable(s): {string.Join(", ", expansion.UnresolvedVariables.Select(name => $"${name}"))}");
            return false;
        }

        string expandedText;
        try
        {
            expandedText = await _functionExpander.ExpandAsync(expansion.ExpandedText);
        }
        catch (Exception exception)
        {
            SetStatusMessage($"Function expansion failed: {exception.Message}");
            return false;
        }

        if (isPrintCommand)
        {
            SetStatusMessage(expandedText, isValueOutput: true);
            CommandText = string.Empty;
            return true;
        }

        if (_urlCommandModel.TryExecuteDirectUrl(expandedText, out var directUrlResult))
        {
            SetStatusMessage(directUrlResult.Message);
            if (!directUrlResult.Succeeded)
            {
                return false;
            }

            CommandText = string.Empty;
            return true;
        }

        var parseResult = _parser.Parse(expandedText);
        if (parseResult.Status == CommandParseStatus.Incomplete)
        {
            SetStatusMessage("Command is incomplete.");
            return false;
        }

        if (parseResult.Status == CommandParseStatus.Invalid)
        {
            SetStatusMessage(parseResult.Error ?? "Command is invalid.");
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
            SetStatusMessage($"Command kind '{parsedCommand.Definition.Kind}' is not supported.");
            return false;
        }

        SetStatusMessage(executionResult.Message);
        if (!executionResult.Succeeded)
        {
            return false;
        }

        CommandText = string.Empty;
        return true;
    }

    private void SetStatusMessage(string message, bool isValueOutput = false)
    {
        IsValueOutput = isValueOutput;
        StatusMessage = message;
    }

    private string FormatExtensionList()
    {
        if (_extensions.Extensions.Count == 0)
        {
            return "No extensions installed.";
        }

        return string.Join(
            Environment.NewLine,
            _extensions.Extensions.Select(extension =>
                $"{extension.Id} | {extension.Version} [{(_extensionStateStore.IsEnabled(extension.Id) ? "enabled" : "disabled")}]")
        );
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
