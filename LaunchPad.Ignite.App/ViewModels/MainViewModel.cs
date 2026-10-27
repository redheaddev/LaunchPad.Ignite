using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Clipboard;
using LaunchPad.Ignite.App.Services.Completion;
using LaunchPad.Ignite.App.Services.DirectFiles;
using LaunchPad.Ignite.App.Services.Hotkeys;
using LaunchPad.Ignite.App.Services.Macros;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.App.Services.Secrets;
using LaunchPad.Ignite.App.Services.Snippets;
using LaunchPad.Ignite.App.Services.ShellCommands;
using LaunchPad.Ignite.App.Services.UrlTargets;
using LaunchPad.Ignite.App.Services.Terminals;
using LaunchPad.Ignite.App.Services.Themes;
using LaunchPad.Ignite.App.Services.WindowBehavior;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;
using LaunchPad.Ignite.App.Views;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const string HistoryIcon = "↶";
    private const string VerboseLoggingSettingName = "app:verboseLogging";

    public event Action<string?>? CommandEditorRequested;
    public event Action<LauncherNavigationTarget>? NavigationRequested;
    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown";
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
    private IReadOnlyDictionary<string, string> _commandPackSources = null!;
    private IReadOnlySet<string> _userCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private CommandSchemaLoader _commandSchemaLoader = null!;
    private CommandPackLoader _commandPackLoader = null!;
    private DefinitionManagementCommandModel _definitionManagementCommandModel = null!;
    private SnippetStore _snippetStore = null!;
    private MacroStore _macroStore = null!;
    private MacroInvocationParser _macroInvocationParser = null!;
    private MacroExecutionEngine _macroExecutionEngine = null!;
    private MacroCompletionSession _macroCompletionSession = null!;
    private MacroSecretAccessDetector _macroSecretAccessDetector = null!;
    private IReadOnlyList<CommandVariableDefinition> _macroVariables = [];
    private UrlCommandModel _urlCommandModel = null!;
    private DirectFilePathHandler _directFilePathHandler = null!;
    private CommandArgumentPathCompletion _argumentPathCompletion = null!;
    private CommandArgumentPathContext? _activeArgumentPathContext;
    private SecretCommandModel _secretCommandModel = null!;
    private readonly SecretAccessGate _secretAccessGate;
    private readonly DispatcherTimer _secretAccessStateTimer;
    private AvaloniaClipboardService? _clipboardService;
    private readonly ExtensionStateStore _extensionStateStore;
    private readonly ProfileCommandLogger _commandLogger;
    private readonly HistorySuggestionCycler _historySuggestionCycler = new();
    private readonly CommandInputUndoHistory _commandInputUndoHistory = new();
    private IReadOnlyList<CommandHistoryEntry> _historyCommands = Array.Empty<CommandHistoryEntry>();
    private string _historyInputText = string.Empty;
    private int _commandInputCaretIndex;
    private CommandMetadataFilter? _activeCommandMetadataFilter;
    private string _commandMetadataFilterPrefix = string.Empty;
    private CommandMetadataFilterInput _commandMetadataFilterInput;
    private IReadOnlyList<string> _commandMetadataFilterValueSuggestions = Array.Empty<string>();
    private int _highlightedCompletionIndex;
    private LoadedExtensions _extensions = null!;
    private IReadOnlyList<LoadedCommandPackInfo> _commandPacks = null!;
    private IReadOnlyList<CommandListDefinition> _commandListDefaults = [];
    private readonly ISecretStore _secretStore;
    private readonly ProfileSettingsStore _profileSettingsStore;
    private readonly LauncherWindowStateStore _launcherWindowStateStore;
    private readonly IReadOnlyList<CommandSettingDefinition> _applicationSettings;
    private readonly IProcessStarter _processStarter;
    private readonly ISecretTerminalLauncher _terminalLauncher;
    private bool _isExtensionListVisible;
    private bool _isDirectPathCompletionActive;
    private bool _isManagementCompletionActive;
    private bool _isEditingCommandMetadataFilter;
    private string _managementCompletionInput = string.Empty;
    private bool _isHistoryPathStepping;
    private IReadOnlyList<string> _directPathSuggestions = Array.Empty<string>();
    private int _directPathHighlightedIndex;
    private bool _directPathReplacesWholeInput;
    private bool _syncingInput;
    private bool _isMiniMode;
    private bool _isUndoingCommandInput;

    public string GlobalActivationHotkeyValue => _profileSettingsStore.GetValue(
        "app:globalActivationHotkey",
        GlobalActivationHotkey.DefaultValue);

    public bool StartMinimized => string.Equals(
        _profileSettingsStore.GetValue("app:startMinimized", "Off"),
        "On",
        StringComparison.OrdinalIgnoreCase);

    public bool KeepWindowOpen => string.Equals(
        _profileSettingsStore.GetValue("app:keepWindowOpen", "Off"),
        "On",
        StringComparison.OrdinalIgnoreCase);

    public bool VerboseLoggingEnabled => string.Equals(
        _profileSettingsStore.GetValue(VerboseLoggingSettingName, "Off"),
        "On",
        StringComparison.OrdinalIgnoreCase);

    public bool IsMiniMode
    {
        get => _isMiniMode;
        private set
        {
            if (SetProperty(ref _isMiniMode, value))
            {
                OnPropertyChanged(nameof(IsMainMode));
            }
        }
    }

    public bool IsMainMode => !IsMiniMode;

    public LauncherWindowMode CurrentWindowMode => _launcherWindowStateStore.Mode;

    public MainViewModel()
    {
        _commandLogger = new ProfileCommandLogger();
        _commandLogger.StartSession();
        _historyCommands = _commandLogger.ReadHistoryEntries();
        _extensionStateStore = new ExtensionStateStore();
        _profileSettingsStore = new ProfileSettingsStore();
        VerboseLogging.Configure(VerboseLoggingEnabled);
        _secretStore = new SecretActivityTrackingStore(PlatformSecretStore.Create(), _profileSettingsStore);
        _launcherWindowStateStore = new LauncherWindowStateStore(_profileSettingsStore);
        _isMiniMode = _launcherWindowStateStore.Mode == LauncherWindowMode.Mini;
        ColorThemeService.MigrateLegacyThemes(_profileSettingsStore);
        ColorThemeService.Apply(ColorThemeService.Resolve(
            _profileSettingsStore.GetValue("app:colorTheme", ColorTheme.Default.Id),
            _profileSettingsStore));
        _applicationSettings = new ApplicationSettingsLoader().Load();
        _processStarter = new SystemProcessStarter();
        _terminalLauncher = new SystemSecretTerminalLauncher(_processStarter);
        _secretAccessGate = new SecretAccessGate();
        _directFilePathHandler = new DirectFilePathHandler(_processStarter, terminalLauncher: _terminalLauncher);
        _argumentPathCompletion = new CommandArgumentPathCompletion(_directFilePathHandler);
        InitializeCommandRuntime();
        _secretAccessStateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _secretAccessStateTimer.Tick += (_, _) =>
        {
            RefreshSecretAccessState();
            RefreshClipboardExpiry();
        };
        _secretAccessStateTimer.Start();
        RefreshSecretAccessState();
    }

    public void ToggleWindowMode()
    {
        if (LauncherModeCommandParser.TryParse("toggle", CurrentWindowMode, out var targetMode))
        {
            SetWindowMode(targetMode);
        }
    }

    public void SetWindowMode(LauncherWindowMode mode)
    {
        _launcherWindowStateStore.SetMode(mode);
        IsMiniMode = mode == LauncherWindowMode.Mini;
    }

    public void RequestNavigation(LauncherNavigationTarget target)
        => NavigationRequested?.Invoke(target);

    public HelpViewModel CreateHelpViewModel()
        => new(_catalog.Schema.Commands, _commandSources);

    public async Task ToggleSecretAccessAsync()
    {
        RefreshSecretAccessState();
        var action = SecretCommandDefinition.Definition.Actions
            .First(candidate => string.Equals(candidate.Name, "toggle", StringComparison.OrdinalIgnoreCase));
        var parsedCommand = new ParsedCommand(
            SecretCommandDefinition.Definition,
            action,
            new Dictionary<string, string>());
        var result = await _secretCommandModel.ExecuteAsync(parsedCommand);
        SetStatusMessage(result.Message);
        RefreshSecretAccessState();
    }

    public bool TryGetWindowPosition(LauncherWindowMode mode, out LauncherWindowPosition position)
        => _launcherWindowStateStore.TryGetPosition(mode, out position);

    public void SaveWindowPosition(LauncherWindowMode mode, LauncherWindowPosition position)
        => _launcherWindowStateStore.SetPosition(mode, position);

    public bool TryGetWindowSize(LauncherWindowMode mode, out LauncherWindowSize size)
        => _launcherWindowStateStore.TryGetSize(mode, out size);

    public void SaveWindowSize(LauncherWindowMode mode, LauncherWindowSize size)
        => _launcherWindowStateStore.SetSize(mode, size);

    private void InitializeCommandRuntime()
    {
        VerboseLogging.Configure(VerboseLoggingEnabled);
        var previousExtensions = _extensions;
        var extensions = new ExtensionLoader(_extensionStateStore).Load();
        var profileSettings = _profileSettingsStore.GetValues();
        var extensionSecretAccessPolicy = new ExtensionSecretAccessPolicy(
            _profileSettingsStore,
            _secretStore);
        Func<string, IReadOnlyList<string>, Task<ExtensionSecretResolutionResult>> resolveExtensionSecrets = async (extensionId, names) =>
        {
            var result = await _secretCommandModel.ResolveExtensionSecretsAsync(extensionId, names);
            return result;
        };
        _clipboardService = new AvaloniaClipboardService(GetSystemClipboard);
        _secretCommandModel = new SecretCommandModel(
            _secretStore,
            _secretAccessGate,
            new AvaloniaSecretEntryDialogService(),
            _clipboardService,
            _terminalLauncher,
            extensionSecretAccessPolicy,
            _profileSettingsStore);
        var extensionIds = extensions.Extensions.Select(extension => extension.Id).ToArray();
        _commandPackLoader = new CommandPackLoader(_extensionStateStore);
        var commandPacks = _commandPackLoader
            .LoadWithSources(extensions.Schema, extensionIds);
        var commandListDefaults = commandPacks.Schema.Lists;
        var commandPackIds = commandPacks.Packs.Select(pack => pack.Id).ToArray();
        var componentIds = extensionIds.Concat(commandPackIds).ToArray();
        var appSchema = commandPacks.Schema with
        {
            Commands = commandPacks.Schema.Commands
                .Append(LauncherModeCommandModel.Definition)
                .Concat(NavigationCommandModel.Definitions)
                .ToArray()
        };
        var commandSources = extensions.CommandSources;
        var commandPackSources = commandPacks.CommandSources;
        _commandSchemaLoader = new CommandSchemaLoader(appSchema, commandPacks.PackedCommandNames, _profileSettingsStore);
        var schema = commandPacks.FilterDisabledCommands(_commandSchemaLoader.Load());
        schema = schema with
        {
            Commands = MediaShortcutSettings.ApplyOverrides(schema.Commands, profileSettings)
        };
        _definitionManagementCommandModel = new DefinitionManagementCommandModel(_commandSchemaLoader, InitializeCommandRuntime);
        _userCommandNames = _commandSchemaLoader.UserCommandNames;
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
            profileSettings,
            (extensionNamespace, name) => extensionSecretAccessPolicy.TryGetSecret(extensionNamespace, name),
            url => urlTargetOpener.Open([new UrlTarget(url)]),
            resolveExtensionSecrets);

        var commands = schema.Commands
            .Where(command => !string.Equals(command.Name, SecretCommandDefinition.Definition.Name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.Name, ExitCommandModel.Definition.Name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.QualifiedName, ExtensionCommandModel.Definition.QualifiedName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.Name, SnippetCommandDefinition.Definition.Name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.Name, MacroCommandDefinition.Definition.Name, StringComparison.OrdinalIgnoreCase))
            .Append(VariableCommandModel.Definition);
        commands = commands.Append(ExtensionCommandModel.Definition);

        schema = schema with
        {
            Commands = commands.Append(SecretCommandDefinition.Definition)
                .Append(DefinitionManagementCommandModel.Definition)
                .Append(SecretListCommandDefinition.Definition)
                .Append(ThemeCommandDefinition.Definition)
                .Append(EditCommandDefinition.Definition)
                .Append(ProfileBackupCommandDefinitions.Backup)
                .Append(ProfileBackupCommandDefinitions.Restore)
                .Append(SnippetCommandDefinition.Definition)
                .Append(MacroCommandDefinition.Definition)
                .Append(ExitCommandModel.Definition)
                .ToArray()
        };
        schema = schema with
        {
            Lists = new ListDefaultOverrideStore(_profileSettingsStore).ApplyOverrides(schema.Lists)
        };
        var builtInKinds = new[] { "shell", "variable", "secret", "secretList", "theme", "edit", "exit", "profileBackup", "profileRestore", "snippet", "macro", "extension", "shortcut", "definitionManagement", "mode", "navigation" };

        var catalog = new CommandCatalog(schema, extensions.Executors.Keys.Concat(extensions.AsyncExecutors.Keys).Concat(builtInKinds));
        var variableExpander = new CommandTextVariableExpander(schema.Variables);
        var shortcutResolver = new ShortcutResolver(schema.Commands);
        var extensionPackageManager = new ExtensionPackageManager();
        _snippetStore = new SnippetStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchPad.Ignite", "snippets"),
            _profileSettingsStore);
        _macroStore = new MacroStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchPad.Ignite", "macros"),
            _profileSettingsStore);
        var valueProvider = new CommandSchemaValueProvider(
            schema,
            new CompositeArgumentValueProvider(
                new SnippetNamesArgumentValueProvider(_snippetStore),
                new MacroNamesArgumentValueProvider(_macroStore),
                new SecretNamesArgumentValueProvider(_secretStore),
                new ThemeNamesArgumentValueProvider(_profileSettingsStore),
                new CommandNamesArgumentValueProvider(() => _catalog?.Commands.ToArray() ?? []),
                new ProfileBackupFilesArgumentValueProvider(profileBackupService),
                new ExtensionPackageNamesArgumentValueProvider(extensionPackageManager),
                new ExtensionIdsArgumentValueProvider(extensionIds),
                new InstalledComponentIdsArgumentValueProvider(componentIds, _extensionStateStore)));
        _macroInvocationParser = new MacroInvocationParser(
            _macroStore,
            new MacroArgumentBinder(valueProvider));
        _macroExecutionEngine = new MacroExecutionEngine();
        _macroSecretAccessDetector = new MacroSecretAccessDetector(catalog);
        _macroCompletionSession = new MacroCompletionSession(
            _macroStore.GetAll(),
            valueProvider,
            schema.Variables,
            schema.Functions);
        _macroVariables = schema.Variables;
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
            _commandSchemaLoader,
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
                _macroVariables = effectiveVariables;
                _macroCompletionSession.UpdateVariables(effectiveVariables);
                _urlCommandModel.UpdateVariables(effectiveVariables);
                extensions.ConfigureCommandExecutors(
                    effectiveVariables,
                    _profileSettingsStore.GetValues(),
                    (extensionNamespace, name) => extensionSecretAccessPolicy.TryGetSecret(extensionNamespace, name),
                    url => urlTargetOpener.Open([new UrlTarget(url)]),
                    resolveExtensionSecrets);
                ApplyCompletionState(_completionSession.UpdateVariables(effectiveVariables));
            },
            ConfirmVariableOverwrite);
        var commandExecutors = new Dictionary<string, ICommandExecutor>(StringComparer.OrdinalIgnoreCase)
        {
            ["edit"] = new EditCommandModel(name => CommandEditorRequested?.Invoke(name)),
            ["mode"] = new LauncherModeCommandModel(() => CurrentWindowMode, SetWindowMode),
            ["navigation"] = new NavigationCommandModel(target => NavigationRequested?.Invoke(target)),
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
            ["profileRestore"] = new ProfileRestoreCommandModel(profileBackupService, ConfirmProfileRestore),
            ["macro"] = new MacroCommandModel(_macroStore, new AvaloniaMacroDialogService(), InitializeCommandRuntime),
            ["snippet"] = new SnippetCommandModel(
                _snippetStore,
                _secretStore,
                new AvaloniaSnippetDialogService(),
                _secretAccessGate,
                new AvaloniaSecretEntryDialogService(),
                _terminalLauncher)
        };
        commandExecutors["extension"] = new ExtensionCommandModel(
            _extensionStateStore,
            extensionIds,
            InitializeCommandRuntime,
            packageManager: extensionPackageManager,
            commandPackIds: commandPackIds,
            commandPackLoader: new CommandPackLoader(_extensionStateStore),
            confirmPackReplacement: ConfirmCommandPackReplacement,
            existingCommandNames: packId => _catalog.Commands
                .Where(command => !_commandPackSources.TryGetValue(command.QualifiedName, out var sourcePackId)
                    || !string.Equals(sourcePackId, packId, StringComparison.OrdinalIgnoreCase))
                .Select(command => command.QualifiedName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

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
        _commandListDefaults = commandListDefaults;
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
        _commandPackSources = commandPackSources;
        ApplyCompletionState(_completionSession.State);

        if (previousExtensions is not null)
        {
            foreach (var executor in previousExtensions.ShutdownAwareExecutors)
            {
                try
                {
                    executor.Shutdown();
                }
                catch (Exception)
                {
                }
            }

            foreach (var loadContext in previousExtensions.LoadContexts)
            {
                if (loadContext.IsCollectible)
                {
                    loadContext.Unload();
                }
            }
        }
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
    {
        return new(
            _applicationSettings,
            _extensions,
            _profileSettingsStore,
            _extensionStateStore,
            InitializeCommandRuntime,
            ColorThemeService.Apply,
            _secretStore.GetNames(),
            _catalog.Commands.ToArray(),
            _catalog.Schema.Lists,
            _commandSources,
            _userCommandNames,
            SaveUserCommandDefinition,
            _commandPackSources,
            SaveOwnedCommandDefinition,
            _commandPacks,
            _commandListDefaults,
            _secretStore.GetNames(),
            _snippetStore.GetAll(),
            _macroStore.GetAll(),
            _catalog.Schema.Functions,
            _catalog.Schema.Variables);
    }

    private bool SaveOwnedCommandDefinition(string? packId, CommandDefinition command, CommandDefinition original)
    {
        try
        {
            if (_catalog.Schema.Commands.Any(existing =>
                !string.Equals(existing.QualifiedName, original.QualifiedName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.QualifiedName, command.QualifiedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Command name '{command.QualifiedName}' is already in use.");
            }

            if (packId is null)
            {
                _commandSchemaLoader.UpdateUserCommand(command, original.QualifiedName);
            }
            else
            {
                _commandPackLoader.UpdatePackCommand(packId, original.QualifiedName, command);
            }

            InitializeCommandRuntime();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DataException or IOException or ArgumentException)
        {
            SetStatusMessage($"Could not save command: {exception.Message}");
            return false;
        }
    }

    private bool SaveUserCommandDefinition(CommandDefinition command)
    {
        try
        {
            _commandSchemaLoader.UpdateUserCommand(command);
            InitializeCommandRuntime();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DataException or IOException or ArgumentException)
        {
            SetStatusMessage($"Could not save command: {exception.Message}");
            return false;
        }
    }

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

    public ImportExportViewModel CreateImportExportViewModel(Action close)
    {
        var reservedCommandNames = _catalog.Schema.Commands
            .Where(command => !_userCommandNames.Contains(command.QualifiedName)
                && !_commandPackSources.ContainsKey(command.QualifiedName))
            .Select(command => command.QualifiedName)
            .Concat(_commandPackSources.Keys.Where(name => !_commandPacks
                .Where(pack => pack.Enabled && _extensionStateStore.IsEnabled(pack.Id))
                .Any(pack => pack.Commands.Contains(name, StringComparer.OrdinalIgnoreCase))))
            .ToArray();
        var manager = new CommandOwnershipManager(
            _commandSchemaLoader,
            _commandPackLoader,
            _extensionStateStore,
            _commandPacks,
            reservedCommandNames,
            InitializeCommandRuntime);
        return new ImportExportViewModel(
            manager,
            ImportCommandPackForDialogAsync,
            path => CommandPackLoader.PreviewPack(path, GetCommandOwnerLabel),
            close,
            new AddonPackageService(
                _commandPackLoader,
                _snippetStore,
                new ExtensionPackageManager(),
                GetCommandOwnerLabel,
                _macroStore),
            InitializeCommandRuntime);
    }

    private string? GetCommandOwnerLabel(string qualifiedName)
    {
        if (_userCommandNames.Contains(qualifiedName))
        {
            return "User commands";
        }

        if (_commandPackSources.TryGetValue(qualifiedName, out var packId))
        {
            return $"Pack: {packId}";
        }

        if (_commandSources.TryGetValue(qualifiedName, out var extensionId))
        {
            return $"Extension: {extensionId}";
        }

        return _catalog.Commands.Any(command => string.Equals(command.QualifiedName, qualifiedName, StringComparison.OrdinalIgnoreCase))
            ? "App commands"
            : null;
    }

    private async Task<CommandExecutionResult> ImportCommandPackForDialogAsync(string path, bool overwriteExisting)
    {
        var command = new ParsedCommand(
            ExtensionCommandModel.Definition,
            ExtensionCommandModel.Definition.Actions.Single(action => action.Name == "pack-install"),
            new Dictionary<string, string>
            {
                ["json-path"] = path,
                ["conflict-policy"] = overwriteExisting ? "overwrite" : "skip"
            });
        var model = (ExtensionCommandModel)_commandExecutors["extension"];
        return await model.ExecuteAsync(command);
    }

    private static Task<bool> ConfirmCommandPackReplacement(string id)
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mainWindow
                ? mainWindow.ConfirmCommandPackReplacementAsync(id)
                : Task.FromResult(false);

    private static Task<bool> ConfirmAddonReplacementAsync(AddonImportPreview preview)
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mainWindow
                ? mainWindow.ConfirmAddonReplacementAsync(preview)
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
    [NotifyPropertyChangedFor(nameof(MiniUsageStatusText))]
    private string _commandInputText = string.Empty;

    [ObservableProperty]
    private string _completionGhostText = string.Empty;

    private string _commandExpansionPreview = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MiniUsageStatusText))]
    private IReadOnlyList<CompletionOptionViewModel> _completionItems = Array.Empty<CompletionOptionViewModel>();

    [NotifyPropertyChangedFor(nameof(HasUsageText))]
    [NotifyPropertyChangedFor(nameof(MiniUsageStatusText))]
    [ObservableProperty]
    private string _usageText = string.Empty;

    public string UsageCommandText { get; private set; } = string.Empty;

    [NotifyPropertyChangedFor(nameof(HasTargetPreview))]
    [NotifyPropertyChangedFor(nameof(TargetPreviewDisplay))]
    [ObservableProperty]
    private string _targetPreview = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MiniUsageStatusText))]
    private string _statusMessage = "Start typing to see commands";

    public double ClipboardExpiryProgress { get; private set; }

    public bool IsClipboardExpiryActive { get; private set; }

    public string ClipboardExpiryTooltip { get; private set; } = string.Empty;

    public string ClipboardExpiryStatus { get; private set; } = string.Empty;

    public void ExtendClipboardExpiry()
    {
        if (_clipboardService?.ExtendExpiry(TimeSpan.FromSeconds(30)) == true)
        {
            RefreshClipboardExpiry();
        }
    }

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

    private CommandCompletionState ActiveCompletionState
        => CommandText.StartsWith('%') ? _macroCompletionSession.State : _completionSession.State;

    public bool IsChecklistActive => ActiveCompletionState.IsChecklistActive;

    public bool HasCompletionOptions => CompletionItems.Count > 0;

    public bool HasUsageText => !string.IsNullOrEmpty(UsageText);

    public string MiniUsageStatusText
    {
        get
        {
            var highlightedIndex = -1;
            for (var index = 0; index < CompletionItems.Count; index++)
            {
                if (CompletionItems[index].IsHighlighted)
                {
                    highlightedIndex = index;
                    break;
                }
            }

            var statusIndex = highlightedIndex < 0 && CompletionItems.Count > 0
                ? 0
                : highlightedIndex;
            var historySuggestions = GetHistorySuggestions(CommandText);
            var positionIndex = -1;
            var positionCount = 0;
            if (statusIndex >= 0 && statusIndex < historySuggestions.Count)
            {
                (positionIndex, positionCount) = GetHistoryPosition(CommandText, statusIndex);
            }
            else if (statusIndex >= historySuggestions.Count && statusIndex < CompletionItems.Count)
            {
                positionIndex = statusIndex - historySuggestions.Count;
                positionCount = CompletionItems.Count - historySuggestions.Count;
            }

            return MiniUsageStatusBuilder.Build(
                CommandInputText,
                UsageText,
                CompletionItems.Select(item => string.IsNullOrEmpty(item.StatusText)
                    ? item.DisplayText
                    : item.StatusText).ToArray(),
                statusIndex,
                HasSelectedCommandWithTrailingSpace,
                positionIndex,
                positionCount);
        }
    }

    private bool HasSelectedCommandWithTrailingSpace
    {
        get
        {
            if (!_tokenizer.EndsWithWhitespace(CommandText))
            {
                return false;
            }

            var tokens = _tokenizer.Tokenize(CommandText);
            return tokens.Count == 1 && _catalog.TryGetCommand(tokens[0].Value, out _);
        }
    }

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

    public bool IsCommandMetadataFilterMode
        => _activeCommandMetadataFilter is not null || _isEditingCommandMetadataFilter;

    public bool IsCommandMetadataFilterCompletionActive
        => _isEditingCommandMetadataFilter && _commandMetadataFilterValueSuggestions.Count > 0;

    public bool IsCommandMetadataFilterInputActive => _isEditingCommandMetadataFilter;

    public bool CanComplete => _isManagementCompletionActive
        ? CompletionItems.Count > 0
        : _isExtensionListVisible
        || IsCommandMetadataFilterCompletionActive
        || (_isDirectPathCompletionActive
            ? _directPathSuggestions.Count > 0
                || _historySuggestionCycler.TryGetSuggestions(CommandText, out _)
            : ActiveCompletionState.CanComplete
                || (!IsCommandMetadataFilterMode
                    && (GetHistorySuggestions(CommandText).Count > 0
                        || GetShortcutSuggestions(CommandText).Count > 0
                        || GetSnippetSuggestions(CommandText).Count > 0)));

    public bool CanExpandTypedShortcut
        => !IsCommandMetadataFilterMode && _shortcutResolver.TryExpandAlias(CommandText, out _);

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

        _historySuggestionCycler.Reset();
        _historyCommands = _commandLogger.ReadHistoryEntries();
        _highlightedCompletionIndex = 0;
        ApplyCompletionState(_completionSession.UpdateInputText(CommandText));
        return true;
    }

    public bool CycleHistory(bool older)
    {
        var historySuggestions = GetHistorySuggestions(CommandText);
        var currentSelection = _highlightedCompletionIndex >= 0
            && _highlightedCompletionIndex < historySuggestions.Count
                ? historySuggestions[_highlightedCompletionIndex]
                : null;
        var prefix = TryGetHistoryFilter(CommandText, out var historySearchText)
            ? historySearchText
            : CommandText;
        if (!_historySuggestionCycler.TryCycle(
            _historyCommands,
            CommandText,
            prefix,
            older,
            out _,
            currentSelection))
        {
            return false;
        }

        _highlightedCompletionIndex = 0;
        ApplyCompletionState(_completionSession.State);
        return true;
    }

    public bool SelectHistoryBoundary(bool newest)
    {
        if (IsCommandMetadataFilterMode)
        {
            return false;
        }

        var prefix = TryGetHistoryFilter(CommandText, out var historySearchText)
            ? historySearchText
            : CommandText;
        if (!_historySuggestionCycler.TrySelectBoundary(
            _historyCommands,
            CommandText,
            prefix,
            newest,
            out _))
        {
            return false;
        }

        _highlightedCompletionIndex = 0;
        ApplyCompletionState(_completionSession.State);
        return true;
    }

    public void ResetHistoryNavigation()
    {
        _historySuggestionCycler.Reset();
        _isHistoryPathStepping = false;
        _highlightedCompletionIndex = 0;
        ApplyCompletionState(_completionSession.UpdateInputText(CommandText));
    }

    public void ShutdownExtensions()
    {
        foreach (var executor in _extensions.ShutdownAwareExecutors)
        {
            try
            {
                executor.Shutdown();
            }
            catch (Exception)
            {
            }
        }
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
        _historySuggestionCycler.Reset();
        _highlightedCompletionIndex = 0;
        ApplyCompletionState(_completionSession.UpdateInputText(CommandText));
        return true;
    }

    public bool IsCommandTextEmpty => string.IsNullOrEmpty(CommandText);

    public bool UndoCommandInput()
        => UndoCommandInput(out _);

    public bool UndoCommandInput(out int caretIndex)
    {
        if (!_commandInputUndoHistory.TryUndo(CommandInputText, _commandInputCaretIndex, out var previousText, out caretIndex))
        {
            return false;
        }

        _commandInputCaretIndex = caretIndex;
        _isUndoingCommandInput = true;
        try
        {
            CommandInputText = previousText;
        }
        finally
        {
            _isUndoingCommandInput = false;
        }

        return true;
    }

    public bool RedoCommandInput()
        => RedoCommandInput(out _);

    public bool RedoCommandInput(out int caretIndex)
    {
        if (!_commandInputUndoHistory.TryRedo(CommandInputText, _commandInputCaretIndex, out var nextText, out caretIndex))
        {
            return false;
        }

        _commandInputCaretIndex = caretIndex;
        _isUndoingCommandInput = true;
        try
        {
            CommandInputText = nextText;
        }
        finally
        {
            _isUndoingCommandInput = false;
        }

        return true;
    }

    public void SetCommandInputCaretIndex(int caretIndex)
        => _commandInputCaretIndex = Math.Clamp(caretIndex, 0, CommandInputText.Length);

    public void EndCommandInputSession()
        => _commandInputUndoHistory.EndSession();

    public bool ShowCommandPlaceholder => string.IsNullOrEmpty(CommandInputText) && string.IsNullOrEmpty(CompletionGhostText);

    public int GetCommandInputCaretIndex(int commandCaretIndex)
        => Math.Clamp(_commandMetadataFilterPrefix.Length + commandCaretIndex, 0, CommandInputText.Length);

    public async Task AcceptCompletionAtIndexAsync(int index)
    {
        if (_isExtensionListVisible || index < 0 || index >= CompletionItems.Count)
        {
            return;
        }

        await HandleInputKeyAsync(CommandInputKey.First);
        for (var completionIndex = 0; completionIndex < index; completionIndex++)
        {
            await HandleInputKeyAsync(CommandInputKey.Down);
        }

        await HandleInputKeyAsync(CommandInputKey.Tab);
    }

    public async Task<bool> HandleInputKeyAsync(
        CommandInputKey key,
        bool newWindow = false,
        CommandLaunchMode launchMode = CommandLaunchMode.Normal)
    {
        if (key is CommandInputKey.Enter or CommandInputKey.Escape or CommandInputKey.Clear)
        {
            EndCommandInputSession();
        }

        if (key is CommandInputKey.Clear or CommandInputKey.Escape)
        {
            if (_isEditingCommandMetadataFilter)
            {
                _isEditingCommandMetadataFilter = false;
                UpdateCommandMetadataCompletionFilter();
            }
        }

        if (key == CommandInputKey.Enter && _isEditingCommandMetadataFilter)
        {
            return false;
        }

        if (key == CommandInputKey.Clear)
        {
            ResetHistoryNavigation();
        }

        if (_isEditingCommandMetadataFilter)
        {
            return HandleCommandMetadataFilterInputKey(key);
        }

        if (key == CommandInputKey.Enter)
        {
            if (_definitionManagementCommandModel.Handles(CommandText))
            {
                return await ExecuteCommandAsync(newWindow, launchMode);
            }

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

        var historySuggestions = GetHistorySuggestions(CommandText);
        var selectedHistoryCompletion = key is CommandInputKey.Right or CommandInputKey.Tab
            && CompletionItems.Any(item =>
                item.IsHighlighted
                && item.DisplayText.StartsWith($"{HistoryIcon} ", StringComparison.Ordinal));
        if (key == CommandInputKey.Right
            && selectedHistoryCompletion
            && historySuggestions.Count > 0)
        {
            var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, historySuggestions.Count - 1);
            AcceptHistorySuggestionToken(historySuggestions[selectedIndex]);
            return false;
        }

        if (_definitionManagementCommandModel.Handles(CommandText)
            && (!_isManagementCompletionActive
                || !string.Equals(_managementCompletionInput, CommandText, StringComparison.Ordinal)))
        {
            ApplyManagementCompletion(CommandText);
        }

        if (_isManagementCompletionActive
            && !selectedHistoryCompletion
            && HandleManagementCompletionKey(key))
        {
            return false;
        }

        if (_isExtensionListVisible
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                or CommandInputKey.SelectAll or CommandInputKey.DeselectAll)
        {
            return false;
        }

        var hasRetainedHistoryContext = _historySuggestionCycler.TryGetSuggestions(CommandText, out _);
        var hasSelectedHistoryPath = key == CommandInputKey.Right
            && _highlightedCompletionIndex >= 0
            && _highlightedCompletionIndex < historySuggestions.Count
            && _directFilePathHandler.TryGetCompletionPath(
                historySuggestions[_highlightedCompletionIndex].CommandText,
                out _);
        if (!IsCommandMetadataFilterMode
            && _isDirectPathCompletionActive
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                or CommandInputKey.Right
            && (key != CommandInputKey.Right
                || (!hasSelectedHistoryPath
                    && (!hasRetainedHistoryContext || !_isHistoryPathStepping))))
        {
            if (key == CommandInputKey.Tab
                && hasRetainedHistoryContext
                && historySuggestions.Count > 0)
            {
                var selectedHistoryIndex = Math.Clamp(_highlightedCompletionIndex, 0, historySuggestions.Count - 1);
                CommandText = historySuggestions[selectedHistoryIndex].CommandText;
                return false;
            }

            if (hasRetainedHistoryContext)
            {
                _isHistoryPathStepping = false;
            }

            HandleDirectPathCompletionKey(key);
            return false;
        }

        if (!selectedHistoryCompletion
            && CommandText.StartsWith('%')
            && key is CommandInputKey.Tab or CommandInputKey.Right or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                or CommandInputKey.SelectAll or CommandInputKey.DeselectAll or CommandInputKey.Space)
        {
            _macroCompletionSession.UpdateInputText(CommandText);
            var macroCompletionState = _macroCompletionSession.HandleKey(key);
            if (key is CommandInputKey.Tab or CommandInputKey.Right or CommandInputKey.Space)
            {
                CommandText = macroCompletionState.InputText;
            }
            else
            {
                ApplyCompletionState(macroCompletionState);
            }

            return false;
        }

        if (!selectedHistoryCompletion && SnippetCompletion.TryGetFilterPrefix(CommandText, out _))
        {
            var snippetSuggestions = GetSnippetSuggestions(CommandText);
            if (snippetSuggestions.Count > 0
                && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                    or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                    or CommandInputKey.Right)
            {
                if (key is CommandInputKey.Right or CommandInputKey.Tab)
                {
                    var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, snippetSuggestions.Count - 1);
                    CommandText = SnippetCompletion.BuildRunCommand(snippetSuggestions[selectedIndex]);
                }
                else
                {
                    _highlightedCompletionIndex = key switch
                    {
                        CommandInputKey.First => 0,
                        CommandInputKey.Last => snippetSuggestions.Count - 1,
                        _ => MoveCompletionIndex(_highlightedCompletionIndex, key, snippetSuggestions.Count)
                    };
                    ApplyCompletionState(_completionSession.State);
                }

                return false;
            }
        }

        var shortcutList = GetShortcutSuggestions(CommandText);
        if (!selectedHistoryCompletion
            && shortcutList.Count > 0
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                or CommandInputKey.Right)
        {
            if (key is CommandInputKey.Right or CommandInputKey.Tab)
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

        if (historySuggestions.Count > 0
            && (key != CommandInputKey.Right || _highlightedCompletionIndex < historySuggestions.Count)
            && key is CommandInputKey.Tab or CommandInputKey.Up or CommandInputKey.Down
                or CommandInputKey.PageUp or CommandInputKey.PageDown or CommandInputKey.First or CommandInputKey.Last
                or CommandInputKey.Right)
        {
            var regularCount = _completionSession.State.Items.Count;
            var historyCount = historySuggestions.Count;
            if (key == CommandInputKey.Down
                && _highlightedCompletionIndex >= 0
                && _highlightedCompletionIndex < historyCount
                && _directFilePathHandler.TryGetCompletionPath(
                    historySuggestions[_highlightedCompletionIndex].CommandText,
                    out var historyCompletionPath))
            {
                var selectedHistoryEntry = historySuggestions[_highlightedCompletionIndex];
                CommandText = selectedHistoryEntry.CommandText;
                if (_historySuggestionCycler.TryRetainSelection(
                    _historyCommands,
                    CommandText,
                    historyCompletionPath,
                    selectedHistoryEntry))
                {
                    _isHistoryPathStepping = true;
                    _highlightedCompletionIndex = 0;
                    ApplyCompletionState(_completionSession.State);
                    return false;
                }
            }

            if (key == CommandInputKey.Right && _highlightedCompletionIndex < historyCount)
            {
                var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, historyCount - 1);
                AcceptHistorySuggestionToken(historySuggestions[selectedIndex]);
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
        bool skipLeadingWhitespace = false,
        bool acceptDirectPathSegments = true)
    {
        var inputPath = input.Trim('"');
        if (acceptDirectPathSegments
            && _directFilePathHandler.TryGetCompletionPath(suggestion, out var suggestionPath)
            && _directFilePathHandler.TryAcceptNextPathSegment(inputPath, suggestionPath, out var nextPath))
        {
            var completedPath = _directFilePathHandler.GetSuggestionText(nextPath);
            CommandText = completedPath;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime pathDesktop
                && pathDesktop.MainWindow is MainWindow pathMainWindow)
            {
                pathMainWindow.SetPendingCaretIndex(completedPath.Length);
            }

            return;
        }

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
            if (boundary >= 0 && boundary < remaining.Length)
            {
                if (!skipLeadingWhitespace && remaining[boundary] == ' ')
                {
                    acceptedLength++;
                }
                else if (skipLeadingWhitespace)
                {
                    while (acceptedLength < remaining.Length && char.IsWhiteSpace(remaining[acceptedLength]))
                    {
                        acceptedLength++;
                    }
                }
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

    private void AcceptHistorySuggestionToken(CommandHistoryEntry selectedHistoryEntry)
    {
        var completionInput = TryGetHistoryFilter(CommandText, out var historySearchText)
            ? historySearchText
            : CommandText;
        AcceptNextSuggestionToken(
            completionInput,
            selectedHistoryEntry.CommandText,
            skipLeadingWhitespace: true,
            acceptDirectPathSegments: false);
        var selectedHistoryPathCompleted = _directFilePathHandler.TryGetCompletionPath(
                selectedHistoryEntry.CommandText,
                out var selectedHistoryPath)
            && _directFilePathHandler.TryGetCompletionPath(CommandText, out var completedHistoryPath)
            && string.Equals(selectedHistoryPath, completedHistoryPath, StringComparison.OrdinalIgnoreCase);
        if (!selectedHistoryPathCompleted
            && _completionSession.State.CanComplete
            && _completionSession.State.Items.Count == 0
            && !_tokenizer.EndsWithWhitespace(CommandText))
        {
            CommandText += ' ';
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is MainWindow mainWindow)
            {
                mainWindow.SetPendingCaretIndex(CommandText.Length);
            }
        }

        if (selectedHistoryPathCompleted)
        {
            _historySuggestionCycler.Reset();
            _isHistoryPathStepping = false;
            _highlightedCompletionIndex = 0;
            ApplyCompletionState(_completionSession.UpdateInputText(CommandText));
            return;
        }

        var updatedPrefix = TryGetHistoryFilter(CommandText, out var updatedHistorySearchText)
            ? updatedHistorySearchText
            : CommandText;
        if (_historySuggestionCycler.TryRetainSelection(
            _historyCommands,
            CommandText,
            updatedPrefix,
            selectedHistoryEntry))
        {
            _isHistoryPathStepping = true;
            _highlightedCompletionIndex = 0;
            ApplyCompletionState(_completionSession.UpdateInputText(CommandText));
        }
    }

    partial void OnCommandTextChanging(string oldValue, string newValue)
    {
        if (!_syncingInput && !_isUndoingCommandInput)
        {
            _commandInputUndoHistory.RecordProgrammaticChange(
                CommandInputText,
                _commandMetadataFilterPrefix + newValue,
                _commandInputCaretIndex);
        }
    }

    partial void OnCommandTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsCommandTextEmpty));
        OnPropertyChanged(nameof(ShowCommandPlaceholder));
        if (_syncingInput)
        {
            UpdateCommandExpansionPreview(value);
            return;
        }

        SyncCommandInputText(value);
        _historySuggestionCycler.Reset();
        _isHistoryPathStepping = false;
        UpdateCommandExpansionPreview(value);
        if (_definitionManagementCommandModel?.Handles(value) == true)
        {
            ApplyManagementCompletion(value);
            return;
        }

        _isManagementCompletionActive = false;
        if (_macroCompletionSession is not null && value.StartsWith('%'))
        {
            ApplyCompletionState(_macroCompletionSession.UpdateInputText(value));
            return;
        }

        ApplyCompletionState(_completionSession.UpdateInputText(value));
        if (!IsCommandMetadataFilterMode && string.Equals(value.Trim(), "!", StringComparison.Ordinal))
        {
            ShowExtensionList();
        }
    }

    partial void OnCommandInputTextChanging(string oldValue, string newValue)
    {
        if (!_syncingInput && !_isUndoingCommandInput)
        {
            _commandInputUndoHistory.RecordUserChange(oldValue, newValue, _commandInputCaretIndex);
        }
    }

    partial void OnCommandInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowCommandPlaceholder));
        if (_syncingInput)
        {
            return;
        }

        _historySuggestionCycler.Reset();
        _isHistoryPathStepping = false;
        var filterInput = CommandMetadataFilter.ParseInput(value);
        if (filterInput.IsFilterInput)
        {
            if (!_commandMetadataFilterInput.IsFilterInput
                || _commandMetadataFilterInput.IsEditingFieldName != filterInput.IsEditingFieldName
                || !string.Equals(
                    _commandMetadataFilterInput.EditingField,
                    filterInput.EditingField,
                    StringComparison.OrdinalIgnoreCase))
            {
                _highlightedCompletionIndex = 0;
            }

            _commandMetadataFilterPrefix = filterInput.FilterPrefix;
            _isManagementCompletionActive = false;
            if (filterInput.IsComplete && filterInput.Filter is not null)
            {
                _isEditingCommandMetadataFilter = false;
                _commandMetadataFilterInput = default;
                _commandMetadataFilterValueSuggestions = Array.Empty<string>();
                SetCommandMetadataFilter(filterInput.Filter);
                SetCommandTextFromInput(filterInput.CommandText);
                ApplyCompletionState(_completionSession.UpdateInputText(filterInput.CommandText));
            }
            else
            {
                _isEditingCommandMetadataFilter = true;
                _commandMetadataFilterInput = filterInput;
                SetCommandTextFromInput(string.Empty);
                UpdateCommandMetadataCompletionFilter();
                ApplyCompletionState(_completionSession.UpdateInputText(string.Empty));
                UpdateCommandMetadataFilterValueSuggestions(filterInput);
            }

            return;
        }

        _commandMetadataFilterPrefix = string.Empty;
        if (_isEditingCommandMetadataFilter || _activeCommandMetadataFilter is not null)
        {
            _isEditingCommandMetadataFilter = false;
            _commandMetadataFilterInput = default;
            _commandMetadataFilterValueSuggestions = Array.Empty<string>();
            _activeCommandMetadataFilter = null;
            UpdateCommandMetadataCompletionFilter();
        }

        SetCommandTextFromInput(value);
        UpdateCommandExpansionPreview(value);
        if (_definitionManagementCommandModel?.Handles(value) == true)
        {
            ApplyManagementCompletion(value);
            return;
        }

        _isManagementCompletionActive = false;
        ApplyCompletionState(value.StartsWith('%')
            ? _macroCompletionSession.UpdateInputText(value)
            : _completionSession.UpdateInputText(value));
        if (!IsCommandMetadataFilterMode && string.Equals(value.Trim(), "!", StringComparison.Ordinal))
        {
            ShowExtensionList();
        }
    }

    private void SetCommandTextFromInput(string commandText)
    {
        _syncingInput = true;
        CommandText = commandText;
        _syncingInput = false;
    }

    private void SyncCommandInputText(string commandText)
    {
        var inputText = _commandMetadataFilterPrefix + commandText;
        if (string.Equals(CommandInputText, inputText, StringComparison.Ordinal))
        {
            return;
        }

        _syncingInput = true;
        CommandInputText = inputText;
        _syncingInput = false;
    }

    private void SetCommandMetadataFilter(CommandMetadataFilter filter)
    {
        _activeCommandMetadataFilter = filter;
        UpdateCommandMetadataCompletionFilter();
    }

    private void UpdateCommandMetadataCompletionFilter()
    {
        if (_isEditingCommandMetadataFilter)
        {
            _completionSession.UpdateCommandFilter(_ => false);
            return;
        }

        if (_activeCommandMetadataFilter is not { } filter)
        {
            _completionSession.UpdateCommandFilter(null);
            return;
        }

        _completionSession.UpdateCommandFilter(command =>
        {
            var packId = _commandPackSources.TryGetValue(command.QualifiedName, out var sourcePackId)
                ? sourcePackId
                : null;
            var extensionId = _commandSources.TryGetValue(command.QualifiedName, out var sourceExtensionId)
                ? sourceExtensionId
                : null;
            return filter.Matches(command, packId, extensionId);
        });
    }

    private bool HandleCommandMetadataFilterInputKey(CommandInputKey key)
    {
        if (key is CommandInputKey.Up or CommandInputKey.Down
            or CommandInputKey.PageUp or CommandInputKey.PageDown
            or CommandInputKey.First or CommandInputKey.Last)
        {
            if (_commandMetadataFilterValueSuggestions.Count == 0)
            {
                return false;
            }

            _highlightedCompletionIndex = key switch
            {
                CommandInputKey.First => 0,
                CommandInputKey.Last => _commandMetadataFilterValueSuggestions.Count - 1,
                _ => MoveCompletionIndex(_highlightedCompletionIndex, key, _commandMetadataFilterValueSuggestions.Count)
            };
            UpdateCommandMetadataFilterValueSuggestions(_commandMetadataFilterInput);
            return false;
        }

        if (_commandMetadataFilterInput.IsEditingFieldName
            && key is (CommandInputKey.Tab or CommandInputKey.Right)
            && _commandMetadataFilterValueSuggestions.Count > 0
            && _commandMetadataFilterInput.EditingFieldStart >= 0
            && _commandMetadataFilterInput.EditingFieldStart <= CommandInputText.Length)
        {
            var selectedIndex = Math.Clamp(
                _highlightedCompletionIndex,
                0,
                _commandMetadataFilterValueSuggestions.Count - 1);
            var selectedField = _commandMetadataFilterValueSuggestions[selectedIndex];
            CommandInputText = CommandInputText[.._commandMetadataFilterInput.EditingFieldStart] + selectedField + ':';
            return false;
        }

        if (key is CommandInputKey.Tab or CommandInputKey.Space or CommandInputKey.Right
            && _commandMetadataFilterValueSuggestions.Count > 0
            && _commandMetadataFilterInput.EditingValueStart >= 0
            && _commandMetadataFilterInput.EditingValueStart <= CommandInputText.Length)
        {
            var selectedIndex = Math.Clamp(
                _highlightedCompletionIndex,
                0,
                _commandMetadataFilterValueSuggestions.Count - 1);
            var selectedValue = _commandMetadataFilterValueSuggestions[selectedIndex];
            CommandInputText = CommandInputText[.._commandMetadataFilterInput.EditingValueStart] + selectedValue + ' ';
        }

        return false;
    }

    private void UpdateCommandMetadataFilterValueSuggestions(CommandMetadataFilterInput filterInput)
    {
        _commandMetadataFilterValueSuggestions = CommandMetadataFilterSuggestionResolver.Resolve(
            _catalog.Schema,
            _commandPackSources,
            _commandSources,
            filterInput);
        if (filterInput.IsEditingFieldName)
        {
            _highlightedCompletionIndex = _commandMetadataFilterValueSuggestions.Count == 0
                ? -1
                : Math.Clamp(_highlightedCompletionIndex, 0, _commandMetadataFilterValueSuggestions.Count - 1);
            CompletionItems = _commandMetadataFilterValueSuggestions
                .Select((field, index) => new CompletionOptionViewModel(field, index == _highlightedCompletionIndex, false))
                .ToArray();
            CompletionGhostText = _highlightedCompletionIndex >= 0
                ? _commandMetadataFilterValueSuggestions[_highlightedCompletionIndex][filterInput.FieldPrefix.Length..]
                : string.Empty;
            TargetPreview = string.Empty;
            UsageText = string.Empty;
            UsageCommandText = string.Empty;
            OnPropertyChanged(nameof(HasCompletionOptions));
            OnPropertyChanged(nameof(CanComplete));
            OnPropertyChanged(nameof(CanExpandTypedShortcut));
            OnPropertyChanged(nameof(UsageCommandText));
            return;
        }
        _highlightedCompletionIndex = _commandMetadataFilterValueSuggestions.Count == 0
            ? -1
            : Math.Clamp(_highlightedCompletionIndex, 0, _commandMetadataFilterValueSuggestions.Count - 1);
        CompletionItems = _commandMetadataFilterValueSuggestions
            .Select((value, index) => new CompletionOptionViewModel(value, index == _highlightedCompletionIndex, false))
            .ToArray();
        CompletionGhostText = _highlightedCompletionIndex >= 0
            && _commandMetadataFilterValueSuggestions[_highlightedCompletionIndex]
                .StartsWith(filterInput.ValuePrefix, StringComparison.OrdinalIgnoreCase)
                ? _commandMetadataFilterValueSuggestions[_highlightedCompletionIndex][filterInput.ValuePrefix.Length..]
                : string.Empty;
        TargetPreview = string.Empty;
        UsageText = string.Empty;
        UsageCommandText = string.Empty;
        OnPropertyChanged(nameof(HasCompletionOptions));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanExpandTypedShortcut));
        OnPropertyChanged(nameof(UsageCommandText));
    }

    private void ApplyManagementCompletion(string input)
    {
        var help = _definitionManagementCommandModel!.GetHelp(input);
        if (!string.Equals(_managementCompletionInput, input, StringComparison.Ordinal))
        {
            _managementCompletionInput = input;
            _highlightedCompletionIndex = 0;
        }

        _isManagementCompletionActive = true;
        var highlightedIndex = help.Suggestions.Count == 0
            ? -1
            : Math.Clamp(_highlightedCompletionIndex, 0, help.Suggestions.Count - 1);
        _highlightedCompletionIndex = Math.Max(0, highlightedIndex);
        CompletionItems = help.Suggestions
            .Select((suggestion, index) => new CompletionOptionViewModel(suggestion, index == highlightedIndex, false))
            .ToArray();
        CompletionGhostText = highlightedIndex >= 0
            && help.Suggestions[highlightedIndex].StartsWith(input, StringComparison.OrdinalIgnoreCase)
                ? help.Suggestions[highlightedIndex][input.Length..]
                : string.Empty;
        UsageText = $"{help.Usage}{Environment.NewLine}{help.Example}";
        UsageCommandText = help.Usage["Usage: ".Length..];
        TargetPreview = string.Empty;
        _isExtensionListVisible = false;
        OnPropertyChanged(nameof(HasCompletionOptions));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanExpandTypedShortcut));
        OnPropertyChanged(nameof(UsageCommandText));
    }

    private bool HandleManagementCompletionKey(CommandInputKey key)
    {
        if (key == CommandInputKey.Tab
            && _definitionManagementCommandModel!.ShouldAppendSeparatorOnTab(CommandText))
        {
            var acceptedText = CommandText + " ";
            CommandText = acceptedText;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is MainWindow mainWindow)
            {
                mainWindow.SetPendingCaretIndex(acceptedText.Length);
            }

            return true;
        }

        var suggestions = CompletionItems;
        if (suggestions.Count == 0)
        {
            return key is CommandInputKey.Tab or CommandInputKey.Right
                or CommandInputKey.Up or CommandInputKey.Down or CommandInputKey.PageUp or CommandInputKey.PageDown
                or CommandInputKey.First or CommandInputKey.Last;
        }

        switch (key)
        {
            case CommandInputKey.Up:
            case CommandInputKey.Down:
            case CommandInputKey.PageUp:
            case CommandInputKey.PageDown:
            case CommandInputKey.First:
            case CommandInputKey.Last:
                _highlightedCompletionIndex = key switch
                {
                    CommandInputKey.First => 0,
                    CommandInputKey.Last => suggestions.Count - 1,
                    _ => MoveCompletionIndex(_highlightedCompletionIndex, key, suggestions.Count)
                };
                ApplyManagementCompletion(CommandText);
                return true;
            case CommandInputKey.Tab:
            {
                var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, suggestions.Count - 1);
                var acceptedText = suggestions[selectedIndex].DisplayText.TrimEnd() + " ";
                CommandText = acceptedText;
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.SetPendingCaretIndex(acceptedText.Length);
                }

                return true;
            }
            case CommandInputKey.Right:
            {
                var selectedIndex = Math.Clamp(_highlightedCompletionIndex, 0, suggestions.Count - 1);
                AcceptNextSuggestionToken(CommandText, suggestions[selectedIndex].DisplayText);
                return true;
            }
            default:
                return false;
        }
    }

    partial void OnCompletionGhostTextChanged(string value)
        => OnPropertyChanged(nameof(ShowCommandPlaceholder));

    private void ApplyCompletionState(CommandCompletionState state)
    {
        if (!_isUndoingCommandInput
            && !string.Equals(CommandInputText, state.InputText, StringComparison.Ordinal))
        {
            _commandInputUndoHistory.RecordProgrammaticChange(
                CommandInputText,
                state.InputText,
                _commandInputCaretIndex);
        }

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

        SyncCommandInputText(state.InputText);

        UpdateCommandExpansionPreview(state.InputText);
        var historySuggestions = GetHistorySuggestions(state.InputText);
        var shortcutSuggestions = GetShortcutSuggestions(state.InputText);
        var snippetSuggestions = GetSnippetSuggestions(state.InputText);
        var selectedHistoryIndex = _highlightedCompletionIndex < historySuggestions.Count
            ? _highlightedCompletionIndex
            : -1;
        var selectedShortcutIndex = _highlightedCompletionIndex >= historySuggestions.Count
            && _highlightedCompletionIndex < historySuggestions.Count + shortcutSuggestions.Count
                ? _highlightedCompletionIndex - historySuggestions.Count
                : -1;
        var selectedSnippetIndex = _highlightedCompletionIndex >= historySuggestions.Count + shortcutSuggestions.Count
            && _highlightedCompletionIndex < historySuggestions.Count + shortcutSuggestions.Count + snippetSuggestions.Count
                ? _highlightedCompletionIndex - historySuggestions.Count - shortcutSuggestions.Count
                : -1;
        var selectedCommandText = selectedHistoryIndex >= 0
            ? historySuggestions[selectedHistoryIndex].CommandText
            : selectedShortcutIndex >= 0
                ? shortcutSuggestions[selectedShortcutIndex].Value
                : selectedSnippetIndex >= 0
                    ? SnippetCompletion.BuildRunCommand(snippetSuggestions[selectedSnippetIndex])
            : historySuggestions.Count + shortcutSuggestions.Count + snippetSuggestions.Count > 0
                ? GetRegularCompletionValue(state, _highlightedCompletionIndex - historySuggestions.Count - shortcutSuggestions.Count - snippetSuggestions.Count)
                : state.Items.FirstOrDefault(item => item.IsHighlighted)?.Value;
        var tokens = _tokenizer.Tokenize(state.InputText);
        var isCommandCompletion = tokens.Count == 0
            || (tokens.Count == 1 && !_tokenizer.EndsWithWhitespace(state.InputText));
        var inputIsExactCommand = tokens.Count == 1
            && _catalog.TryGetCommand(tokens[0].Value, out _);
        var previewCommandText = selectedHistoryIndex >= 0
            ? historySuggestions[selectedHistoryIndex].CommandText
            : selectedSnippetIndex >= 0
                ? selectedCommandText!
            : isCommandCompletion && !inputIsExactCommand && selectedCommandText is not null
                ? selectedCommandText
                : state.InputText;
        TargetPreview = GetCommandTargetPreview(previewCommandText);
        if (!IsCommandMetadataFilterMode
            && _directFilePathHandler.TryGetCompletionPath(state.InputText, out var directPath))
        {
            _activeArgumentPathContext = null;
            ApplyDirectPathCompletion(
                directPath,
                replacesWholeInput: true,
                resetHighlight: true);
            return;
        }

        if (!IsCommandMetadataFilterMode
            && _argumentPathCompletion.TryGetContext(state.InputText, _catalog, _tokenizer, out var pathContext))
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
        var isShortcutFilter = TryGetShortcutFilter(state.InputText, out var shortcutPrefix);
        var isSnippetFilter = SnippetCompletion.TryGetFilterPrefix(state.InputText, out var snippetPrefix);
        var filterGhostText = isShortcutFilter
            ? selectedShortcutIndex >= 0
                ? CompletionText.GetGhostSuffix(shortcutSuggestions[selectedShortcutIndex].Key, shortcutPrefix)
                : string.Empty
            : isSnippetFilter && selectedSnippetIndex >= 0
                ? CompletionText.GetGhostSuffix(snippetSuggestions[selectedSnippetIndex], snippetPrefix)
                : string.Empty;
        CompletionGhostText = isShortcutFilter || isSnippetFilter
            ? filterGhostText
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

                var listSuggestionCount = historySuggestions.Count + shortcutSuggestions.Count + snippetSuggestions.Count;
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
            var (historyPositionIndex, historyPositionCount) = GetHistoryPosition(state.InputText, index);
            var historyText = $"{HistoryIcon} {entry.CommandText}";
            return new CompletionOptionViewModel(
                $"{HistoryIcon} {historyPositionIndex + 1}/{historyPositionCount} {entry.CommandText}",
                index == selectedHistoryIndex,
                false,
                ageLabel,
                StatusText: historyText);
        });
        var shortcutItems = shortcutSuggestions.Select((shortcut, index) =>
            new CompletionOptionViewModel(
                $"[shortcut] {shortcut.Key} → {shortcut.Value.TrimEnd()}",
                index == selectedShortcutIndex,
                false));
        var snippetItems = snippetSuggestions.Select((snippet, index) =>
            new CompletionOptionViewModel(
                $"[snippet] {snippet}",
                index == selectedSnippetIndex,
                false));
        CompletionItems = historyItems.Concat(shortcutItems).Concat(snippetItems).Concat(regularItems).ToArray();
        OnPropertyChanged(nameof(IsChecklistActive));
        OnPropertyChanged(nameof(HasCompletionOptions));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanExpandTypedShortcut));
    }

    private IReadOnlyList<CommandHistoryEntry> GetHistorySuggestions(string inputText)
    {
        if (IsCommandMetadataFilterMode)
        {
            return Array.Empty<CommandHistoryEntry>();
        }

        if (_historySuggestionCycler.TryGetSuggestions(inputText, out var cycledSuggestions))
        {
            return cycledSuggestions;
        }

        if (TryGetHistoryFilter(inputText, out var searchText))
        {
            return searchText.Length == 0
                ? _historyCommands
                : ProfileCommandLogger.FilterHistoryEntriesByPrefix(_historyCommands, searchText);
        }

        return ProfileCommandLogger.FilterHistoryEntries(_historyCommands, inputText);
    }

    private (int Index, int Count) GetHistoryPosition(string inputText, int suggestionIndex)
    {
        if (_historySuggestionCycler.TryGetSuggestions(inputText, out _)
            && _historySuggestionCycler.SelectedIndex >= 0)
        {
            return (_historySuggestionCycler.SelectedIndex, _historySuggestionCycler.EntryCount);
        }

        var historyPrefix = TryGetHistoryFilter(inputText, out var historySearchText)
            ? historySearchText
            : inputText;
        return (
            suggestionIndex,
            ProfileCommandLogger.FilterHistoryEntriesByPrefix(_historyCommands, historyPrefix).Count);
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
        => !IsCommandMetadataFilterMode && TryGetShortcutFilter(inputText, out var prefix)
            ? _shortcutResolver.GetAliasExpansions(prefix)
            : Array.Empty<KeyValuePair<string, string>>();

    private IReadOnlyList<string> GetSnippetSuggestions(string inputText)
        => IsCommandMetadataFilterMode
            ? Array.Empty<string>()
            : SnippetCompletion.Filter(_snippetStore.GetNames(), inputText);

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
            && parseResult.ParsedCommand is { Definition.ExecutionHandler: var handler } parsedCommand
            && string.Equals(handler, "url", StringComparison.OrdinalIgnoreCase))
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

        var hasRetainedHistory = _historySuggestionCycler.TryGetSuggestions(CommandText, out var retainedHistory)
            && retainedHistory.Count > 0;
        CompletionGhostText = _isHistoryPathStepping
            && hasRetainedHistory
            && _directFilePathHandler.TryGetCompletionPath(retainedHistory[0].CommandText, out var historyPath)
            && _directFilePathHandler.TryGetCompletionPath(CommandText, out var currentPath)
            && _directFilePathHandler.IsPathPrefix(historyPath, currentPath)
                ? historyPath[currentPath.Length..]
                : DirectFilePathSelection.TrySelectSuggestion(
                    _directPathSuggestions,
                    _directPathHighlightedIndex,
                    out var selectedPath)
                    && _directFilePathHandler.TryGetCompletionPath(CommandText, out var typedPath)
                    && _directFilePathHandler.IsPathPrefix(selectedPath, typedPath)
                        ? selectedPath[typedPath.Length..]
                        : string.Empty;
        UsageText = "Files and folders";
        UsageCommandText = string.Empty;
        OnPropertyChanged(nameof(UsageCommandText));
        var pathItems = _directPathSuggestions
            .Select((path, index) => new CompletionOptionViewModel(
                path,
                !(_isHistoryPathStepping && hasRetainedHistory)
                    && index == _directPathHighlightedIndex,
                false))
            .ToList();
        if (hasRetainedHistory)
        {
            var entry = retainedHistory[0];
            var age = ProfileCommandLogger.FormatHistoryAge(entry.RunTime, DateTimeOffset.Now);
            var ageLabel = age.Length == 0 ? string.Empty : $" [{age}]";
            var (historyPositionIndex, historyPositionCount) = GetHistoryPosition(CommandText, 0);
            var historyText = $"{HistoryIcon} {entry.CommandText}";
            pathItems.Insert(0, new CompletionOptionViewModel(
                $"{HistoryIcon} {historyPositionIndex + 1}/{historyPositionCount} {entry.CommandText}",
                _isHistoryPathStepping || _highlightedCompletionIndex == 0,
                false,
                ageLabel,
                StatusText: historyText));
        }

        CompletionItems = pathItems;
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

        if (key == CommandInputKey.Right)
        {
            AcceptNextDirectPathSegment();
            return;
        }

        if (key == CommandInputKey.Tab)
        {
            if (DirectFilePathSelection.TrySelectSuggestion(
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

    private void AcceptNextDirectPathSegment()
    {
        if (!DirectFilePathSelection.TrySelectSuggestion(
                _directPathSuggestions,
                _directPathHighlightedIndex,
                out var selectedPath)
            || !_directFilePathHandler.TryAcceptNextPathSegment(
                GetCurrentDirectPathText(),
                selectedPath,
                out var acceptedPath))
        {
            return;
        }

        if (_directPathReplacesWholeInput)
        {
            CommandText = _directFilePathHandler.GetSuggestionText(acceptedPath);
        }
        else
        {
            CommandText = CommandArgumentPathCompletion.ReplaceValue(
                CommandText,
                _activeArgumentPathContext!,
                acceptedPath);
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mainWindow)
        {
            mainWindow.SetPendingCaretIndex(CommandText.Length);
        }
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
    string KindText = "",
    string StatusText = "");

    private (string Text, string CommandText) GetUsageText(string inputText)
    {
        if (_macroCompletionSession.TryGetUsageText(inputText, out var macroUsage))
        {
            return ($"Usage: {macroUsage}", macroUsage);
        }

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
            ResetHistoryNavigation();
        }
    }

    private (bool Found, string Value) TryGetSecret(string name)
    {
        var found = _secretStore.TryGet(name, out var value);
        return (found, value);
    }

    private async Task<bool> ExecuteMacroInvocationAsync(bool newWindow, CommandLaunchMode launchMode)
    {
        var invocationText = CommandText;
        var parsed = _macroInvocationParser.Parse(invocationText, _macroVariables);
        if (!parsed.IsMacroInvocation || parsed.Error is not null || parsed.Macro is null || parsed.Arguments is null)
        {
            SetStatusMessage(parsed.Error ?? "Macro invocation is invalid.");
            return false;
        }

        var usesSecrets = _macroSecretAccessDetector.UsesSecrets(parsed.Macro.Script);
        var result = await _secretCommandModel.ExecuteWithSecretAccessScopeAsync(
            () => _macroExecutionEngine.ExecuteAsync(
                parsed.Macro,
                parsed.Arguments,
                newWindow,
                launchMode,
                ExecuteMacroCommandAsync),
            relockWhenFinished: usesSecrets);
        SetStatusMessage(result.Message);
        CommandText = result.Succeeded ? string.Empty : invocationText;
        return result.Succeeded;
    }

    private async Task<CommandExecutionResult> ExecuteMacroCommandAsync(
        string commandText,
        IReadOnlyDictionary<string, string> arguments,
        bool newWindow,
        CommandLaunchMode launchMode)
    {
        if (commandText.TrimStart().StartsWith('%'))
        {
            return new CommandExecutionResult(false, "Nested macro calls are not supported.");
        }

        var macroArgumentNames = arguments.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var variables = _macroVariables
            .Where(variable => !string.IsNullOrEmpty(variable.Namespace)
                || !macroArgumentNames.Contains(variable.Name))
            .Concat(arguments.Select(argument => new CommandVariableDefinition(
                argument.Key,
                JsonSerializer.Serialize(argument.Value))));
        var expansion = new CommandTextVariableExpander(variables).Expand(commandText);
        if (expansion.UnresolvedVariables.Count > 0)
        {
            return new CommandExecutionResult(
                false,
                $"Unknown variable(s): {string.Join(", ", expansion.UnresolvedVariables.Select(name => $"${name}"))}");
        }

        string expandedText;
        try
        {
            expandedText = await _functionExpander.ExpandAsync(expansion.ExpandedText);
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Function expansion failed: {exception.Message}");
        }

        var succeeded = await ExecuteCommandCoreAsync(newWindow, launchMode, expandedText);
        return new CommandExecutionResult(succeeded, StatusMessage) { IsValueOutput = IsValueOutput };
    }

    private async Task<bool> ExecuteCommandCoreAsync(
        bool newWindow = false,
        CommandLaunchMode launchMode = CommandLaunchMode.Normal)
    {
        if (CommandText.TrimStart().StartsWith('%'))
        {
            return await ExecuteMacroInvocationAsync(newWindow, launchMode);
        }

        if (_definitionManagementCommandModel.Handles(CommandText))
        {
            var managementResult = _definitionManagementCommandModel.Execute(CommandText);
            SetStatusMessage(managementResult.Message, managementResult.IsValueOutput);
            if (managementResult.Succeeded)
            {
                CommandText = string.Empty;
            }
            return managementResult.Succeeded;
        }

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
                && string.Equals(optionFreeParseResult.ParsedCommand!.Definition.ExecutionHandler, "url", StringComparison.OrdinalIgnoreCase))
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
        var executionHandler = parsedCommand.Definition.ExecutionHandler;
        if (launchMode == CommandLaunchMode.Normal
            && string.Equals(executionHandler, "shell", StringComparison.OrdinalIgnoreCase))
        {
            launchMode = new CommandLaunchPreferenceStore(_profileSettingsStore)
                .GetLaunchMode(parsedCommand.Definition);
        }

        if (launchMode != CommandLaunchMode.Normal
            && !string.Equals(executionHandler, "shell", StringComparison.OrdinalIgnoreCase))
        {
            SetStatusMessage("Elevated and alternate-user launch is only supported for shell commands.");
            return false;
        }

        CommandExecutionResult executionResult;
        if (string.Equals(executionHandler, "secret", StringComparison.OrdinalIgnoreCase))
        {
            executionResult = await _secretCommandModel.ExecuteWithSecretRelockAsync(
                () => _secretCommandModel.ExecuteAsync(parsedCommand));
            RefreshSecretAccessState();
        }
        else if (_commandExecutors.TryGetValue(executionHandler, out var commandExecutor))
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
        else if (_asyncCommandExecutors.TryGetValue(executionHandler, out var asyncCommandExecutor))
        {
            executionResult = await _secretCommandModel.ExecuteWithSecretRelockAsync(
                () => asyncCommandExecutor.ExecuteAsync(parsedCommand));
            if (string.Equals(executionHandler, "snippet", StringComparison.OrdinalIgnoreCase))
            {
                RefreshSecretAccessState();
            }
        }
        else
        {
            SetStatusMessage($"Command handler '{executionHandler}' is not supported.");
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
        RefreshClipboardExpiry();
    }

    private void RefreshClipboardExpiry()
    {
        var expirationTime = _clipboardService?.ExpirationTime;
        var duration = _clipboardService?.ExpirationDuration ?? TimeSpan.Zero;
        var remaining = expirationTime is { } expiration
            ? expiration - DateTimeOffset.UtcNow
            : TimeSpan.Zero;
        var isActive = remaining > TimeSpan.Zero && duration > TimeSpan.Zero;
        var progress = isActive
            ? Math.Clamp(remaining.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100)
            : 0;
        var remainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        var tooltip = isActive
            ? $"Clipboard clears in {remainingSeconds} seconds. Click to add 30 seconds."
            : "No copied secret is awaiting expiration.";

        if (IsClipboardExpiryActive != isActive)
        {
            IsClipboardExpiryActive = isActive;
            OnPropertyChanged(nameof(IsClipboardExpiryActive));
        }

        if (Math.Abs(ClipboardExpiryProgress - progress) >= 0.1)
        {
            ClipboardExpiryProgress = progress;
            OnPropertyChanged(nameof(ClipboardExpiryProgress));
        }

        if (!string.Equals(ClipboardExpiryTooltip, tooltip, StringComparison.Ordinal))
        {
            ClipboardExpiryTooltip = tooltip;
            OnPropertyChanged(nameof(ClipboardExpiryTooltip));
        }

        var status = isActive
            ? $"Secret clears in {remainingSeconds}s — click to add 30s"
            : string.Empty;
        if (!string.Equals(ClipboardExpiryStatus, status, StringComparison.Ordinal))
        {
            ClipboardExpiryStatus = status;
            OnPropertyChanged(nameof(ClipboardExpiryStatus));
        }
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
                && string.Equals(optionFreeParseResult.ParsedCommand!.Definition.ExecutionHandler, "url", StringComparison.OrdinalIgnoreCase))
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
        var executionHandler = parsedCommand.Definition.ExecutionHandler;
        if (launchMode == CommandLaunchMode.Normal
            && string.Equals(executionHandler, "shell", StringComparison.OrdinalIgnoreCase))
        {
            launchMode = new CommandLaunchPreferenceStore(_profileSettingsStore)
                .GetLaunchMode(parsedCommand.Definition);
        }

        if (launchMode != CommandLaunchMode.Normal
            && !string.Equals(executionHandler, "shell", StringComparison.OrdinalIgnoreCase))
        {
            SetStatusMessage("Elevated and alternate-user launch is only supported for shell commands.");
            return false;
        }

        CommandExecutionResult executionResult;
        if (string.Equals(executionHandler, "secret", StringComparison.OrdinalIgnoreCase))
        {
            executionResult = await _secretCommandModel.ExecuteWithSecretRelockAsync(
                () => _secretCommandModel.ExecuteAsync(parsedCommand));
            RefreshSecretAccessState();
        }
        else if (_commandExecutors.TryGetValue(executionHandler, out var commandExecutor))
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
        else if (_asyncCommandExecutors.TryGetValue(executionHandler, out var asyncCommandExecutor))
        {
            executionResult = await _secretCommandModel.ExecuteWithSecretRelockAsync(
                () => asyncCommandExecutor.ExecuteAsync(parsedCommand));
        }
        else
        {
            SetStatusMessage($"Command handler '{executionHandler}' is not supported.");
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
