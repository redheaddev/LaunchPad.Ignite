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
using LaunchPad.Ignite.App.Views;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private CommandCompletionSession _completionSession = null!;
    private CommandCatalog _catalog = null!;
    private CommandLineParser _parser = null!;
    private ShortcutResolver _shortcutResolver = null!;
    private CommandTextVariableExpander _variableExpander = null!;
    private CommandTextFunctionExpander _functionExpander = null!;
    private readonly CommandLineTokenizer _tokenizer = new();
    private IReadOnlyDictionary<string, ICommandExecutor> _commandExecutors = null!;
    private IReadOnlyDictionary<string, IAsyncCommandExecutor> _asyncCommandExecutors = null!;
    private IReadOnlyDictionary<string, string> _commandSources = null!;
    private UrlCommandModel _urlCommandModel = null!;
    private DirectFilePathHandler _directFilePathHandler = null!;
    private CommandArgumentPathCompletion _argumentPathCompletion = null!;
    private CommandArgumentPathContext? _activeArgumentPathContext;
    private readonly SecretCommandModel _secretCommandModel;
    private readonly SecretAccessGate _secretAccessGate;
    private readonly DispatcherTimer _secretAccessStateTimer;
    private readonly ExtensionStateStore _extensionStateStore;
    private readonly ProfileCommandLogger _commandLogger;
    private IReadOnlyList<CommandHistoryEntry> _historyCommands = Array.Empty<CommandHistoryEntry>();
    private string _historyInputText = string.Empty;
    private int _highlightedCompletionIndex;
    private LoadedExtensions _extensions = null!;
    private IReadOnlyList<LoadedCommandPackInfo> _commandPacks = null!;
    private readonly ISecretStore _secretStore;
    private readonly ProfileSettingsStore _profileSettingsStore;
    private readonly IReadOnlyList<CommandSettingDefinition> _applicationSettings;
    private readonly IProcessStarter _processStarter;
    private readonly ISecretTerminalLauncher _terminalLauncher;
    private bool _isExtensionListVisible;
    private bool _isDirectPathCompletionActive;
    private IReadOnlyList<string> _directPathSuggestions = Array.Empty<string>();
    private int _directPathHighlightedIndex;
    private bool _directPathReplacesWholeInput;
    private bool _syncingInput;

    public MainViewModel()
    {
        _commandLogger = new ProfileCommandLogger();
        _commandLogger.StartSession();
        _historyCommands = _commandLogger.ReadHistoryEntries();
        _extensionStateStore = new ExtensionStateStore();
        _secretStore = PlatformSecretStore.Create();
        _profileSettingsStore = new ProfileSettingsStore();
        ColorThemeService.MigrateLegacyThemes(_profileSettingsStore);
        ColorThemeService.Apply(ColorThemeService.Resolve(
            _profileSettingsStore.GetValue("app:colorTheme", ColorTheme.Default.Id),
            _profileSettingsStore));
        _applicationSettings = new ApplicationSettingsLoader().Load();
        _processStarter = new SystemProcessStarter();
        _terminalLauncher = new SystemSecretTerminalLauncher(_processStarter);
        _secretAccessGate = new SecretAccessGate();
        _secretCommandModel = new SecretCommandModel(
            _secretStore,
            _secretAccessGate,
            new AvaloniaSecretEntryDialogService(),
            new AvaloniaClipboardService(GetSystemClipboard),
            _terminalLauncher);
        _directFilePathHandler = new DirectFilePathHandler(_processStarter, terminalLauncher: _terminalLauncher);
        _argumentPathCompletion = new CommandArgumentPathCompletion(_directFilePathHandler);
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
        var commandPacks = new CommandPackLoader(_extensionStateStore)
            .LoadWithSources(extensions.Schema, extensionIds);
        var commandPackIds = commandPacks.Packs.Select(pack => pack.Id).ToArray();
        var componentIds = extensionIds.Concat(commandPackIds).ToArray();
        var appSchema = commandPacks.Schema;
        var commandSources = extensions.CommandSources
            .Concat(commandPacks.CommandSources)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var schemaLoader = new CommandSchemaLoader(appSchema, commandPacks.PackedCommandNames);
        var schema = commandPacks.FilterDisabledCommands(schemaLoader.Load());
        var settings = _applicationSettings.Concat(schema.Settings).ToArray();
        var profileBackupService = new ProfileBackupService();
        var profileVariable = new CommandVariableDefinition("profile", profileBackupService.ProfileDirectory);
        var runtimeBaseVariables = schema.Variables
            .Where(variable => !string.Equals(variable.QualifiedName, profileVariable.QualifiedName, StringComparison.OrdinalIgnoreCase))
            .Append(profileVariable)
            .ToArray();
        schema = schema with
        {
            Settings = settings,
            Variables = GetEffectiveVariables(runtimeBaseVariables, settings)
        };
        var urlTargetOpener = new SystemUrlTargetOpener();
        extensions.ConfigureCommandExecutors(
            schema.Variables,
            _profileSettingsStore.GetValues(),
            name => TryGetSecret(name),
            url => urlTargetOpener.Open([new UrlTarget(url)]));

        var commands = schema.Commands
            .Where(command => !string.Equals(command.Name, SecretCommandDefinition.Definition.Name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.Name, ExitCommandModel.Definition.Name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.QualifiedName, ExtensionCommandModel.Definition.QualifiedName, StringComparison.OrdinalIgnoreCase))
            .Append(VariableCommandModel.Definition);
        commands = commands.Append(ExtensionCommandModel.Definition);

        schema = schema with
        {
            Commands = commands.Append(SecretCommandDefinition.Definition)
                .Append(SecretListCommandDefinition.Definition)
                .Append(ThemeCommandDefinition.Definition)
                .Append(ProfileBackupCommandDefinitions.Backup)
                .Append(ProfileBackupCommandDefinitions.Restore)
                .Append(ExitCommandModel.Definition)
                .ToArray()
        };
        var builtInKinds = new[] { "shell", "variable", "secret", "secretList", "theme", "exit", "profileBackup", "profileRestore" };
        builtInKinds = [.. builtInKinds, "extension"];

        var catalog = new CommandCatalog(schema, extensions.Executors.Keys.Concat(extensions.AsyncExecutors.Keys).Concat(builtInKinds));
        var variableExpander = new CommandTextVariableExpander(schema.Variables);
        var shortcutResolver = new ShortcutResolver(schema.Commands);
        var extensionPackageManager = new ExtensionPackageManager();
        var valueProvider = new CommandSchemaValueProvider(
            schema,
            new CompositeArgumentValueProvider(
                new SecretNamesArgumentValueProvider(_secretStore),
                new ThemeNamesArgumentValueProvider(_profileSettingsStore),
                new ProfileBackupFilesArgumentValueProvider(profileBackupService),
                new ExtensionPackageNamesArgumentValueProvider(extensionPackageManager),
                new ExtensionIdsArgumentValueProvider(extensionIds),
                new InstalledComponentIdsArgumentValueProvider(componentIds, _extensionStateStore)));
        var functionExpander = new CommandTextFunctionExpander(
            schema.Functions,
            new RegisteredCommandFunctionExecutor(extensionFunctionExecutors: extensions.FunctionExecutors));
        var completionSession = new CommandCompletionSession(
            catalog,
            valueProvider,
            schema.Variables,
            schema.Functions);
        var parser = new CommandLineParser(catalog, valueProvider);
        var urlCommandModel = new UrlCommandModel(urlTargetOpener, schema.Variables);
        var variableCommandModel = new VariableCommandModel(
            schemaLoader,
            updatedSchema =>
            {
                var customVariableNames = updatedSchema.Variables
                    .Select(variable => variable.QualifiedName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var updatedVariables = runtimeBaseVariables
                    .Where(variable => !customVariableNames.Contains(variable.QualifiedName))
                    .Concat(updatedSchema.Variables)
                    .ToArray();
                var effectiveVariables = GetEffectiveVariables(updatedVariables, settings);
                _variableExpander.UpdateVariables(effectiveVariables);
                _urlCommandModel.UpdateVariables(effectiveVariables);
                extensions.ConfigureCommandExecutors(
                    effectiveVariables,
                    _profileSettingsStore.GetValues(),
                    name => TryGetSecret(name),
                    url => urlTargetOpener.Open([new UrlTarget(url)]));
                ApplyCompletionState(_completionSession.UpdateVariables(effectiveVariables));
            },
            ConfirmVariableOverwrite);
        var commandExecutors = new Dictionary<string, ICommandExecutor>(StringComparer.OrdinalIgnoreCase)
        {
            ["url"] = urlCommandModel,
            ["shell"] = new ShellCommandModel(_processStarter),
            ["secretList"] = new SecretListCommandModel(_secretStore),
            ["theme"] = new ThemeCommandModel(_profileSettingsStore, ColorThemeService.Apply),
            ["profileBackup"] = new ProfileBackupCommandModel(profileBackupService),
            ["exit"] = new ExitCommandModel(ShutdownApplication),
            ["variable"] = variableCommandModel
        };
        var asyncCommandExecutors = new Dictionary<string, IAsyncCommandExecutor>(StringComparer.OrdinalIgnoreCase)
        {
            ["profileRestore"] = new ProfileRestoreCommandModel(profileBackupService, ConfirmProfileRestore)
        };
        commandExecutors["extension"] = new ExtensionCommandModel(
            _extensionStateStore,
            extensionIds,
            InitializeCommandRuntime,
            packageManager: extensionPackageManager,
            commandPackIds: commandPackIds,
            commandPackLoader: new CommandPackLoader(_extensionStateStore),
            confirmPackReplacement: ConfirmCommandPackReplacement);

        foreach (var (kind, executor) in extensions.Executors)
        {
            if (!commandExecutors.TryAdd(kind, executor))
            {
                throw new InvalidOperationException($"Command kind '{kind}' is already registered.");
            }
        }

        foreach (var (kind, executor) in extensions.AsyncExecutors)
        {
            if (commandExecutors.ContainsKey(kind) || !asyncCommandExecutors.TryAdd(kind, executor))
            {
                throw new InvalidOperationException($"Command kind '{kind}' is already registered.");
            }
        }

        _extensions = extensions;
        _commandPacks = commandPacks.Packs;
        _catalog = catalog;
        _variableExpander = variableExpander;
        _shortcutResolver = shortcutResolver;
        _functionExpander = functionExpander;
        _completionSession = completionSession;
        _parser = parser;
        _urlCommandModel = urlCommandModel;
        _commandExecutors = commandExecutors;
        _asyncCommandExecutors = asyncCommandExecutors;
        _commandSources = commandSources;
        ApplyCompletionState(_completionSession.State);
    }

    private IReadOnlyList<CommandVariableDefinition> GetEffectiveVariables(
        IEnumerable<CommandVariableDefinition> variables,
        IReadOnlyList<CommandSettingDefinition> settings)
    {
        var settingNames = settings
            .Select(setting => setting.QualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return variables
            .Where(variable => !settingNames.Contains(variable.QualifiedName))
            .Concat(settings.Select(setting => new CommandVariableDefinition(
                setting.Name,
                _profileSettingsStore.GetValue(setting.QualifiedName, setting.DefaultValue),
                setting.Namespace)))
            .ToArray();
    }

    public SettingsViewModel CreateSettingsViewModel()
        => new(_applicationSettings, _extensions, _profileSettingsStore, _extensionStateStore, InitializeCommandRuntime, ColorThemeService.Apply);

    public async Task ImportCommandPackAsync(string path)
    {
        var command = new ParsedCommand(
            ExtensionCommandModel.Definition,
            ExtensionCommandModel.Definition.Actions.Single(action => action.Name == "pack-install"),
            new Dictionary<string, string> { ["json-path"] = path });
        var model = (ExtensionCommandModel)_commandExecutors["extension"];
        var result = await model.ExecuteAsync(command);
        SetStatusMessage(result.Message);
    }

    private static Task<bool> ConfirmCommandPackReplacement(string id)
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mainWindow
                ? mainWindow.ConfirmCommandPackReplacementAsync(id)
                : Task.FromResult(false);

    private static Task<bool> ConfirmProfileRestore(string fileName)
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mainWindow
                ? mainWindow.ConfirmProfileRestoreAsync(fileName)
                : Task.FromResult(false);

    private static Task<bool> ConfirmVariableOverwrite(string name)
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mainWindow
                ? mainWindow.ConfirmVariableOverwriteAsync(name)
                : Task.FromResult(false);

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

    public string UsageCommandText { get; private set; } = string.Empty;

    [NotifyPropertyChangedFor(nameof(HasTargetPreview))]
    [NotifyPropertyChangedFor(nameof(TargetPreviewDisplay))]
    [ObservableProperty]
    private string _targetPreview = string.Empty;

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

    public IBrush StatusMessageBrush => Application.Current?.Resources[
        IsValueOutput ? "ThemeAccent" : "ThemeStatus"] as IBrush ?? Brushes.Gray;

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

    public bool HasTargetPreview => !string.IsNullOrWhiteSpace(TargetPreview);

    public string TargetPreviewDisplay => HasTargetPreview ? TargetPreview : "—";

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

    public bool CanComplete => _isExtensionListVisible
        || (_isDirectPathCompletionActive
            ? _directPathSuggestions.Count > 0
            : _completionSession.State.CanComplete
                || GetHistorySuggestions(CommandText).Count > 0
                || GetShortcutSuggestions(CommandText).Count > 0);

    public bool CanExpandTypedShortcut => _shortcutResolver.TryExpandAlias(CommandText, out _);

    public bool TryGetKeyboardShortcutExpansion(KeyboardShortcutChord chord, out string expansion)
        => _shortcutResolver.TryExpandChord(chord, out expansion!);

    public bool TryGetKeyboardShortcutExpansion(KeyboardShortcutChord chord, out string expansion, out bool executeImmediately)
        => _shortcutResolver.TryExpandChord(chord, out expansion!, out executeImmediately);

    public Task<bool> ExecuteShortcutAsync(string expansion)
    {
        CommandText = expansion;
        return ExecuteCommandAsync();
    }

    public bool DeleteSelectedHistoryEntry()
    {
        var historySuggestions = GetHistorySuggestions(CommandText);
        if (_highlightedCompletionIndex < 0 || _highlightedCompletionIndex >= historySuggestions.Count)
        {
            return false;
        }

        if (!_commandLogger.RemoveHistoryEntry(historySuggestions[_highlightedCompletionIndex].CommandText))
        {
            return false;
        }

        _historyCommands = _commandLogger.ReadHistoryEntries();
        _highlightedCompletionIndex = 0;
        ApplyCompletionState(_completionSession.UpdateInputText(CommandText));
        return true;
    }

    public bool OpenDirectPathInTerminal(string path)
    {
        if (!_directFilePathHandler.TryOpenInTerminal(path, out var result))
        {
            return false;
        }

        SetStatusMessage(result.Message);
        return true;
    }

    public bool ClearHistory()
    {
        if (!_commandLogger.ClearHistory())
        {
            return false;
        }

        _historyCommands = Array.Empty<CommandHistoryEntry>();
        _highlightedCompletionIndex = 0;
        ApplyCompletionState(_completionSession.UpdateInputText(CommandText));
        return true;
    }

    public bool IsCommandTextEmpty => string.IsNullOrEmpty(CommandText);

    public bool ShowCommandPlaceholder => IsCommandTextEmpty && string.IsNullOrEmpty(CompletionGhostText);

    public async Task<bool> HandleInputKeyAsync(
        CommandInputKey key,
        bool newWindow = false,
        CommandLaunchMode launchMode = CommandLaunchMode.Normal)
    {
        if (key == CommandInputKey.Enter)
        {
            if (TryGetShortcutFilter(CommandText, out _))
            {
                var matchingShortcuts = GetShortcutSuggestions(CommandText);
                if (matchingShortcuts.Count == 0)
                {
                    return false;
                }

                var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, matchingShortcuts.Count - 1);
                CommandText = matchingShortcuts[selectedIndex].Key;
                TryExpandTypedShortcut();
            }
            else if (!string.Equals(CommandText.Trim(), "!", StringComparison.Ordinal))
            {
                TryExpandTypedShortcut();
            }

            return await ExecuteCommandAsync(newWindow, launchMode);
        }

        if (_isExtensionListVisible
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                or CommandInputKey.SelectAll or CommandInputKey.DeselectAll)
        {
            return false;
        }

        if (_isDirectPathCompletionActive
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last)
        {
            HandleDirectPathCompletionKey(key);
            return false;
        }

        var shortcutList = GetShortcutSuggestions(CommandText);
        if (shortcutList.Count > 0
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                or CommandInputKey.Right)
        {
            if (key == CommandInputKey.Right)
            {
                var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, shortcutList.Count - 1);
                AcceptNextSuggestionToken(CommandText, shortcutList[selectedIndex].Key);
            }
            else if (key == CommandInputKey.Tab)
            {
                var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, shortcutList.Count - 1);
                CommandText = shortcutList[selectedIndex].Key;
            }
            else
            {
                _highlightedCompletionIndex = key switch
                {
                    CommandInputKey.First => 0,
                    CommandInputKey.Last => shortcutList.Count - 1,
                    _ => MoveCompletionIndex(_highlightedCompletionIndex, key, shortcutList.Count)
                };
                ApplyCompletionState(_completionSession.State);
            }

            return false;
        }

        var historySuggestions = GetHistorySuggestions(CommandText);
        if (historySuggestions.Count > 0
            && (key != CommandInputKey.Right || _highlightedCompletionIndex < historySuggestions.Count)
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                or CommandInputKey.Right)
        {
            var regularCount = _completionSession.State.Items.Count;
            var historyCount = historySuggestions.Count;
            if (key == CommandInputKey.Right && _highlightedCompletionIndex < historyCount)
            {
                var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, historyCount - 1);
                var selectedHistoryEntry = historySuggestions[selectedIndex];
                var isHistoryFilter = TryGetHistoryFilter(CommandText, out var historySearchText);
                var completionInput = isHistoryFilter
                    ? historySearchText
                    : CommandText;
                AcceptNextSuggestionToken(
                    completionInput,
                    selectedHistoryEntry.CommandText,
                    skipLeadingWhitespace: true);
                var updatedHistorySuggestions = GetHistorySuggestions(CommandText);
                var selectedHistoryIndex = updatedHistorySuggestions
                    .ToList()
                    .FindIndex(entry => entry == selectedHistoryEntry);
                if (selectedHistoryIndex >= 0)
                {
                    _highlightedCompletionIndex = selectedHistoryIndex;
                    ApplyCompletionState(_completionSession.State);
                }
            }
            else if (key == CommandInputKey.Tab)
            {
                if (_highlightedCompletionIndex < historyCount)
                {
                    CommandText = historySuggestions[_highlightedCompletionIndex].CommandText;
                }
                else
                {
                    SelectRegularCompletion(_highlightedCompletionIndex - historyCount);
                    ApplyCompletionState(_completionSession.HandleKey(CommandInputKey.Tab));
                }
            }
            else
            {
                var totalCount = historyCount + regularCount;
                _highlightedCompletionIndex = key switch
                {
                    CommandInputKey.First => 0,
                    CommandInputKey.Last => totalCount - 1,
                    _ => MoveCompletionIndex(_highlightedCompletionIndex, key, totalCount)
                };
                if (_highlightedCompletionIndex >= historyCount)
                {
                    SelectRegularCompletion(_highlightedCompletionIndex - historyCount);
                }

                ApplyCompletionState(_completionSession.State);
            }

            return false;
        }

        if (key == CommandInputKey.Right
            && (HasCompletionOptions || _completionSession.State.IsChecklistActive))
        {
            var previousText = CommandText;
            var completionState = _completionSession.HandleKey(key);
            ApplyCompletionState(completionState);
            if (!string.Equals(previousText, CommandText, StringComparison.Ordinal)
                && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is MainWindow mainWindow)
            {
                mainWindow.SetPendingCaretIndex(completionState.CaretIndex ?? CommandText.Length);
            }

            return false;
        }

        if (key == CommandInputKey.Tab && TryExpandTypedShortcut())
        {
            return false;
        }

        ApplyCompletionState(_completionSession.HandleKey(key));
        return false;
    }

    private void AcceptNextSuggestionToken(
        string input,
        string suggestion,
        bool skipLeadingWhitespace = false)
    {
        if (!suggestion.StartsWith(input, StringComparison.OrdinalIgnoreCase)
            || suggestion.Length == input.Length)
        {
            return;
        }

        var remaining = suggestion[input.Length..];
        var start = 0;
        if (skipLeadingWhitespace)
        {
            while (start < remaining.Length && char.IsWhiteSpace(remaining[start]))
            {
                start++;
            }
        }

        int acceptedLength;
        if (remaining.StartsWith(','))
        {
            acceptedLength = _tokenizer.FindTokenBoundary(remaining, 1);
        }
        else
        {
            var boundary = _tokenizer.FindTokenBoundary(remaining, start);
            acceptedLength = boundary;
            if (!skipLeadingWhitespace
                && boundary >= 0
                && boundary < remaining.Length
                && remaining[boundary] == ' ')
            {
                acceptedLength++;
            }
        }

        var updatedText = input + remaining[..acceptedLength];
        CommandText = updatedText;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mainWindow)
        {
            mainWindow.SetPendingCaretIndex(updatedText.Length);
        }
    }

    partial void OnCommandTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsCommandTextEmpty));
        OnPropertyChanged(nameof(ShowCommandPlaceholder));
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

    partial void OnCompletionGhostTextChanged(string value)
        => OnPropertyChanged(nameof(ShowCommandPlaceholder));

    private void ApplyCompletionState(CommandCompletionState state)
    {
        _isExtensionListVisible = false;
        if (!string.Equals(_historyInputText, state.InputText, StringComparison.Ordinal))
        {
            _historyInputText = state.InputText;
            _highlightedCompletionIndex = 0;
        }

        if (!string.Equals(CommandText, state.InputText, StringComparison.Ordinal))
        {
            _syncingInput = true;
            CommandText = state.InputText;
            _syncingInput = false;
        }

        UpdateCommandExpansionPreview(state.InputText);
        var historySuggestions = GetHistorySuggestions(state.InputText);
        var shortcutSuggestions = GetShortcutSuggestions(state.InputText);
        var selectedHistoryIndex = _highlightedCompletionIndex < historySuggestions.Count
            ? _highlightedCompletionIndex
            : -1;
        var selectedShortcutIndex = historySuggestions.Count == 0
            && _highlightedCompletionIndex < shortcutSuggestions.Count
                ? _highlightedCompletionIndex
                : -1;
        var selectedCommandText = selectedHistoryIndex >= 0
            ? historySuggestions[selectedHistoryIndex].CommandText
            : selectedShortcutIndex >= 0
                ? shortcutSuggestions[selectedShortcutIndex].Value
            : historySuggestions.Count > 0
                ? GetRegularCompletionValue(state, _highlightedCompletionIndex - historySuggestions.Count)
                : state.Items.FirstOrDefault(item => item.IsHighlighted)?.Value;
        var tokens = _tokenizer.Tokenize(state.InputText);
        var isCommandCompletion = tokens.Count == 0
            || (tokens.Count == 1 && !_tokenizer.EndsWithWhitespace(state.InputText));
        var inputIsExactCommand = tokens.Count == 1
            && _catalog.TryGetCommand(tokens[0].Value, out _);
        var previewCommandText = selectedHistoryIndex >= 0
            ? historySuggestions[selectedHistoryIndex].CommandText
            : isCommandCompletion && !inputIsExactCommand && selectedCommandText is not null
                ? selectedCommandText
                : state.InputText;
        TargetPreview = GetCommandTargetPreview(previewCommandText);
        if (_directFilePathHandler.TryGetCompletionPath(state.InputText, out var directPath))
        {
            _activeArgumentPathContext = null;
            ApplyDirectPathCompletion(
                directPath,
                replacesWholeInput: true,
                resetHighlight: true);
            return;
        }

        if (_argumentPathCompletion.TryGetContext(state.InputText, _catalog, _tokenizer, out var pathContext))
        {
            _activeArgumentPathContext = pathContext;
            ApplyDirectPathCompletion(
                pathContext.Path,
                replacesWholeInput: false,
                resetHighlight: true);
            return;
        }

        _activeArgumentPathContext = null;
        _isDirectPathCompletionActive = false;
        _directPathSuggestions = Array.Empty<string>();
        var selectedHistoryText = selectedHistoryIndex >= 0
            ? historySuggestions[selectedHistoryIndex].CommandText
            : null;
        var selectedHistoryPrefixLength = TryGetHistoryFilter(state.InputText, out var historySearchText)
            ? historySearchText.Length
            : state.InputText.Length;
        CompletionGhostText = TryGetShortcutFilter(state.InputText, out _)
            ? string.Empty
            : selectedHistoryText is not null
                ? selectedHistoryPrefixLength <= selectedHistoryText.Length
                    ? selectedHistoryText[selectedHistoryPrefixLength..]
                    : string.Empty
                : state.InputText.Length == 0 ? selectedCommandText ?? string.Empty : state.GhostText;
        var usage = GetUsageText(previewCommandText);
        UsageText = usage.Text;
        UsageCommandText = usage.CommandText;
        OnPropertyChanged(nameof(UsageCommandText));
        var regularItems = state.Items
            .Select((item, index) =>
            {
                var displayText = state.IsChecklistActive
                    ? $"{(item.IsSelected ? "[x]" : "[ ]")} {item.Value}"
                    : item.DisplayText ?? item.Value;
                var cyanText = string.Empty;
                var kindText = string.Empty;
                if (!state.IsChecklistActive
                    && _catalog.TryGetCommand(item.Value, out var command))
                {
                    var sourceLabel = _commandSources.TryGetValue(command.QualifiedName, out var sourceId)
                        ? $" [{sourceId}]"
                        : string.Empty;
                    displayText = item.Value;
                    cyanText = sourceLabel;
                }

                var listSuggestionCount = historySuggestions.Count + shortcutSuggestions.Count;
                var isHighlighted = listSuggestionCount == 0
                    ? item.IsHighlighted
                    : _highlightedCompletionIndex >= listSuggestionCount
                        && _highlightedCompletionIndex - listSuggestionCount == index;
                return new CompletionOptionViewModel(displayText, isHighlighted, item.IsSelected, cyanText, kindText);
            });
        var historyItems = historySuggestions.Select((entry, index) =>
        {
            var age = ProfileCommandLogger.FormatHistoryAge(entry.RunTime, DateTimeOffset.Now);
            var ageLabel = age.Length == 0 ? string.Empty : $" [{age}]";
            return new CompletionOptionViewModel(
                $"[history] {entry.CommandText}",
                index == selectedHistoryIndex,
                false,
                ageLabel);
        });
        var shortcutItems = shortcutSuggestions.Select((shortcut, index) =>
            new CompletionOptionViewModel(
                $"[shortcut] {shortcut.Key} → {shortcut.Value.TrimEnd()}",
                index == selectedShortcutIndex,
                false));
        CompletionItems = historyItems.Concat(shortcutItems).Concat(regularItems).ToArray();
        OnPropertyChanged(nameof(IsChecklistActive));
        OnPropertyChanged(nameof(HasCompletionOptions));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanExpandTypedShortcut));
    }

    private IReadOnlyList<CommandHistoryEntry> GetHistorySuggestions(string inputText)
    {
        if (TryGetHistoryFilter(inputText, out var searchText))
        {
            return searchText.Length == 0
                ? _historyCommands
                : ProfileCommandLogger.FilterHistoryEntriesByPrefix(_historyCommands, searchText);
        }

        return ProfileCommandLogger.FilterHistoryEntries(_historyCommands, inputText);
    }

    private static bool TryGetHistoryFilter(string inputText, out string searchText)
    {
        var trimmedInput = inputText.Trim();
        if (!trimmedInput.StartsWith('#'))
        {
            searchText = string.Empty;
            return false;
        }

        searchText = trimmedInput[1..].Trim();
        return true;
    }

    private IReadOnlyList<KeyValuePair<string, string>> GetShortcutSuggestions(string inputText)
        => TryGetShortcutFilter(inputText, out var prefix)
            ? _shortcutResolver.GetAliasExpansions(prefix)
            : Array.Empty<KeyValuePair<string, string>>();

    private static bool TryGetShortcutFilter(string inputText, out string prefix)
    {
        var trimmedInput = inputText.Trim();
        if (!trimmedInput.StartsWith('&'))
        {
            prefix = string.Empty;
            return false;
        }

        prefix = trimmedInput[1..].Trim();
        return true;
    }

    private static string? GetRegularCompletionValue(CommandCompletionState state, int index)
        => index >= 0 && index < state.Items.Count ? state.Items[index].Value : null;

    private static int MoveCompletionIndex(int index, CommandInputKey key, int count)
    {
        var offset = key switch
        {
            CommandInputKey.Up => -1,
            CommandInputKey.Down => 1,
            CommandInputKey.PageUp => -5,
            CommandInputKey.PageDown => 5,
            _ => 0
        };
        return (index + offset % count + count) % count;
    }

    private string GetCommandTargetPreview(string commandText)
    {
        var tokens = _tokenizer.Tokenize(commandText);
        if (tokens.Count == 0 || !_catalog.TryGetCommand(tokens[0].Value, out var command))
        {
            return string.Empty;
        }

        CommandActionDefinition? action = null;
        if (tokens.Count > 1 && _catalog.TryGetAction(command, tokens[1].Value, out var explicitAction))
        {
            action = explicitAction;
        }

        if (action is null && !_catalog.TryGetDefaultAction(command, out action!))
        {
            action = command.Actions.FirstOrDefault();
        }

        if (action is null)
        {
            return string.Empty;
        }

        var parseResult = _parser.Parse(commandText);
        if (parseResult.Status == CommandParseStatus.Valid
            && parseResult.ParsedCommand is { Definition.Kind: var kind } parsedCommand
            && string.Equals(kind, "url", StringComparison.OrdinalIgnoreCase))
        {
            var targetResult = _urlCommandModel.BuildPreview(parsedCommand);
            if (targetResult.Succeeded && targetResult.Targets.Count > 0)
            {
                var firstTarget = targetResult.Targets[0].Value;
                return targetResult.Targets.Count == 1
                    ? firstTarget
                    : $"{firstTarget} (+{targetResult.Targets.Count - 1} more)";
            }
        }

        var target = Environment.ExpandEnvironmentVariables(_variableExpander.Expand(action.TargetTemplate).ExpandedText);
        if (parseResult.Status == CommandParseStatus.Valid && parseResult.ParsedCommand is { } parsed)
        {
            foreach (var argument in parsed.Arguments)
            {
                target = target.Replace(
                    $"<{argument.Key}>",
                    argument.Value,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        return target;
    }

    private void SelectRegularCompletion(int targetIndex)
    {
        var items = _completionSession.State.Items;
        if (targetIndex < 0 || targetIndex >= items.Count)
        {
            return;
        }

        var currentIndex = items.ToList().FindIndex(item => item.IsHighlighted);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var steps = (targetIndex - currentIndex + items.Count) % items.Count;
        for (var index = 0; index < steps; index++)
        {
            _completionSession.HandleKey(CommandInputKey.Down);
        }
    }

    private void ApplyDirectPathCompletion(
        string path,
        bool replacesWholeInput,
        bool resetHighlight)
    {
        TargetPreview = string.Empty;
        _isDirectPathCompletionActive = true;
        _directPathReplacesWholeInput = replacesWholeInput;
        _directPathSuggestions = _directFilePathHandler.GetSuggestions(path);
        if (resetHighlight || _directPathHighlightedIndex >= _directPathSuggestions.Count)
        {
            _directPathHighlightedIndex = 0;
        }

        CompletionGhostText = string.Empty;
        UsageText = "Files and folders";
        UsageCommandText = string.Empty;
        OnPropertyChanged(nameof(UsageCommandText));
        CompletionItems = _directPathSuggestions
            .Select((path, index) => new CompletionOptionViewModel(
                path,
                index == _directPathHighlightedIndex,
                false))
            .ToArray();
        OnPropertyChanged(nameof(IsChecklistActive));
        OnPropertyChanged(nameof(HasCompletionOptions));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanExpandTypedShortcut));
    }

    private void HandleDirectPathCompletionKey(CommandInputKey key)
    {
        if (_directPathSuggestions.Count == 0)
        {
            return;
        }

        if (key == CommandInputKey.Tab)
        {
            if (DirectFilePathCompletion.TrySelectSuggestion(
                _directPathSuggestions,
                _directPathHighlightedIndex,
                out var selectedPath))
            {
                if (_directPathReplacesWholeInput)
                {
                    CommandText = _directFilePathHandler.GetSuggestionText(selectedPath);
                    if (CommandText.Length > selectedPath.Length
                        && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        && desktop.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.SetPendingCaretIndex(CommandText.Length - 1);
                    }
                }
                else
                {
                    CommandText = CommandArgumentPathCompletion.ReplaceValue(
                        CommandText,
                        _activeArgumentPathContext!,
                        selectedPath);
                }
            }

            return;
        }

        var suggestionCount = _directPathSuggestions.Count;
        _directPathHighlightedIndex = key switch
        {
            CommandInputKey.First => 0,
            CommandInputKey.Last => suggestionCount - 1,
            _ => MoveCompletionIndex(_directPathHighlightedIndex, key, suggestionCount)
        };
        ApplyDirectPathCompletion(
            _directPathSuggestions.Count > 0 ? GetCurrentDirectPathText() : string.Empty,
            _directPathReplacesWholeInput,
            resetHighlight: false);
    }

    private string GetCurrentDirectPathText()
    {
        if (_directPathReplacesWholeInput)
        {
            return _directFilePathHandler.TryGetCompletionPath(CommandText, out var currentPath)
                ? currentPath
                : CommandText;
        }

        var context = _activeArgumentPathContext!;
        var currentToken = CommandText.Substring(context.TokenStart, context.TokenLength);
        var equalsIndex = currentToken.IndexOf('=');
        return (equalsIndex >= 0 ? currentToken[(equalsIndex + 1)..] : currentToken).Trim('"');
    }

    private void ShowExtensionList()
    {
        TargetPreview = string.Empty;
        var displayLines = ExtensionCommandModel.FormatList(_extensions.Extensions, _commandPacks)
            .Split(Environment.NewLine, StringSplitOptions.None);
        UsageText = "Installed extensions and command packs";
        UsageCommandText = string.Empty;
        OnPropertyChanged(nameof(UsageCommandText));
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

public sealed record CompletionOptionViewModel(
    string DisplayText,
    bool IsHighlighted,
    bool IsSelected,
    string CyanText = "",
    string KindText = "");

    private (string Text, string CommandText) GetUsageText(string inputText)
    {
        var tokens = _tokenizer.Tokenize(inputText);
        if (tokens.Count == 0 || !_catalog.TryGetCommand(tokens[0].Value, out var command))
        {
            return (string.Empty, string.Empty);
        }

        var action = command.Actions.FirstOrDefault();
        if (tokens.Count > 1 && _catalog.TryGetAction(command, tokens[1].Value, out var explicitAction)
            && !string.Equals(explicitAction.Name, "default", StringComparison.OrdinalIgnoreCase))
        {
            action = explicitAction;
        }

        if (action is null)
        {
            return (string.Empty, string.Empty);
        }

        return ($"Usage: {action.Usage}", action.Usage);
    }

    private async Task<bool> ExecuteCommandAsync(
        bool newWindow = false,
        CommandLaunchMode launchMode = CommandLaunchMode.Normal)
    {
        var submittedText = CommandText;
        var runTime = DateTimeOffset.Now;
        var succeeded = false;
        try
        {
            succeeded = await ExecuteCommandCoreAsync(newWindow, launchMode);
            return succeeded;
        }
        finally
        {
            _commandLogger.RecordCommand(submittedText, succeeded, _catalog, runTime);
            _historyCommands = _commandLogger.ReadHistoryEntries();
            ApplyCompletionState(_completionSession.UpdateInputText(CommandText));
        }
    }

    private (bool Found, string Value) TryGetSecret(string name)
    {
        var found = _secretStore.TryGet(name, out var value);
        return (found, value);
    }

    private async Task<bool> ExecuteCommandCoreAsync(
        bool newWindow = false,
        CommandLaunchMode launchMode = CommandLaunchMode.Normal)
    {
        if (string.Equals(CommandText.Trim(), "!", StringComparison.Ordinal))
        {
            SetStatusMessage(ExtensionCommandModel.FormatList(_extensions.Extensions, _commandPacks), isValueOutput: true);
            CommandText = string.Empty;
            return true;
        }

        var isPrintCommand = PrintValueCommandParser.TryParse(CommandText, out var printExpression);
        if (isPrintCommand && string.IsNullOrWhiteSpace(printExpression))
        {
            SetStatusMessage("A value expression is required.");
            return false;
        }

        var isMathCommand = MathExpressionParser.TryParse(CommandText, out var mathExpression);
        if (isMathCommand)
        {
            if (string.IsNullOrWhiteSpace(mathExpression))
            {
                SetStatusMessage("A math expression is required.");
                return false;
            }

            if (!MathExpressionParser.TryEvaluate(mathExpression, out var value))
            {
                SetStatusMessage("Invalid math expression.");
                return false;
            }

            CommandText = $"={value.ToString("G", CultureInfo.InvariantCulture)}";
            SetStatusMessage(string.Empty);
            return true;
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

        if (launchMode == CommandLaunchMode.Normal
            && _directFilePathHandler.TryOpen(expandedText, out var directFileResult))
        {
            SetStatusMessage(directFileResult.Message);
            if (!directFileResult.Succeeded)
            {
                return false;
            }

            CommandText = string.Empty;
            return true;
        }

        var hasNewWindowOption = UrlCommandModel.TryRemoveNewWindowOption(expandedText, out var textWithoutNewWindowOption);
        if (launchMode == CommandLaunchMode.Normal
            && _urlCommandModel.TryExecuteDirectUrl(expandedText, out var directUrlResult, newWindow))
        {
            SetStatusMessage(directUrlResult.Message);
            if (!directUrlResult.Succeeded)
            {
                return false;
            }

            CommandText = string.Empty;
            return true;
        }

        if (launchMode == CommandLaunchMode.Normal
            && hasNewWindowOption
            && _urlCommandModel.TryExecuteDirectUrl(textWithoutNewWindowOption, out directUrlResult, newWindow: true))
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
        var parsedWithNewWindowOption = false;
        if (hasNewWindowOption)
        {
            var optionFreeParseResult = _parser.Parse(textWithoutNewWindowOption);
            if (optionFreeParseResult.Status == CommandParseStatus.Valid
                && string.Equals(optionFreeParseResult.ParsedCommand!.Definition.Kind, "url", StringComparison.OrdinalIgnoreCase))
            {
                parseResult = optionFreeParseResult;
                parsedWithNewWindowOption = true;
            }
        }

        if (parseResult.Status == CommandParseStatus.Incomplete)
        {
            var incompleteTokens = _tokenizer.Tokenize(expandedText);
            if (incompleteTokens.Count == 2
                && string.Equals(incompleteTokens[0].Value, "extension", StringComparison.OrdinalIgnoreCase)
                && string.Equals(incompleteTokens[1].Value, "install", StringComparison.OrdinalIgnoreCase)
                && _commandExecutors.TryGetValue("extension", out var extensionExecutor)
                && extensionExecutor is ExtensionCommandModel extensionModel)
            {
                var packageListResult = extensionModel.ListAvailablePackages();
                SetStatusMessage(packageListResult.Message, packageListResult.IsValueOutput);
                CommandText = string.Empty;
                return true;
            }

            SetStatusMessage("Command is incomplete.");
            return false;
        }

        if (parseResult.Status == CommandParseStatus.Invalid)
        {
            SetStatusMessage(parseResult.Error ?? "Command is invalid.");
            return false;
        }

        var parsedCommand = parseResult.ParsedCommand!;
        if (launchMode != CommandLaunchMode.Normal
            && !string.Equals(parsedCommand.Definition.Kind, "shell", StringComparison.OrdinalIgnoreCase))
        {
            SetStatusMessage("Elevated and alternate-user launch is only supported for shell commands.");
            return false;
        }

        CommandExecutionResult executionResult;
        if (string.Equals(parsedCommand.Definition.Kind, "secret", StringComparison.OrdinalIgnoreCase))
        {
            executionResult = await _secretCommandModel.ExecuteAsync(parsedCommand);
            RefreshSecretAccessState();
        }
        else if (_commandExecutors.TryGetValue(parsedCommand.Definition.Kind, out var commandExecutor))
        {
            executionResult = commandExecutor switch
            {
                ExtensionCommandModel extensionCommandModel => await extensionCommandModel.ExecuteAsync(parsedCommand),
                VariableCommandModel variableCommandModel => await variableCommandModel.ExecuteAsync(parsedCommand),
                UrlCommandModel urlCommandModel => urlCommandModel.Execute(parsedCommand, newWindow || parsedWithNewWindowOption),
                ShellCommandModel shellCommandModel => shellCommandModel.Execute(parsedCommand, launchMode),
                _ => commandExecutor.Execute(parsedCommand)
            };
        }
        else if (_asyncCommandExecutors.TryGetValue(parsedCommand.Definition.Kind, out var asyncCommandExecutor))
        {
            executionResult = await asyncCommandExecutor.ExecuteAsync(parsedCommand);
        }
        else
        {
            SetStatusMessage($"Command kind '{parsedCommand.Definition.Kind}' is not supported.");
            return false;
        }

        SetStatusMessage(executionResult.Message, executionResult.IsValueOutput);
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

    private static void ShutdownApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Shutdown();
        }
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

    public async Task SubmitExpandedShortcut(string expansion)
    {
        // Do not modify the CommandText directly; reuse the existing execution path.
        await ExecuteCommandCoreAsync(false, CommandLaunchMode.Normal, expansion);
    }

    private async Task<bool> ExecuteCommandCoreAsync(
        bool newWindow,
        CommandLaunchMode launchMode,
        string expandedText)
    {
        var hasNewWindowOption = UrlCommandModel.TryRemoveNewWindowOption(expandedText, out var textWithoutNewWindowOption);
        if (launchMode == CommandLaunchMode.Normal
            && _urlCommandModel.TryExecuteDirectUrl(expandedText, out var directUrlResult, newWindow))
        {
            SetStatusMessage(directUrlResult.Message);
            if (!directUrlResult.Succeeded)
            {
                return false;
            }

            CommandText = string.Empty;
            return true;
        }

        if (launchMode == CommandLaunchMode.Normal
            && hasNewWindowOption
            && _urlCommandModel.TryExecuteDirectUrl(textWithoutNewWindowOption, out directUrlResult, newWindow: true))
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
        var parsedWithNewWindowOption = false;
        if (hasNewWindowOption)
        {
            var optionFreeParseResult = _parser.Parse(textWithoutNewWindowOption);
            if (optionFreeParseResult.Status == CommandParseStatus.Valid
                && string.Equals(optionFreeParseResult.ParsedCommand!.Definition.Kind, "url", StringComparison.OrdinalIgnoreCase))
            {
                parseResult = optionFreeParseResult;
                parsedWithNewWindowOption = true;
            }
        }

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
        if (launchMode != CommandLaunchMode.Normal
            && !string.Equals(parsedCommand.Definition.Kind, "shell", StringComparison.OrdinalIgnoreCase))
        {
            SetStatusMessage("Elevated and alternate-user launch is only supported for shell commands.");
            return false;
        }

        CommandExecutionResult executionResult;
        if (string.Equals(parsedCommand.Definition.Kind, "secret", StringComparison.OrdinalIgnoreCase))
        {
            executionResult = await _secretCommandModel.ExecuteAsync(parsedCommand);
            RefreshSecretAccessState();
        }
        else if (_commandExecutors.TryGetValue(parsedCommand.Definition.Kind, out var commandExecutor))
        {
            executionResult = commandExecutor switch
            {
                ExtensionCommandModel extensionCommandModel => await extensionCommandModel.ExecuteAsync(parsedCommand),
                VariableCommandModel variableCommandModel => await variableCommandModel.ExecuteAsync(parsedCommand),
                UrlCommandModel urlCommandModel => urlCommandModel.Execute(parsedCommand, newWindow || parsedWithNewWindowOption),
                ShellCommandModel shellCommandModel => shellCommandModel.Execute(parsedCommand, launchMode),
                _ => commandExecutor.Execute(parsedCommand)
            };
        }
        else if (_asyncCommandExecutors.TryGetValue(parsedCommand.Definition.Kind, out var asyncCommandExecutor))
        {
            executionResult = await asyncCommandExecutor.ExecuteAsync(parsedCommand);
        }
        else
        {
            SetStatusMessage($"Command kind '{parsedCommand.Definition.Kind}' is not supported.");
            return false;
        }

        SetStatusMessage(executionResult.Message, executionResult.IsValueOutput);
        if (!executionResult.Succeeded)
        {
            return false;
        }

        CommandText = string.Empty;
        return true;
    }

}
