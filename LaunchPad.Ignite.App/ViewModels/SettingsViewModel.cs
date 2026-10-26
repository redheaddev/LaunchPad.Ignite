using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Hotkeys;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.App.Services.Themes;
using LaunchPad.Ignite.App.Services.Secrets;
using LaunchPad.Ignite.App.Services.WindowBehavior.Linux;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ProfileSettingsStore _settingsStore;
    private readonly Action _settingsChanged;
    private readonly IReadOnlyList<CommandSettingDefinition> _applicationSettings;
    private readonly ExtensionStateStore _extensionStateStore;
    private readonly Action<ColorTheme> _themeChanged;
    private readonly IReadOnlyList<ExtensionSecretPermissionViewModel> _extensionSecretPermissions;
    private readonly IReadOnlyList<ObjectMetadataTarget> _secretMetadataTargets;
    private ObjectMetadataEditorViewModel _metadataEditor = null!;
    private MetadataObjectType _selectedMetadataObjectType = MetadataObjectType.Secret;
    private readonly IReadOnlyList<CommandPackSettingsViewModel> _commandPacks;
    private readonly IReadOnlyList<CommandPackSettingsViewModel> _ungroupedCommandPacks;
    private readonly IReadOnlyList<CommandPackGroupSettingsViewModel> _commandPackGroups;
    private readonly IReadOnlyList<CommandLaunchPreferenceViewModel> _commandLaunchPreferences;
    private readonly IReadOnlyList<CommandDetailsViewModel> _commandDetails;
    private readonly IReadOnlyList<EnumListEditorViewModel> _enumListEditors;
    private IReadOnlyList<MediaShortcutEditorViewModel> _mediaShortcutEditors = [];
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ListDefaultEditorViewModel>> _listDefaultEditorsByNamespace;
    private readonly Dictionary<string, Dictionary<string, string>> _themeColorDrafts = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ColorTheme> _colorThemes = [];
    private IReadOnlyList<ThemeColorEditorViewModel> _themeColors = [];
    private string _newThemeName = string.Empty;
    private string _renameThemeName = string.Empty;
    private string _themeReferenceUrl = string.Empty;
    private string _selectedCommandSourceFilter = "User";
    private IReadOnlyList<string> _commandSourceFilters = ["All", "User", "App"];
    private CommandDetailsViewModel? _selectedCommandDetails;
    private EnumListEditorViewModel? _selectedEnumListEditor;
    private GlobalActivationHotkey _activationHotkey;
    private GlobalActivationHotkey? _pendingActivationHotkey;
    private bool _isCapturingGlobalActivationHotkey;
    private string _globalActivationHotkeyStatus = string.Empty;

    private SettingsNavigationItem? _selectedNavigationItem;
    public IReadOnlyList<SettingsNavigationItem> NavigationItems { get; }
    public IReadOnlyList<SettingEditorViewModel> Settings { get; private set; } = [];
    public IReadOnlyList<ExtensionSecretPermissionViewModel> ExtensionSecretPermissions => _extensionSecretPermissions;
    public ObjectMetadataEditorViewModel MetadataEditor => _metadataEditor;
    public IReadOnlyList<MetadataObjectType> MetadataObjectTypes { get; } = [MetadataObjectType.Secret, MetadataObjectType.Snippet, MetadataObjectType.Macro, MetadataObjectType.Shortcut, MetadataObjectType.Command, MetadataObjectType.Function, MetadataObjectType.List, MetadataObjectType.Variable];
    public MetadataObjectType SelectedMetadataObjectType
    {
        get => _selectedMetadataObjectType;
        set
        {
            if (SetProperty(ref _selectedMetadataObjectType, value))
            {
                RefreshMetadataEditor();
            }
        }
    }

    private void RefreshMetadataEditor()
    {
        var targets = _secretMetadataTargets
            .Where(target => target.ObjectType == SelectedMetadataObjectType)
            .ToArray();
        _metadataEditor = new ObjectMetadataEditorViewModel(_settingsStore, SelectedMetadataObjectType, targets);
        OnPropertyChanged(nameof(MetadataEditor));
    }

    public IReadOnlyList<CommandPackSettingsViewModel> CommandPacks => _commandPacks;
    public IReadOnlyList<CommandPackSettingsViewModel> UngroupedCommandPacks => _ungroupedCommandPacks;
    public IReadOnlyList<CommandPackGroupSettingsViewModel> CommandPackGroups => _commandPackGroups;
    public IReadOnlyList<CommandLaunchPreferenceViewModel> CommandLaunchPreferences => _commandLaunchPreferences;
    public IReadOnlyList<MediaShortcutEditorViewModel> MediaShortcutEditors => _mediaShortcutEditors;
    public IReadOnlyList<CommandDetailsViewModel> CommandDetails => _commandDetails;
    public IReadOnlyList<CommandDetailsViewModel> FilteredCommandDetails => _commandDetails
        .Where(command => _selectedCommandSourceFilter switch
        {
            "User" => command.SourceLocation == "User",
            "App" => command.SourceLocation == "App",
            "All" => true,
            _ => string.Equals(command.SourceLocation, _selectedCommandSourceFilter, StringComparison.Ordinal)
        })
        .ToArray();
    public IReadOnlyList<string> CommandSourceFilters => _commandSourceFilters;
    public string SelectedCommandSourceFilter
    {
        get => _selectedCommandSourceFilter;
        set
        {
            if (SetProperty(ref _selectedCommandSourceFilter, value))
            {
                OnPropertyChanged(nameof(FilteredCommandDetails));
                SelectedCommandDetails = FilteredCommandDetails.FirstOrDefault();
            }
        }
    }
    public IReadOnlyList<EnumListEditorViewModel> EnumListEditors => _enumListEditors;
    public bool HasNoEnumLists => _enumListEditors.Count == 0;
    public EnumListEditorViewModel? SelectedEnumListEditor
    {
        get => _selectedEnumListEditor;
        set => SetProperty(ref _selectedEnumListEditor, value);
    }
    public CommandDetailsViewModel? SelectedCommandDetails
    {
        get => _selectedCommandDetails;
        set => SetProperty(ref _selectedCommandDetails, value);
    }

    public void OpenCommandEditor(string? commandName)
    {
        var command = string.IsNullOrWhiteSpace(commandName)
            ? null
            : _commandDetails.FirstOrDefault(details =>
                string.Equals(details.QualifiedName, commandName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (command is not null)
        {
            SelectedCommandSourceFilter = command.SourceLocation switch
            {
                "User" => "User",
                var source when source.StartsWith("Pack [", StringComparison.Ordinal) => source,
                var source when source.StartsWith("Extension [", StringComparison.Ordinal) => source,
                "App" => "App",
                _ => "All"
            };
        }
        SelectedNavigationItem = NavigationItems.FirstOrDefault(item => item.Category == "commands");
        if (command is not null)
        {
            SelectedCommandDetails = command;
        }
    }
    public IReadOnlyList<ListDefaultEditorViewModel> ListDefaultEditors => SelectedNavigationItem is { Category: "extension" } selected
        && _listDefaultEditorsByNamespace.TryGetValue(selected.Namespace, out var editors)
            ? editors
            : [];
    public ExtensionSecretPermissionViewModel? SelectedExtensionSecretPermissions => SelectedNavigationItem is { Category: "extension" } selected
        ? _extensionSecretPermissions.FirstOrDefault(extension => string.Equals(extension.ExtensionNamespace, selected.Namespace, StringComparison.OrdinalIgnoreCase))
        : null;
    public bool HasSelectedExtensionSecretPermissions => SelectedExtensionSecretPermissions is not null;
    public bool HasNoLists => IsExtensionSelected && ListDefaultEditors.Count == 0;
    public string Header { get; private set; } = "General";
    public string Subheader { get; private set; } = string.Empty;
    public bool IsExtensionEnabled { get; private set; } = true;
    public bool IsExtensionSelected => SelectedNavigationItem?.Category == "extension";
    public bool IsExtensionsSelected => SelectedNavigationItem?.Category == "extensions";
    public bool IsPacksSelected => SelectedNavigationItem?.Category == "packs";
    public bool IsThemeSelected => SelectedNavigationItem?.Category == "theme";
    public bool IsCommandsSelected => SelectedNavigationItem?.Category == "commands";
    public bool IsListsSelected => SelectedNavigationItem?.Category == "lists";
    public bool IsMetadataSelected => SelectedNavigationItem?.Category == "metadata";
    public bool IsApplicationSelected => SelectedNavigationItem?.Category == "application";
    public bool IsMediaExtensionSelected => SelectedNavigationItem is { Category: "extension" } selected
        && string.Equals(selected.Namespace, MediaShortcutSettings.Namespace, StringComparison.OrdinalIgnoreCase);
    public GlobalActivationHotkey ActivationHotkey
    {
        get => _activationHotkey;
        private set => SetProperty(ref _activationHotkey, value);
    }
    public string GlobalActivationHotkeyDisplay => ActivationHotkey.DisplayName;
    public string PendingGlobalActivationHotkeyDisplay => _pendingActivationHotkey?.DisplayName ?? string.Empty;
    public bool HasPendingGlobalActivationHotkey => _pendingActivationHotkey is not null;
    public bool CanSaveGlobalActivationHotkey => _pendingActivationHotkey is not null
        && _pendingActivationHotkey != ActivationHotkey;
    public bool IsLinuxX11Session => OperatingSystem.IsLinux()
        && LinuxSessionDetector.IsX11Session(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("DISPLAY"));
    public bool IsLinuxWaylandSession => OperatingSystem.IsLinux()
        && LinuxSessionDetector.IsWaylandSession(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    public bool IsLinuxSessionTypeUnknown => OperatingSystem.IsLinux()
        && !IsLinuxX11Session
        && !IsLinuxWaylandSession;
    public bool IsCapturingGlobalActivationHotkey
    {
        get => _isCapturingGlobalActivationHotkey;
        private set => SetProperty(ref _isCapturingGlobalActivationHotkey, value);
    }
    public string GlobalActivationHotkeyStatus
    {
        get => _globalActivationHotkeyStatus;
        private set => SetProperty(ref _globalActivationHotkeyStatus, value);
    }

    public event EventHandler? GlobalActivationHotkeyChanged;

    public void ReportGlobalActivationHotkeyStatus(string status)
    {
        GlobalActivationHotkeyStatus = status;
    }

    public bool IsSettingsListVisible => !IsExtensionSelected && !IsExtensionsSelected && !IsPacksSelected && !IsThemeSelected && !IsMetadataSelected && !IsCommandsSelected && !IsListsSelected;
    public IReadOnlyList<ColorTheme> ColorThemes => _colorThemes;
    public string NewThemeName
    {
        get => _newThemeName;
        set => SetProperty(ref _newThemeName, value);
    }

    public string RenameThemeName
    {
        get => _renameThemeName;
        set => SetProperty(ref _renameThemeName, value);
    }
    public string ThemeReferenceUrl
    {
        get => _themeReferenceUrl;
        set
        {
            if (SetProperty(ref _themeReferenceUrl, value))
            {
                OnPropertyChanged(nameof(HasUnsavedThemeReferenceUrlChanges));
            }
        }
    }
    public string ThemeImportUrl { get; set; } = "https://www.schemecolor.com/love-to-ride.php";
    public string ThemeImportStatus { get; private set; } = string.Empty;
    public string SelectedColorThemeId { get; private set; }
    public string? SelectedColorThemeUrl => _settingsStore.TryGetTheme(SelectedColorThemeId, out var theme) ? theme.Url : null;
    public bool HasSelectedColorThemeUrl => Uri.TryCreate(SelectedColorThemeUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
    public bool HasUnsavedThemeReferenceUrlChanges => !string.Equals(
        (ThemeReferenceUrl ?? string.Empty).Trim(), SelectedColorThemeUrl ?? string.Empty, StringComparison.Ordinal);
    public ColorTheme SelectedColorTheme => _colorThemes.FirstOrDefault(theme =>
        string.Equals(theme.Id, SelectedColorThemeId, StringComparison.OrdinalIgnoreCase))
        ?? ColorThemeService.Resolve(SelectedColorThemeId, _settingsStore);
    public bool CanDeleteSelectedColorTheme => !string.Equals(
        SelectedColorThemeId, ColorTheme.Default.Id, StringComparison.OrdinalIgnoreCase);
    public bool CanRenameSelectedColorTheme => CanDeleteSelectedColorTheme;
    public bool HasUnsavedThemeColorChanges => GetSelectedThemeDraft().Any(entry =>
        !string.Equals(entry.Value, GetPersistedThemeColor(SelectedColorThemeId, entry.Key), StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<ThemeColorEditorViewModel> ThemeColors => _themeColors;

    public SettingsViewModel(
        IReadOnlyList<CommandSettingDefinition> applicationSettings,
        LoadedExtensions extensions,
        ProfileSettingsStore settingsStore,
        ExtensionStateStore extensionStateStore,
        Action settingsChanged,
        Action<ColorTheme> themeChanged,
        IReadOnlyList<string>? availableSecretNames = null,
        IReadOnlyList<CommandDefinition>? commands = null,
        IReadOnlyList<CommandListDefinition>? lists = null,
        IReadOnlyDictionary<string, string>? commandSources = null,
        IReadOnlySet<string>? userCommandNames = null,
        Func<CommandDefinition, bool>? saveUserCommand = null,
        IReadOnlyDictionary<string, string>? commandPackSources = null,
        Func<string?, CommandDefinition, CommandDefinition, bool>? saveOwnedCommand = null,
        IReadOnlyList<LoadedCommandPackInfo>? commandPacks = null,
        IReadOnlyList<CommandListDefinition>? listDefaults = null,
        IReadOnlyList<string>? availableSecretNamesForMetadata = null,
        IReadOnlyList<SnippetDefinition>? availableSnippetsForMetadata = null,
        IReadOnlyList<MacroDefinition>? availableMacrosForMetadata = null,
        IReadOnlyList<CommandFunctionDefinition>? availableFunctionsForMetadata = null,
        IReadOnlyList<CommandVariableDefinition>? availableVariablesForMetadata = null)
    {
        _settingsStore = settingsStore;
        ActivationHotkey = GlobalActivationHotkey.TryParse(
            settingsStore.GetValue("app:globalActivationHotkey", GlobalActivationHotkey.DefaultValue),
            out var activationHotkey)
            ? activationHotkey
            : GlobalActivationHotkey.TryParse(GlobalActivationHotkey.DefaultValue, out var defaultHotkey)
                ? defaultHotkey
                : throw new InvalidOperationException("The default global activation hotkey is invalid.");
        _settingsChanged = settingsChanged;
        _applicationSettings = applicationSettings;
        _extensionStateStore = extensionStateStore;
        _themeChanged = themeChanged;
        _secretMetadataTargets = (availableSecretNamesForMetadata ?? [])
            .Select(name => new ObjectMetadataTarget(MetadataObjectType.Secret, name, name))
            .Concat((availableSnippetsForMetadata ?? []).Select(snippet =>
                new ObjectMetadataTarget(MetadataObjectType.Snippet, snippet.Metadata.Id, snippet.Metadata.Name)))
            .Concat((availableMacrosForMetadata ?? []).Select(macro =>
                new ObjectMetadataTarget(MetadataObjectType.Macro, macro.Metadata.Id, macro.Metadata.QualifiedName)))
            .Concat((commands ?? []).Where(command => !string.Equals(command.ExecutionHandler, "shortcut", StringComparison.OrdinalIgnoreCase))
                .Select(command => new ObjectMetadataTarget(MetadataObjectType.Command, command.QualifiedName, command.QualifiedName)))
            .Concat((commands ?? []).Where(command => string.Equals(command.ExecutionHandler, "shortcut", StringComparison.OrdinalIgnoreCase))
                .Select(command => new ObjectMetadataTarget(MetadataObjectType.Shortcut, command.QualifiedName, command.QualifiedName)))
            .Concat((lists ?? []).Select(list => new ObjectMetadataTarget(MetadataObjectType.List, list.QualifiedName, list.QualifiedName)))
            .Concat((availableFunctionsForMetadata ?? extensions.Schema.Functions).Select(function =>
                new ObjectMetadataTarget(MetadataObjectType.Function, function.QualifiedName, function.QualifiedName)))
            .Concat((availableVariablesForMetadata ?? extensions.Schema.Variables).Select(variable =>
                new ObjectMetadataTarget(MetadataObjectType.Variable, variable.QualifiedName, variable.QualifiedName)))
            .ToArray();
        RefreshMetadataEditor();
        var packInfos = (commandPacks ?? [])
            .OrderBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _commandPacks = packInfos
            .Select(pack => new CommandPackSettingsViewModel(
                pack.Id,
                pack.Commands,
                pack.Enabled,
                SetComponentEnabled,
                pack.Group))
            .ToArray();
        _ungroupedCommandPacks = _commandPacks
            .Where(pack => string.IsNullOrEmpty(pack.Group))
            .ToArray();
        _commandPackGroups = _commandPacks
            .Where(pack => !string.IsNullOrEmpty(pack.Group))
            .GroupBy(pack => pack.Group!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CommandPackGroupSettingsViewModel(
                group.Key,
                group.OrderBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
                SetComponentGroupEnabled))
            .ToArray();
        var commandList = commands ?? [];
        var launchPreferences = commandList
            .Where(command => string.Equals(command.ExecutionHandler, "shell", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(command => command.QualifiedName, command => new CommandLaunchPreferenceViewModel(
                command.QualifiedName,
                CommandLaunchPreferenceStore.GetSettingName(command),
                _settingsStore.GetValue(
                    CommandLaunchPreferenceStore.GetSettingName(command),
                    nameof(CommandLaunchMode.Normal)),
                SaveCommandLaunchMode), StringComparer.OrdinalIgnoreCase);
        _listDefaultEditorsByNamespace = (lists ?? [])
            .Where(list => !string.IsNullOrEmpty(list.Namespace))
            .GroupBy(list => list.Namespace!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ListDefaultEditorViewModel>)group
                    .OrderBy(list => $"{list.Type}:{list.Name}", StringComparer.OrdinalIgnoreCase)
                    .Select(list => new ListDefaultEditorViewModel(list, new ListDefaultOverrideStore(_settingsStore)))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        _commandLaunchPreferences = launchPreferences.Values
            .OrderBy(command => command.CommandName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var availableLists = (lists ?? []).Concat(extensions.Schema.Lists)
            .DistinctBy(list => $"{list.Type}:{list.QualifiedName}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var defaultListsByKey = (listDefaults ?? []).Concat(extensions.Schema.Lists)
            .DistinctBy(list => $"{list.Type}:{list.QualifiedName}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(list => $"{list.Type}:{list.QualifiedName}", StringComparer.OrdinalIgnoreCase);
        _enumListEditors = availableLists
            .Where(list => string.Equals(list.Type, "enum", StringComparison.OrdinalIgnoreCase))
            .OrderBy(list => list.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .Select(list =>
            {
                var defaultList = defaultListsByKey.GetValueOrDefault($"{list.Type}:{list.QualifiedName}") ?? list;
                return new EnumListEditorViewModel(
                    list,
                    new ListDefaultOverrideStore(_settingsStore),
                    _settingsChanged,
                    defaultList,
                    ResetListDefaultEditors);
            })
            .ToArray();
        var mediaCommandDefinitions = commandList
            .Where(MediaShortcutSettings.IsMediaCommand)
            .ToArray();
        _mediaShortcutEditors = mediaCommandDefinitions
            .SelectMany(command => command.Actions)
            .Where(action => action.KeyboardShortcut is not null)
            .Select(action => new MediaShortcutEditorViewModel(
                action.Name,
                action.Description,
                MediaShortcutSettings.GetDefaultShortcut(action.Name),
                commandList,
                _settingsStore,
                _settingsChanged))
            .ToArray();
        _commandDetails = commandList
            .OrderBy(command => command.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .Select(command =>
            {
                var isUserCommand = userCommandNames?.Contains(command.QualifiedName) == true;
                var packId = commandPackSources is not null && commandPackSources.TryGetValue(command.QualifiedName, out var sourcePackId)
                    ? sourcePackId
                    : null;
                return new CommandDetailsViewModel(
                    command,
                    commandList,
                    launchPreferences.GetValueOrDefault(command.QualifiedName),
                    CommandSourceLabels.GetLabel(command, commandSources, userCommandNames, commandPackSources),
                    saveUserCommand,
                    isUserCommand,
                    availableLists,
                    packId,
                    saveOwnedCommand);
            })
            .ToArray();
        _commandSourceFilters = [
            "All",
            "User",
            "App",
            .. _commandDetails
                .Where(command => command.SourceLocation.StartsWith("Pack [", StringComparison.Ordinal)
                    || command.SourceLocation.StartsWith("Extension [", StringComparison.Ordinal))
                .Select(command => command.SourceLocation)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
        ];
        var secretNames = _settingsStore.GetValues()
            .Where(pair => pair.Key.StartsWith("secretPermissions:", StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => ParseSecretNames(pair.Value))
            .Concat(availableSecretNames ?? [])
            .Concat(extensions.Schema.Settings
                .Where(setting => setting.SecretReference)
                .Select(setting => _settingsStore.GetValue(setting.QualifiedName, setting.DefaultValue)))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _extensionSecretPermissions = extensions.Extensions
            .OrderBy(extension => extension.Id, StringComparer.OrdinalIgnoreCase)
            .Select(extension => new ExtensionSecretPermissionViewModel(
                extension.Id,
                extension.Namespace,
                secretNames,
                ReadGrantedSecrets(extension.Namespace),
                SaveGrantedSecrets))
            .ToArray();
        SelectedColorThemeId = _settingsStore.GetValue("app:colorTheme", ColorTheme.Default.Id);
        ColorThemeService.MigrateLegacyThemes(_settingsStore);
        ThemeReferenceUrl = SelectedColorThemeUrl ?? string.Empty;
        RefreshColorThemes();
        RefreshThemeColors();
        var extensionSettings = extensions.Schema.Settings
            .GroupBy(setting => setting.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CommandSettingDefinition>)group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var navigation = new List<SettingsNavigationItem>
        {
            new("General", "application", "") { AvailableSettings = applicationSettings },
            new("Commands", "commands", ""),
            new("Packs", "packs", ""),
            new("Lists", "lists", ""),
            new("Metadata", "metadata", ""),
            new("Theme", "theme", ""),
            new("Extensions", "extensions", "")
        };
        foreach (var extension in extensions.Extensions.OrderBy(extension => extension.Id, StringComparer.OrdinalIgnoreCase))
        {
            extensionSettings.TryGetValue(extension.Namespace, out var settingsForExtension);
            var extensionLabel = settingsForExtension?.FirstOrDefault()?.Namespace ?? extension.Id;
            navigation.Add(new SettingsNavigationItem(extensionLabel, "extension", extension.Id)
            {
                IsEnabled = extension.Enabled,
                Namespace = extension.Namespace,
                AvailableSettings = settingsForExtension ?? []
            });
        }

        NavigationItems = navigation;
        SelectedNavigationItem = navigation.FirstOrDefault(item => item.Category == "application");
        SelectedCommandDetails = FilteredCommandDetails.FirstOrDefault();
    }

    public void BeginGlobalActivationHotkeyCapture()
    {
        IsCapturingGlobalActivationHotkey = true;
        GlobalActivationHotkeyStatus = "Press a key chord with at least one modifier (Ctrl, Alt, Shift, or Win). Press Esc to cancel.";
    }

    public void CancelGlobalActivationHotkeyCapture()
    {
        IsCapturingGlobalActivationHotkey = false;
        _pendingActivationHotkey = null;
        OnPropertyChanged(nameof(PendingGlobalActivationHotkeyDisplay));
        OnPropertyChanged(nameof(HasPendingGlobalActivationHotkey));
        OnPropertyChanged(nameof(CanSaveGlobalActivationHotkey));
        GlobalActivationHotkeyStatus = "Hotkey change canceled.";
    }

    public bool TrySetGlobalActivationHotkey(bool control, bool alt, bool shift, bool meta, string keyName)
    {
        IsCapturingGlobalActivationHotkey = false;
        if (!GlobalActivationHotkey.TryCreate(control, alt, shift, meta, keyName, out var hotkey))
        {
            GlobalActivationHotkeyStatus = "That chord is not supported. Choose a non-modifier key and at least one modifier.";
            return false;
        }

        _pendingActivationHotkey = hotkey;
        OnPropertyChanged(nameof(PendingGlobalActivationHotkeyDisplay));
        OnPropertyChanged(nameof(HasPendingGlobalActivationHotkey));
        OnPropertyChanged(nameof(CanSaveGlobalActivationHotkey));
        GlobalActivationHotkeyStatus = hotkey == ActivationHotkey
            ? $"{hotkey.DisplayName} is already the active shortcut."
            : $"Captured {hotkey.DisplayName}. Save to apply it.";
        return true;
    }

    public bool SaveGlobalActivationHotkey()
    {
        if (_pendingActivationHotkey is not { } hotkey)
        {
            return false;
        }

        _pendingActivationHotkey = null;
        OnPropertyChanged(nameof(PendingGlobalActivationHotkeyDisplay));
        OnPropertyChanged(nameof(HasPendingGlobalActivationHotkey));
        OnPropertyChanged(nameof(CanSaveGlobalActivationHotkey));
        if (hotkey == ActivationHotkey)
        {
            GlobalActivationHotkeyStatus = $"{hotkey.DisplayName} is already the active shortcut.";
            return false;
        }

        ActivationHotkey = hotkey;
        _settingsStore.SetValue("app:globalActivationHotkey", hotkey.ToSettingValue());
        OnPropertyChanged(nameof(GlobalActivationHotkeyDisplay));
        GlobalActivationHotkeyStatus = $"Saved {hotkey.DisplayName}. Updating the global shortcut...";
        GlobalActivationHotkeyChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RefreshColorThemes()
    {
        _colorThemes = ColorThemeService.LoadThemes(_settingsStore);
        OnPropertyChanged(nameof(ColorThemes));
    }

    public bool CreateTheme(string? name)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            ThemeImportStatus = "Enter a name for the new theme.";
            OnPropertyChanged(nameof(ThemeImportStatus));
            return false;
        }

        var themeId = $"custom-{Guid.NewGuid():N}";
        var source = SelectedColorTheme;
        var newTheme = source with { Id = themeId, Name = normalizedName };
        _settingsStore.SetTheme(ColorThemeService.ToProfileTheme(newTheme));

        NewThemeName = string.Empty;
        RefreshColorThemes();
        SelectColorTheme(themeId);
        ThemeImportStatus = $"Created '{normalizedName}' as a new theme.";
        OnPropertyChanged(nameof(ThemeImportStatus));
        return true;
    }

    public async Task ImportThemeColorsAsync(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            ThemeImportStatus = "Enter a valid HTTPS palette URL.";
            OnPropertyChanged(nameof(ThemeImportStatus));
            return;
        }

        try
        {
            var imported = await ThemePaletteImporter.ImportAsync(uri, ColorTheme.Default);
            var themeId = $"imported-{Guid.NewGuid():N}";
            imported = imported with { Id = themeId, Name = GetImportedThemeName(uri) };

            _settingsStore.SetTheme(ColorThemeService.ToProfileTheme(imported) with { Url = uri.AbsoluteUri });

            RefreshColorThemes();
            SelectColorTheme(themeId);
            OnPropertyChanged(nameof(SelectedColorThemeUrl));
            ThemeImportStatus = $"Imported '{imported.Name}' as a new theme. Existing themes were not changed.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or ArgumentException or TaskCanceledException)
        {
            ThemeImportStatus = $"Could not import palette: {exception.Message}";
        }

        OnPropertyChanged(nameof(ThemeImportStatus));
    }

    private static string GetImportedThemeName(Uri uri)
    {
        var slug = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? "");
        if (slug.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
        {
            slug = slug[..^4];
        }

        var name = string.Join(' ', slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        return string.IsNullOrWhiteSpace(name) ? "Imported theme" : name;
    }

    private static string GetColor(ColorTheme theme, string property) => property switch
    {
        nameof(ColorTheme.WindowBackground) => theme.WindowBackground,
        nameof(ColorTheme.PanelBackground) => theme.PanelBackground,
        nameof(ColorTheme.InputBackground) => theme.InputBackground,
        nameof(ColorTheme.Border) => theme.Border,
        nameof(ColorTheme.Text) => theme.Text,
        nameof(ColorTheme.SecondaryText) => theme.SecondaryText,
        nameof(ColorTheme.MutedText) => theme.MutedText,
        nameof(ColorTheme.Accent) => theme.Accent,
        nameof(ColorTheme.Highlight) => theme.Highlight,
        nameof(ColorTheme.TextSelection) => theme.TextSelection,
        nameof(ColorTheme.CommandFocus) => theme.CommandFocus,
        nameof(ColorTheme.NavigationSelected) => theme.NavigationSelected,
        nameof(ColorTheme.Target) => theme.Target,
        nameof(ColorTheme.RequiredArgument) => theme.RequiredArgument,
        nameof(ColorTheme.OptionalArgument) => theme.OptionalArgument,
        nameof(ColorTheme.Checked) => theme.Checked,
        nameof(ColorTheme.Status) => theme.Status,
        _ => string.Empty
    };

    public SettingsNavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (SetProperty(ref _selectedNavigationItem, value) && value is not null)
            {
                UpdateSelection(value);
            }
        }
    }

    private void UpdateSelection(SettingsNavigationItem value)
    {
        Header = value.Name;
        if (value.Category == "extensions")
        {
            Subheader = "Select an extension below to configure its settings, secrets, and enabled status.";
            Settings = [];
        }
        else if (value.Category == "packs")
        {
            Subheader = "Enable or disable installed command packs.";
            Settings = [];
        }
        else if (value.Category == "extension")
        {
            var settingDefinitions = value.AvailableSettings;
            Subheader = $"{settingDefinitions.Count} setting(s) available. Token prefix: {value.Id}";
            IsExtensionEnabled = _extensionStateStore.IsEnabled(value.Id);
            Settings = settingDefinitions.Select(CreateEditor).ToArray();
            Subheader = ListDefaultEditors.Count == 0
                ? $"{settingDefinitions.Count} setting(s) available. Token prefix: {value.Id}"
                : $"{settingDefinitions.Count} setting(s) available. Token prefix: {value.Id}. Configure extension list defaults below.";
        }
        else if (value.Category == "application")
        {
            Subheader = "Application settings";
            Settings = _applicationSettings.Select(CreateEditor).ToArray();
        }
        else if (value.Category == "theme")
        {
            Subheader = "Choose the color palette used throughout LaunchPad.";
            Settings = [];
        }
        else if (value.Category == "commands")
        {
            Subheader = "Choose whether each shell command runs normally, elevated, or as another user when you press Enter.";
            Settings = [];
        }
        else if (value.Category == "lists")
        {
            Subheader = "Edit enum list values and choose the default. The default is listed first; remaining values are alphabetical.";
            Settings = [];
            SelectedEnumListEditor = _enumListEditors.FirstOrDefault();
        }
        else if (value.Category == "metadata")
        {
            Subheader = "Define metadata fields and values for supported object types.";
            Settings = [];
        }
        else
        {
            Subheader = string.Empty;
            Settings = [];
        }

        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(Subheader));
        OnPropertyChanged(nameof(IsExtensionEnabled));
        OnPropertyChanged(nameof(IsExtensionSelected));
        OnPropertyChanged(nameof(IsMediaExtensionSelected));
        OnPropertyChanged(nameof(IsExtensionsSelected));
        OnPropertyChanged(nameof(IsPacksSelected));
        OnPropertyChanged(nameof(IsThemeSelected));
        OnPropertyChanged(nameof(IsSettingsListVisible));
        OnPropertyChanged(nameof(IsMetadataSelected));
        OnPropertyChanged(nameof(IsCommandsSelected));
        OnPropertyChanged(nameof(IsListsSelected));
        OnPropertyChanged(nameof(IsApplicationSelected));
        OnPropertyChanged(nameof(IsExtensionSelected));
        OnPropertyChanged(nameof(IsMetadataSelected));
        OnPropertyChanged(nameof(ExtensionSecretPermissions));
        OnPropertyChanged(nameof(SelectedExtensionSecretPermissions));
        OnPropertyChanged(nameof(HasSelectedExtensionSecretPermissions));
        OnPropertyChanged(nameof(CommandLaunchPreferences));
        OnPropertyChanged(nameof(CommandDetails));
        OnPropertyChanged(nameof(MediaShortcutEditors));
        OnPropertyChanged(nameof(EnumListEditors));
        OnPropertyChanged(nameof(ListDefaultEditors));
        OnPropertyChanged(nameof(HasNoLists));
        OnPropertyChanged(nameof(Settings));
    }

    public void SelectColorTheme(string? themeId)
    {
        var theme = ColorThemeService.Resolve(themeId, _settingsStore);
        if (string.Equals(SelectedColorThemeId, theme.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelectedColorThemeId = theme.Id;
        RenameThemeName = theme.Name;
        ThemeReferenceUrl = SelectedColorThemeUrl ?? string.Empty;
        OnPropertyChanged(nameof(SelectedColorThemeId));
        OnPropertyChanged(nameof(SelectedColorTheme));
        OnPropertyChanged(nameof(SelectedColorThemeUrl));
        OnPropertyChanged(nameof(HasSelectedColorThemeUrl));
        OnPropertyChanged(nameof(CanDeleteSelectedColorTheme));
        OnPropertyChanged(nameof(CanRenameSelectedColorTheme));
        _settingsStore.SetValue("app:colorTheme", theme.Id);
        _themeChanged(theme);
        RefreshThemeColors();
    }

    public bool RenameSelectedColorTheme(string? name)
    {
        var normalizedName = name?.Trim();
        if (!ColorThemeService.RenameTheme(SelectedColorThemeId, normalizedName, _settingsStore))
        {
            ThemeImportStatus = string.IsNullOrWhiteSpace(normalizedName)
                ? "Enter a name for the theme."
                : "Default cannot be renamed.";
            OnPropertyChanged(nameof(ThemeImportStatus));
            return false;
        }

        RefreshColorThemes();
        OnPropertyChanged(nameof(SelectedColorTheme));
        RenameThemeName = normalizedName!;
        ThemeImportStatus = $"Renamed theme to '{normalizedName}'.";
        OnPropertyChanged(nameof(ThemeImportStatus));
        return true;
    }

    public bool SaveThemeReferenceUrl()
    {
        var normalizedUrl = (ThemeReferenceUrl ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(normalizedUrl)
            && (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            ThemeImportStatus = "Enter a valid HTTPS reference URL, or leave it blank to clear it.";
            OnPropertyChanged(nameof(ThemeImportStatus));
            return false;
        }

        var hasTheme = _settingsStore.TryGetTheme(SelectedColorThemeId, out var theme);
        theme ??= ColorThemeService.ToProfileTheme(SelectedColorTheme);
        _settingsStore.SetTheme(theme with { Url = string.IsNullOrEmpty(normalizedUrl) ? null : normalizedUrl });
        OnPropertyChanged(nameof(SelectedColorThemeUrl));
        OnPropertyChanged(nameof(HasSelectedColorThemeUrl));
        OnPropertyChanged(nameof(HasUnsavedThemeReferenceUrlChanges));
        if (!hasTheme && string.Equals(SelectedColorThemeId, ColorTheme.Default.Id, StringComparison.OrdinalIgnoreCase))
        {
            RefreshColorThemes();
        }

        ThemeImportStatus = string.IsNullOrEmpty(normalizedUrl)
            ? $"Cleared the reference URL for '{SelectedColorTheme.Name}'."
            : $"Saved the reference URL for '{SelectedColorTheme.Name}'.";
        OnPropertyChanged(nameof(ThemeImportStatus));
        return true;
    }

    public bool DeleteSelectedColorTheme()
    {
        var themeId = SelectedColorThemeId;
        if (!ColorThemeService.DeleteTheme(themeId, _settingsStore))
        {
            return false;
        }

        if (string.Equals(themeId, SelectedColorThemeId, StringComparison.OrdinalIgnoreCase))
        {
            SelectedColorThemeId = ColorTheme.Default.Id;
            _settingsStore.SetValue("app:colorTheme", ColorTheme.Default.Id);
            OnPropertyChanged(nameof(SelectedColorThemeId));
            OnPropertyChanged(nameof(SelectedColorTheme));
            _themeChanged(ColorThemeService.Resolve(ColorTheme.Default.Id, _settingsStore));
        }

        RefreshColorThemes();
        OnPropertyChanged(nameof(CanDeleteSelectedColorTheme));
        RefreshThemeColors();
        ThemeImportStatus = $"Deleted '{themeId}'.";
        OnPropertyChanged(nameof(ThemeImportStatus));
        return true;
    }

    public void SetThemeColor(string property, string? value)
    {
        if (!ColorTheme.EditableColors.Any(colorEntry => colorEntry.Property == property) || value is null)
        {
            return;
        }

        var editor = _themeColors.FirstOrDefault(colorEntry => colorEntry.Property == property);
        if (editor is not null)
        {
            editor.Update(value);
        }
    }

    public bool SaveThemeColorChanges()
    {
        var draft = GetSelectedThemeDraft();
        if (draft.Any(entry => !ColorThemeService.IsValidHexColor(entry.Value) || !Color.TryParse(entry.Value, out _)))
        {
            ThemeImportStatus = "Fix invalid color values before saving. Use #RRGGBB or #AARRGGBB.";
            OnPropertyChanged(nameof(ThemeImportStatus));
            return false;
        }

        var values = draft.Select(entry => new KeyValuePair<string, string>(entry.Key, Color.Parse(entry.Value).ToString()));
        ColorThemeService.SaveColorOverrides(SelectedColorThemeId, values, _settingsStore);

        var theme = ColorThemeService.Load(
            ColorTheme.Default with { Id = SelectedColorThemeId, Name = SelectedColorTheme.Name },
            _settingsStore);
        _themeChanged(theme);
        foreach (var editor in _themeColors)
        {
            editor.Update(GetColor(theme, editor.Property));
        }

        ThemeImportStatus = $"Saved changes to '{theme.Name}'.";
        OnPropertyChanged(nameof(ThemeImportStatus));
        OnPropertyChanged(nameof(HasUnsavedThemeColorChanges));
        return true;
    }

    private void RefreshThemeColors()
    {
        _themeColors = ColorTheme.EditableColors
            .Select(color => new ThemeColorEditorViewModel(color.Name, color.Property, color.Usage, GetDraftThemeColor(SelectedColorThemeId, color.Property), StageThemeColor))
            .ToArray();
        OnPropertyChanged(nameof(ThemeColors));
        OnPropertyChanged(nameof(HasUnsavedThemeColorChanges));
    }

    private Dictionary<string, string> GetSelectedThemeDraft()
    {
        if (!_themeColorDrafts.TryGetValue(SelectedColorThemeId, out var draft))
        {
            draft = ColorTheme.EditableColors.ToDictionary(
                color => color.Property,
                color => GetPersistedThemeColor(SelectedColorThemeId, color.Property),
                StringComparer.OrdinalIgnoreCase);
            _themeColorDrafts[SelectedColorThemeId] = draft;
        }

        return draft;
    }

    private string GetDraftThemeColor(string themeId, string property)
    {
        if (!_themeColorDrafts.TryGetValue(themeId, out var draft))
        {
            draft = ColorTheme.EditableColors.ToDictionary(
                color => color.Property,
                color => GetPersistedThemeColor(themeId, color.Property),
                StringComparer.OrdinalIgnoreCase);
            _themeColorDrafts[themeId] = draft;
        }

        return draft.GetValueOrDefault(property, string.Empty);
    }

    private string GetPersistedThemeColor(string themeId, string property)
    {
        var theme = ColorThemeService.Resolve(themeId, _settingsStore);
        return GetColor(theme, property);
    }

    private void StageThemeColor(string property, string value)
    {
        if (!ColorTheme.EditableColors.Any(color => color.Property == property))
        {
            return;
        }

        GetSelectedThemeDraft()[property] = value;
        OnPropertyChanged(nameof(HasUnsavedThemeColorChanges));
    }

    private string GetThemeColor(string property)
    {
        var theme = SelectedColorTheme;
        return property switch
        {
            nameof(ColorTheme.WindowBackground) => theme.WindowBackground,
            nameof(ColorTheme.PanelBackground) => theme.PanelBackground,
            nameof(ColorTheme.InputBackground) => theme.InputBackground,
            nameof(ColorTheme.Border) => theme.Border,
            nameof(ColorTheme.Text) => theme.Text,
            nameof(ColorTheme.SecondaryText) => theme.SecondaryText,
            nameof(ColorTheme.MutedText) => theme.MutedText,
            nameof(ColorTheme.Accent) => theme.Accent,
            nameof(ColorTheme.Highlight) => theme.Highlight,
            nameof(ColorTheme.TextSelection) => theme.TextSelection,
            nameof(ColorTheme.CommandFocus) => theme.CommandFocus,
            nameof(ColorTheme.NavigationSelected) => theme.NavigationSelected,
            nameof(ColorTheme.Target) => theme.Target,
            nameof(ColorTheme.RequiredArgument) => theme.RequiredArgument,
            nameof(ColorTheme.OptionalArgument) => theme.OptionalArgument,
            nameof(ColorTheme.Checked) => theme.Checked,
            nameof(ColorTheme.Status) => theme.Status,
            _ => string.Empty
        };
    }

    public void RefreshSelectedColorTheme()
    {
        OnPropertyChanged(nameof(SelectedColorTheme));
    }

    public sealed class ThemeColorEditorViewModel : ViewModelBase
    {
        private string _value;

        public string Name { get; }
        public string Property { get; }
        public string Usage { get; }
        private readonly Action<string, string> _valueChanged;
        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    OnPropertyChanged(nameof(PickerColor));
                    OnPropertyChanged(nameof(PickerBrush));
                    _valueChanged(Property, value);
                }
            }
        }
        public Color PickerColor
        {
            get => Color.TryParse(Value, out var color) ? color : Colors.Transparent;
            set => Value = value.ToString();
        }
        public IBrush PickerBrush => new SolidColorBrush(PickerColor);

        public ThemeColorEditorViewModel(string name, string property, string usage, string value, Action<string, string> valueChanged)
        {
            Name = name;
            Property = property;
            Usage = usage;
            _value = value;
            _valueChanged = valueChanged;
        }

        public void Update(string value) => Value = value;
    }

    private void SaveCommandLaunchMode(string settingName, string launchMode)
    {
        if (Enum.TryParse<CommandLaunchMode>(launchMode, ignoreCase: true, out var parsedMode))
        {
            if (parsedMode == CommandLaunchMode.Normal)
            {
                _settingsStore.RemoveValue(settingName);
            }
            else
            {
                _settingsStore.SetValue(settingName, parsedMode.ToString());
            }

            _settingsChanged();
        }
    }

    private IReadOnlyList<string> ReadGrantedSecrets(string extensionNamespace)
    {
        var value = _settingsStore.GetValue(ExtensionSecretAccessPolicy.GetSettingName(extensionNamespace), "[]");
        return ParseSecretNames(value);
    }

    private void SaveGrantedSecrets(string extensionNamespace, IReadOnlyList<string> secretNames)
    {
        var key = ExtensionSecretAccessPolicy.GetSettingName(extensionNamespace);
        if (secretNames.Count == 0)
        {
            _settingsStore.RemoveValue(key);
        }
        else
        {
            _settingsStore.SetValue(key, System.Text.Json.JsonSerializer.Serialize(secretNames));
        }

        _settingsChanged();
    }

    private static IReadOnlyList<string> ParseSecretNames(string serialized)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(serialized) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private SettingEditorViewModel CreateEditor(CommandSettingDefinition definition)
    {
        var value = _settingsStore.GetValue(definition.QualifiedName, definition.DefaultValue);
        return new SettingEditorViewModel(definition, value, _settingsStore, _settingsChanged);
    }

    [RelayCommand]
    private void ToggleExtension()
    {
        if (SelectedNavigationItem?.Category != "extension")
        {
            return;
        }

        _extensionStateStore.SetEnabled(SelectedNavigationItem.Id, IsExtensionEnabled);
        _settingsChanged();
    }

    public void SetExtensionEnabled(bool enabled)
    {
        if (_selectedNavigationItem?.Category == "extension")
        {
            _extensionStateStore.SetEnabled(_selectedNavigationItem.Id, enabled);
            _settingsChanged();
        }

        IsExtensionEnabled = enabled;
        OnPropertyChanged(nameof(IsExtensionEnabled));
    }

    private void SetComponentEnabled(string id, bool enabled)
    {
        _extensionStateStore.SetEnabled(id, enabled);
        _settingsChanged();
    }

    private void SetComponentGroupEnabled(IReadOnlyList<string> ids, bool enabled)
    {
        _extensionStateStore.SetEnabled(ids, enabled);
        _settingsChanged();
    }

    private void ResetListDefaultEditors(CommandListDefinition defaultList)
    {
        foreach (var editor in _listDefaultEditorsByNamespace.Values.SelectMany(editors => editors)
            .Where(editor => string.Equals(editor.ListName, $"{defaultList.Type}:{defaultList.QualifiedName}", StringComparison.OrdinalIgnoreCase)))
        {
            editor.ResetToDefaults(defaultList);
        }
    }

    public sealed class SettingsNavigationItem(string name, string category, string id)
    {
        public string Name { get; } = name;
        public string Category { get; } = category;
        public string Id { get; } = id;
        public string Namespace { get; init; } = string.Empty;
        public Thickness Indent => Category == "extension" ? new Thickness(18, 0, 0, 0) : default;
        public bool IsEnabled { get; init; }
        public IReadOnlyList<CommandSettingDefinition> AvailableSettings { get; init; } = [];
        public CommandSettingDefinition? SettingDefinition { get; init; }
    }
}

public sealed class CommandPackSettingsViewModel : ViewModelBase
{
    private readonly Action<string, bool> _setEnabled;
    private bool _isEnabled;
    private bool _isExpanded;

    public string Id { get; }
    public string? Group { get; }
    public IReadOnlyList<string> Commands { get; }
    public string Details => Commands.Count == 1 ? "1 command" : $"{Commands.Count} commands";
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _setEnabled(Id, value);
            }
        }
    }

    public CommandPackSettingsViewModel(
        string id,
        IReadOnlyList<string> commands,
        bool isEnabled,
        Action<string, bool> setEnabled,
        string? group = null)
    {
        Id = id;
        Commands = commands;
        _isEnabled = isEnabled;
        _setEnabled = setEnabled;
        Group = group;
    }

    internal void SetEnabledFromGroup(bool enabled)
        => SetProperty(ref _isEnabled, enabled, nameof(IsEnabled));
}

public sealed class CommandPackGroupSettingsViewModel : ViewModelBase
{
    private readonly IReadOnlyList<CommandPackSettingsViewModel> _packs;
    private readonly Action<IReadOnlyList<string>, bool> _setEnabled;

    public string Name { get; }
    public IReadOnlyList<CommandPackSettingsViewModel> Packs => _packs;
    public string Details => _packs.Count == 1 ? "1 pack" : $"{_packs.Count} packs";
    public bool? IsEnabled
    {
        get => _packs.All(pack => pack.IsEnabled)
            ? true
            : _packs.All(pack => !pack.IsEnabled) ? false : null;
        set
        {
            if (value.HasValue)
            {
                SetEnabled(value.Value);
            }
        }
    }

    public CommandPackGroupSettingsViewModel(
        string name,
        IReadOnlyList<CommandPackSettingsViewModel> packs,
        Action<IReadOnlyList<string>, bool> setEnabled)
    {
        Name = name;
        _packs = packs;
        _setEnabled = setEnabled;
        foreach (var pack in _packs)
        {
            pack.PropertyChanged += Pack_PropertyChanged;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
        {
            return;
        }

        foreach (var pack in _packs)
        {
            pack.SetEnabledFromGroup(enabled);
        }
        _setEnabled(_packs.Select(pack => pack.Id).ToArray(), enabled);
        OnPropertyChanged(nameof(IsEnabled));
    }

    private void Pack_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(CommandPackSettingsViewModel.IsEnabled))
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
    }
}

public sealed class ListDefaultEditorViewModel : ViewModelBase
{
    public const string ExtensionDefaultOption = "(Use extension default)";

    private readonly CommandListDefinition _list;
    private readonly ListDefaultOverrideStore _overrideStore;
    private string _selectedDefault;

    public string ListName => $"{_list.Type}:{_list.QualifiedName}";
    public IReadOnlyList<string> Values { get; private set; }
    public string SelectedDefault
    {
        get => _selectedDefault;
        set
        {
            if (SetProperty(ref _selectedDefault, value))
            {
                _overrideStore.SetDefault(
                    _list,
                    string.Equals(value, ExtensionDefaultOption, StringComparison.Ordinal) ? null : value);
            }
        }
    }

    public ListDefaultEditorViewModel(CommandListDefinition list, ListDefaultOverrideStore overrideStore)
    {
        _list = list;
        _overrideStore = overrideStore;
        Values = new[] { ExtensionDefaultOption }.Concat(list.Values).ToArray();
        _selectedDefault = overrideStore.GetDefaultOverride(list) ?? ExtensionDefaultOption;
    }

    public void ResetToDefaults(CommandListDefinition defaultList)
    {
        Values = new[] { ExtensionDefaultOption }.Concat(defaultList.Values).ToArray();
        SelectedDefault = ExtensionDefaultOption;
        OnPropertyChanged(nameof(Values));
    }
}

public sealed partial class EnumListEditorViewModel : ViewModelBase
{
    private readonly CommandListDefinition _list;
    private readonly CommandListDefinition _defaultList;
    private readonly ListDefaultOverrideStore _overrideStore;
    private readonly Action _settingsChanged;
    private readonly Action<CommandListDefinition>? _resetRelatedListDefaults;
    private string _valuesText;
    private string _selectedDefault;
    private string _status = string.Empty;

    public string ListName => $"{_list.Type}:{_list.QualifiedName}";
    public string ValuesText
    {
        get => _valuesText;
        set
        {
            if (SetProperty(ref _valuesText, value))
            {
                OnPropertyChanged(nameof(Values));
            }
        }
    }
    public IReadOnlyList<string> Values => ParseValues(ValuesText);
    public string SelectedDefault
    {
        get => _selectedDefault;
        set => SetProperty(ref _selectedDefault, value);
    }
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public EnumListEditorViewModel(
        CommandListDefinition list,
        ListDefaultOverrideStore overrideStore,
        Action settingsChanged,
        CommandListDefinition? defaultList = null,
        Action<CommandListDefinition>? resetRelatedListDefaults = null)
    {
        _list = list;
        _defaultList = defaultList ?? list;
        _overrideStore = overrideStore;
        _settingsChanged = settingsChanged;
        _resetRelatedListDefaults = resetRelatedListDefaults;
        _valuesText = string.Join(Environment.NewLine, list.Values);
        _selectedDefault = list.Default ?? list.Values.FirstOrDefault() ?? string.Empty;
    }

    public void ResetToDefaults()
    {
        _overrideStore.ResetList(_list);
        ValuesText = string.Join(Environment.NewLine, _defaultList.Values);
        SelectedDefault = _defaultList.Default ?? _defaultList.Values.FirstOrDefault() ?? string.Empty;
        _resetRelatedListDefaults?.Invoke(_defaultList);
        Status = "List reset to defaults.";
        _settingsChanged();
    }

    [RelayCommand]
    private void Save() => SaveChanges();

    [RelayCommand]
    private void Reset() => ResetToDefaults();

    public bool SaveChanges()
    {
        try
        {
            var values = ParseValues(ValuesText);
            var canonicalDefault = values.FirstOrDefault(value =>
                string.Equals(value, SelectedDefault, StringComparison.OrdinalIgnoreCase));
            if (values.Count == 0 || canonicalDefault is null)
            {
                Status = "Add at least one value and select a default that is in the list.";
                return false;
            }

            _overrideStore.SetList(_list, values, canonicalDefault);
            ValuesText = string.Join(Environment.NewLine, _overrideStore.ApplyOverrides([_list]).Single().Values);
            SelectedDefault = canonicalDefault;
            Status = "List saved.";
            _settingsChanged();
            return true;
        }
        catch (ArgumentException exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    private static IReadOnlyList<string> ParseValues(string? text)
        => (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed partial class CommandDetailsViewModel : ViewModelBase
{
    private readonly IReadOnlyList<CommandDefinition> _allCommands;
    private readonly CommandLaunchPreferenceViewModel? _launchPreference;
    private readonly Func<CommandDefinition, bool>? _saveUserCommand;
    private readonly Func<string?, CommandDefinition, CommandDefinition, bool>? _saveOwnedCommand;
    private readonly string? _packId;
    private CommandDefinition _originalCommand;
    private CommandDefinition _command;
    private string _description;
    private string _name;
    private string _namespace;
    private string? _keyboardShortcut;
    private bool _executeOnKeyboardShortcut;
    private string _shortcutStatus = string.Empty;
    private bool _isCapturingShortcut;
    private bool _hasUnsavedChanges;
    private CommandActionEditorViewModel? _selectedAction;

    public string Name
    {
        get => _name;
        set
        {
            if (IsEditable && SetProperty(ref _name, value))
            {
                UpdateIdentity();
            }
        }
    }
    public string Namespace
    {
        get => _namespace;
        set
        {
            if (IsEditable && SetProperty(ref _namespace, value))
            {
                UpdateIdentity();
            }
        }
    }
    public string QualifiedName => _command.QualifiedName;
    public string? KeyboardShortcut
    {
        get => _keyboardShortcut;
        set
        {
            if (IsEditable && !string.Equals(_keyboardShortcut, value, StringComparison.Ordinal))
            {
                UpdateCommandShortcut(value);
            }
        }
    }
    public bool ExecuteOnKeyboardShortcut
    {
        get => _executeOnKeyboardShortcut;
        set
        {
            if (IsEditable && SetProperty(ref _executeOnKeyboardShortcut, value))
            {
                _command = _command with { ExecuteOnKeyboardShortcut = value };
                HasUnsavedChanges = true;
            }
        }
    }
    public string ShortcutStatus
    {
        get => _shortcutStatus;
        private set => SetProperty(ref _shortcutStatus, value);
    }
    public bool IsCapturingShortcut
    {
        get => _isCapturingShortcut;
        private set => SetProperty(ref _isCapturingShortcut, value);
    }
    public string Kind { get; }
    public string SourceLocation { get; }
    public string Description
    {
        get => _description;
        set
        {
            if (IsEditable && SetProperty(ref _description, value))
            {
                UpdateDescription(value);
            }
        }
    }
    public ObservableCollection<CommandActionEditorViewModel> Actions { get; } = [];
    public IReadOnlyList<CommandListReferenceChoice> ReferenceChoices { get; }
    public CommandActionEditorViewModel? SelectedAction
    {
        get => _selectedAction;
        set => SetProperty(ref _selectedAction, value);
    }
    public bool IsEditable { get; }
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }
    public bool HasAction => _command.Actions.Count > 0;
    public bool IsShellCommand => _launchPreference is not null;
    public IReadOnlyList<string> LaunchModes => _launchPreference?.LaunchModes ?? [];
    public string? SelectedLaunchMode
    {
        get => _launchPreference?.SelectedLaunchMode;
        set
        {
            if (_launchPreference is not null && value is not null)
            {
                _launchPreference.SelectedLaunchMode = value;
            }
        }
    }

    public CommandDetailsViewModel(
        CommandDefinition command,
        IReadOnlyList<CommandDefinition> allCommands,
        CommandLaunchPreferenceViewModel? launchPreference,
        string sourceLocation,
        Func<CommandDefinition, bool>? saveUserCommand,
        bool isUserCommand,
        IReadOnlyList<CommandListDefinition> lists,
        string? packId = null,
        Func<string?, CommandDefinition, CommandDefinition, bool>? saveOwnedCommand = null)
    {
        _command = command;
        _originalCommand = command;
        _allCommands = allCommands;
        _name = command.Name;
        _namespace = command.Namespace ?? string.Empty;
        _keyboardShortcut = command.KeyboardShortcut;
        _executeOnKeyboardShortcut = command.ExecuteOnKeyboardShortcut;
        Kind = command.ExecutionHandler;
        SourceLocation = sourceLocation;
        _description = command.Description;
        _saveUserCommand = saveUserCommand;
        _packId = packId;
        _saveOwnedCommand = saveOwnedCommand;
        IsEditable = (isUserCommand || packId is not null)
            && (saveOwnedCommand is not null || (isUserCommand && saveUserCommand is not null));
        ReferenceChoices = [new CommandListReferenceChoice("(none — free text)", null), .. lists
            .Select(list => new CommandListReferenceChoice(
                $"{list.Type}: {list.QualifiedName}",
                $"{list.Type}:{list.QualifiedName}"))];
        foreach (var action in command.Actions)
        {
            Actions.Add(new CommandActionEditorViewModel(action, UpdateAction, RemoveAction, ReferenceChoices, IsEditable,
                (definition, chord) => IsShortcutAvailable(chord, definition, includeCommand: false)));
        }
        SelectedAction = Actions.FirstOrDefault();
        _launchPreference = launchPreference;
    }

    public void BeginShortcutCapture()
    {
        if (IsEditable)
        {
            IsCapturingShortcut = true;
            ShortcutStatus = "Press a modifier chord and key. Esc cancels.";
        }
    }

    public void CancelShortcutCapture()
    {
        IsCapturingShortcut = false;
        ShortcutStatus = "Shortcut capture canceled.";
    }

    public bool CaptureShortcut(string chord)
    {
        IsCapturingShortcut = false;
        return SetShortcut(chord);
    }

    private void UpdateCommandShortcut(string? value)
    {
        var shortcut = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        KeyboardShortcutChord parsed = null!;
        if (shortcut is not null && (!KeyboardShortcutChord.TryParse(shortcut, out parsed)
            || !IsShortcutAvailable(shortcut, null, includeCommand: true, excludeCommandShortcut: _keyboardShortcut)))
        {
            ShortcutStatus = "Enter a valid, unused chord.";
            return;
        }

        if (shortcut is not null)
        {
            shortcut = parsed.ToString();
        }

        _keyboardShortcut = shortcut;
        _command = _command with { KeyboardShortcut = shortcut };
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(KeyboardShortcut));
        ShortcutStatus = string.IsNullOrEmpty(shortcut) ? "Command shortcut cleared." : $"Shortcut: {shortcut}";
    }

    private bool SetShortcut(string chord)
    {
        if (!KeyboardShortcutChord.TryParse(chord, out var parsed)
            || !IsShortcutAvailable(chord, null, includeCommand: true, excludeCommandShortcut: _keyboardShortcut))
        {
            ShortcutStatus = "That chord is invalid or already assigned.";
            return false;
        }

        var normalized = parsed.ToString();
        _keyboardShortcut = normalized;
        _command = _command with { KeyboardShortcut = normalized };
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(KeyboardShortcut));
        ShortcutStatus = $"Shortcut: {normalized}";
        return true;
    }

    private bool IsShortcutAvailable(
        string chord,
        CommandActionDefinition? actionBeingEdited,
        bool includeCommand,
        string? excludeCommandShortcut = null)
    {
        if (!KeyboardShortcutChord.TryParse(chord, out var candidate))
        {
            return false;
        }

        foreach (var command in _allCommands)
        {
            var isCurrent = string.Equals(command.QualifiedName, _originalCommand.QualifiedName, StringComparison.OrdinalIgnoreCase);
            var commandShortcut = isCurrent
                ? includeCommand ? excludeCommandShortcut : _command.KeyboardShortcut
                : command.KeyboardShortcut;
            if (KeyboardShortcutChord.TryParse(commandShortcut, out var otherCommandChord)
                && otherCommandChord == candidate)
            {
                return false;
            }

            foreach (var action in isCurrent ? _command.Actions : command.Actions)
            {
                if (isCurrent && actionBeingEdited is not null
                    && string.Equals(action.Name, actionBeingEdited.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (KeyboardShortcutChord.TryParse(action.KeyboardShortcut, out var otherActionChord)
                    && otherActionChord == candidate)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool SaveChanges()
    {
        if (!IsEditable)
        {
            return false;
        }

        var saved = _saveOwnedCommand is not null
            ? _saveOwnedCommand(_packId, _command with { Name = _name, Namespace = NormalizeNamespace(_namespace) }, _originalCommand)
            : _saveUserCommand?.Invoke(_command) == true;
        if (!saved) return false;

        _command = _command with { Name = _name, Namespace = NormalizeNamespace(_namespace) };
        _originalCommand = _command;
        OnPropertyChanged(nameof(QualifiedName));
        HasUnsavedChanges = false;
        return true;
    }

    [RelayCommand]
    private void Save()
    {
        SaveChanges();
    }

    private void UpdateAction()
    {
        if (!IsEditable)
        {
            return;
        }

        SetCommandActions();
    }

    public void RemoveActionEditor(CommandActionEditorViewModel editor) => RemoveAction(editor);

    [RelayCommand]
    public void AddNewAction()
    {
        if (!IsEditable)
        {
            return;
        }

        var index = 1;
        var name = $"action{index}";
        while (Actions.Any(action => string.Equals(action.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"action{++index}";
        }

        var action = new CommandActionDefinition(name, string.Empty, $"{Name} {name}", "", []);
        var editor = new CommandActionEditorViewModel(action, UpdateAction, RemoveAction, ReferenceChoices, IsEditable,
            (definition, chord) => IsShortcutAvailable(chord, definition, includeCommand: false));
        Actions.Add(editor);
        SelectedAction = editor;
        SetCommandActions();
    }

    private void RemoveAction(CommandActionEditorViewModel editor)
    {
        if (!IsEditable || Actions.Count <= 1 || !Actions.Remove(editor))
        {
            return;
        }

        SelectedAction = Actions.FirstOrDefault();
        SetCommandActions();
    }

    private void SetCommandActions()
    {
        _command = _command with { Actions = Actions.Select(action => action.Definition).ToArray() };
        HasUnsavedChanges = true;
    }

    private void UpdateDescription(string description)
    {
        _command = _command with { Description = description };
        HasUnsavedChanges = true;
    }

    private void UpdateIdentity()
    {
        _command = _command with { Name = _name, Namespace = NormalizeNamespace(_namespace) };
        OnPropertyChanged(nameof(QualifiedName));
        HasUnsavedChanges = true;
    }

    private static string? NormalizeNamespace(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}

public sealed partial class CommandArgumentEditorViewModel : ViewModelBase
{
    private readonly Action<CommandArgumentEditorViewModel, CommandArgumentDefinition> _changed;
    private readonly Action _moveEarlier;
    private readonly Action _moveLater;
    private readonly IReadOnlyList<CommandListReferenceChoice> _referenceChoices;
    private CommandArgumentDefinition _definition;

    public CommandArgumentDefinition Definition => _definition;
    public bool IsEditable { get; }
    public bool CanMoveEarlier { get; private set; }
    public bool CanMoveLater { get; private set; }
    public IReadOnlyList<CommandListReferenceChoice> ReferenceChoices { get; }
    public IReadOnlyList<string> InputTypes { get; } = ["Text", "CSV"];
    public string Name
    {
        get => _definition.Name;
        set => Update(_definition with { Name = value });
    }
    public bool IsOptional
    {
        get => _definition.Optional;
        set => Update(_definition with { Optional = value });
    }
    public CommandListReferenceChoice? SelectedReference
    {
        get => _referenceChoices.FirstOrDefault(choice => string.Equals(choice.Reference, _definition.Ref, StringComparison.OrdinalIgnoreCase))
            ?? (_definition.Ref is null ? _referenceChoices.FirstOrDefault() : new CommandListReferenceChoice(_definition.Ref, _definition.Ref));
        set
        {
            Update(_definition with { Ref = value?.Reference });
            OnPropertyChanged(nameof(HasReference));
            OnPropertyChanged(nameof(IsListReference));
        }
    }

    public bool HasReference => _definition.Ref is not null;
    public bool IsListReference => _definition.Ref?.StartsWith("list:", StringComparison.OrdinalIgnoreCase) == true;
    public string Reference
    {
        get => _definition.Ref ?? string.Empty;
        set
        {
            var reference = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Update(_definition with { Ref = reference });
            OnPropertyChanged(nameof(SelectedReference));
            OnPropertyChanged(nameof(HasReference));
            OnPropertyChanged(nameof(IsListReference));
        }
    }
    public string? ListReference
    {
        get => _definition.Ref?.StartsWith("list:", StringComparison.OrdinalIgnoreCase) == true ? _definition.Ref : null;
        set
        {
            var reference = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (reference is not null && !reference.StartsWith("list:", StringComparison.OrdinalIgnoreCase))
            {
                reference = $"list:{reference}";
            }

            Update(_definition with { Ref = reference });
            OnPropertyChanged(nameof(IsListReference));
        }
    }
    public string InputType
    {
        get => string.Equals(_definition.InputType, "csv", StringComparison.OrdinalIgnoreCase) ? "CSV" : "Text";
        set => Update(_definition with { InputType = string.Equals(value, "CSV", StringComparison.OrdinalIgnoreCase) ? "csv" : null });
    }
    public bool AllowCustomValues
    {
        get => _definition.AllowCustomValues;
        set => Update(_definition with { AllowCustomValues = value });
    }
    public bool IsSingleSelection
    {
        get => _definition.ListMode == ListSelectionMode.Single;
        set => Update(_definition with { ListMode = value ? ListSelectionMode.Single : ListSelectionMode.Multiple });
    }
    public bool IsSensitive
    {
        get => _definition.Sensitive;
        set => Update(_definition with { Sensitive = value });
    }

    public CommandArgumentEditorViewModel(
        CommandArgumentDefinition definition,
        Action<CommandArgumentEditorViewModel, CommandArgumentDefinition> changed,
        IReadOnlyList<CommandListReferenceChoice> referenceChoices,
        bool isEditable,
        Action moveEarlier,
        Action moveLater)
    {
        _definition = definition;
        _changed = changed;
        _moveEarlier = moveEarlier;
        _moveLater = moveLater;
        _referenceChoices = referenceChoices;
        ReferenceChoices = _referenceChoices;
        IsEditable = isEditable;
    }

    [RelayCommand]
    private void MoveEarlier() => _moveEarlier();

    [RelayCommand]
    private void MoveLater() => _moveLater();

    public void UpdateDefinition(CommandArgumentDefinition definition)
    {
        _definition = definition;
        OnPropertyChanged(string.Empty);
    }

    public void SetMoveAvailability(bool canMoveEarlier, bool canMoveLater)
    {
        CanMoveEarlier = canMoveEarlier && IsEditable;
        CanMoveLater = canMoveLater && IsEditable;
        OnPropertyChanged(nameof(CanMoveEarlier));
        OnPropertyChanged(nameof(CanMoveLater));
    }

    private void Update(CommandArgumentDefinition definition)
    {
        if (!IsEditable || _definition == definition)
        {
            return;
        }

        _definition = definition;
        OnPropertyChanged(string.Empty);
        _changed(this, definition);
    }
}

public sealed record CommandListReferenceChoice(string Label, string? Reference);

public sealed partial class CommandActionEditorViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly Action<CommandActionEditorViewModel> _remove;
    private readonly IReadOnlyList<CommandListReferenceChoice> _referenceChoices;
    private readonly bool _isEditable;
    private readonly Func<CommandActionDefinition, string, bool> _shortcutIsAvailable;
    private CommandActionDefinition _definition;
    private bool _isCapturingShortcut;
    private string _shortcutStatus = string.Empty;

    public CommandActionDefinition Definition => _definition;
    public ObservableCollection<CommandArgumentEditorViewModel> Arguments { get; } = [];
    public string Name { get => _definition.Name; set => UpdateAction(_definition with { Name = value }); }
    public string Description { get => _definition.Description; set => UpdateAction(_definition with { Description = value }); }
    public string Usage { get => _definition.Usage; set => UpdateAction(_definition with { Usage = value }); }
    public string TargetTemplate { get => _definition.TargetTemplate; set => UpdateAction(_definition with { TargetTemplate = value }); }
    public string? WorkingDirectoryTemplate { get => _definition.WorkingDirectoryTemplate; set => UpdateAction(_definition with { WorkingDirectoryTemplate = value }); }
    public string ArgumentTemplates
    {
        get => string.Join(Environment.NewLine, _definition.ArgumentTemplates);
        set => UpdateAction(_definition with
        {
            ArgumentTemplates = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        });
    }
    public string? KeyboardShortcut
    {
        get => _definition.KeyboardShortcut;
        set
        {
            var shortcut = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
            if (shortcut is not null && !_shortcutIsAvailable(_definition, shortcut))
            {
                ShortcutStatus = "Enter a valid, unused chord.";
                return;
            }

            UpdateAction(_definition with { KeyboardShortcut = shortcut });
        }
    }
    public bool ExecuteOnKeyboardShortcut
    {
        get => _definition.ExecuteOnKeyboardShortcut;
        set => UpdateAction(_definition with { ExecuteOnKeyboardShortcut = value });
    }
    public bool IsCapturingShortcut
    {
        get => _isCapturingShortcut;
        private set => SetProperty(ref _isCapturingShortcut, value);
    }
    public string ShortcutStatus
    {
        get => _shortcutStatus;
        private set => SetProperty(ref _shortcutStatus, value);
    }
    public bool IsEditable => _isEditable;
    public bool CanRemove => _isEditable;

    public CommandActionEditorViewModel(
        CommandActionDefinition definition,
        Action changed,
        Action<CommandActionEditorViewModel> remove,
        IReadOnlyList<CommandListReferenceChoice> referenceChoices,
        bool isEditable,
        Func<CommandActionDefinition, string, bool>? shortcutIsAvailable = null)
    {
        _definition = definition;
        _changed = changed;
        _remove = editor => remove(editor);
        _referenceChoices = referenceChoices;
        _isEditable = isEditable;
        _shortcutIsAvailable = shortcutIsAvailable ?? ((_, _) => true);
        foreach (var argument in definition.Arguments)
        {
            var editor = CreateArgumentEditor(argument, isEditable);
            editor.SetMoveAvailability(Arguments.Count > 0, Arguments.Count < definition.Arguments.Count - 1);
            Arguments.Add(editor);
        }

        RefreshMoveAvailability();
    }

    public void BeginShortcutCapture()
    {
        if (_isEditable)
        {
            IsCapturingShortcut = true;
            ShortcutStatus = "Press a modifier chord and key. Esc cancels.";
        }
    }

    public void CancelShortcutCapture()
    {
        IsCapturingShortcut = false;
        ShortcutStatus = "Shortcut capture canceled.";
    }

    public bool CaptureShortcut(string chord)
    {
        IsCapturingShortcut = false;
        if (!KeyboardShortcutChord.TryParse(chord, out var parsed) || !_shortcutIsAvailable(_definition, chord))
        {
            ShortcutStatus = "That chord is invalid or already assigned.";
            return false;
        }

        KeyboardShortcut = parsed.ToString();
        ShortcutStatus = $"Shortcut: {parsed}";
        return true;
    }

    public void UpdateDefinition(CommandActionDefinition definition)
    {
        _definition = definition;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void AddArgument()
    {
        if (!_isEditable)
        {
            return;
        }

        var index = 1;
        var name = $"argument{index}";
        while (Arguments.Any(argument => string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"argument{++index}";
        }

        Arguments.Add(CreateArgumentEditor(new CommandArgumentDefinition(name), true));
        RefreshMoveAvailability();
        SetArguments();
    }

    [RelayCommand]
    private void RemoveArgument(CommandArgumentEditorViewModel? argument)
    {
        if (_isEditable && argument is not null && Arguments.Remove(argument))
        {
            RefreshMoveAvailability();
            SetArguments();
        }
    }

    private void UpdateAction(CommandActionDefinition definition)
    {
        if (!_isEditable || _definition == definition)
        {
            return;
        }

        _definition = definition;
        OnPropertyChanged(string.Empty);
        _changed();
    }

    private void UpdateArgument(CommandArgumentEditorViewModel editor, CommandArgumentDefinition definition)
    {
        editor.UpdateDefinition(definition);
        SetArguments();
    }

    private void SetArguments()
    {
        _definition = _definition with { Arguments = Arguments.Select(argument => argument.Definition).ToArray() };
        OnPropertyChanged(nameof(Definition));
        _changed();
    }

    private CommandArgumentEditorViewModel CreateArgumentEditor(CommandArgumentDefinition definition, bool isEditable)
    {
        CommandArgumentEditorViewModel? editor = null;
        editor = new CommandArgumentEditorViewModel(definition, UpdateArgument, _referenceChoices, isEditable,
            () => MoveArgument(editor!, -1),
            () => MoveArgument(editor!, 1));
        return editor;
    }

    private void MoveArgument(CommandArgumentEditorViewModel editor, int offset)
    {
        var index = Arguments.IndexOf(editor);
        var targetIndex = index + offset;
        if (!_isEditable || index < 0 || targetIndex < 0 || targetIndex >= Arguments.Count)
        {
            return;
        }

        Arguments.Move(index, targetIndex);
        RefreshMoveAvailability();
        SetArguments();
    }

    private void RefreshMoveAvailability()
    {
        for (var index = 0; index < Arguments.Count; index++)
        {
            Arguments[index].SetMoveAvailability(index > 0, index < Arguments.Count - 1);
        }
    }

    [RelayCommand]
    public void RemoveSelectedAction()
    {
        if (_isEditable)
        {
            _remove(this);
        }
    }
}

public sealed class ExtensionSecretPermissionViewModel : ViewModelBase
{
    private readonly string _extensionNamespace;
    private readonly Action<string, IReadOnlyList<string>> _save;

    public string ExtensionName { get; }
    public string ExtensionNamespace => _extensionNamespace;
    public IReadOnlyList<SecretPermissionChoiceViewModel> Secrets { get; }

    public ExtensionSecretPermissionViewModel(
        string extensionId,
        string extensionNamespace,
        IReadOnlyList<string> secretNames,
        IReadOnlyList<string> grantedSecretNames,
        Action<string, IReadOnlyList<string>> save)
    {
        ExtensionName = extensionId;
        _extensionNamespace = extensionNamespace;
        _save = save;
        Secrets = secretNames.Select(name => new SecretPermissionChoiceViewModel(
            name,
            grantedSecretNames.Contains(name, StringComparer.OrdinalIgnoreCase),
            OnPermissionChanged)).ToArray();
    }

    private void OnPermissionChanged()
        => _save(_extensionNamespace, Secrets.Where(secret => secret.IsAllowed).Select(secret => secret.Name).ToArray());
}

public sealed class SecretPermissionChoiceViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _isAllowed;

    public string Name { get; }
    public bool IsAllowed
    {
        get => _isAllowed;
        set
        {
            if (SetProperty(ref _isAllowed, value))
            {
                _changed();
            }
        }
    }

    public SecretPermissionChoiceViewModel(string name, bool isAllowed, Action changed)
    {
        Name = name;
        _isAllowed = isAllowed;
        _changed = changed;
    }
}
