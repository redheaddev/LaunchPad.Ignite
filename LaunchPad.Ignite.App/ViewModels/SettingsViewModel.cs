using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services;
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
    private readonly Dictionary<string, Dictionary<string, string>> _themeColorDrafts = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ColorTheme> _colorThemes = [];
    private IReadOnlyList<ThemeColorEditorViewModel> _themeColors = [];
    private string _newThemeName = string.Empty;
    private string _renameThemeName = string.Empty;
    private string _themeReferenceUrl = string.Empty;

    private SettingsNavigationItem? _selectedNavigationItem;
    public IReadOnlyList<SettingsNavigationItem> NavigationItems { get; }
    public IReadOnlyList<SettingEditorViewModel> Settings { get; private set; } = [];
    public IReadOnlyList<ExtensionSecretPermissionViewModel> ExtensionSecretPermissions => _extensionSecretPermissions;
    public string Header { get; private set; } = "General";
    public string Subheader { get; private set; } = string.Empty;
    public bool IsExtensionEnabled { get; private set; } = true;
    public bool IsExtensionSelected => SelectedNavigationItem?.Category == "extension";
    public bool IsThemeSelected => SelectedNavigationItem?.Category == "theme";
    public bool IsSettingsListVisible => !IsThemeSelected && !IsSecretsSelected;
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
        IReadOnlyList<string>? availableSecretNames = null)
    {
        _settingsStore = settingsStore;
        _settingsChanged = settingsChanged;
        _applicationSettings = applicationSettings;
        _extensionStateStore = extensionStateStore;
        _themeChanged = themeChanged;
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
            new("Secrets", "secrets", ""),
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
                AvailableSettings = settingsForExtension ?? []
            });
        }

        NavigationItems = navigation;
        SelectedNavigationItem = navigation.FirstOrDefault(item => item.Category == "application");
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
        if (value.Category == "extension")
        {
            var settingDefinitions = value.AvailableSettings;
            Subheader = $"{settingDefinitions.Count} setting(s) available. Token prefix: {value.Id}";
            IsExtensionEnabled = _extensionStateStore.IsEnabled(value.Id);
            Settings = settingDefinitions.Select(CreateEditor).ToArray();
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
        else if (value.Category == "secrets")
        {
            Subheader = "Grant each extension access only to the secrets it needs. Unchecked secrets are denied.";
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
        OnPropertyChanged(nameof(IsThemeSelected));
        OnPropertyChanged(nameof(IsSettingsListVisible));
        OnPropertyChanged(nameof(IsSecretsSelected));
        OnPropertyChanged(nameof(ExtensionSecretPermissions));
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

    public bool IsSecretsSelected => SelectedNavigationItem?.Category == "secrets";

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

    public sealed class SettingsNavigationItem(string name, string category, string id)
    {
        public string Name { get; } = name;
        public string Category { get; } = category;
        public string Id { get; } = id;
        public Thickness Indent => Category == "extension" ? new Thickness(18, 0, 0, 0) : default;
        public bool IsEnabled { get; init; }
        public IReadOnlyList<CommandSettingDefinition> AvailableSettings { get; init; } = [];
        public CommandSettingDefinition? SettingDefinition { get; init; }
    }
}

public sealed class ExtensionSecretPermissionViewModel : ViewModelBase
{
    private readonly string _extensionNamespace;
    private readonly Action<string, IReadOnlyList<string>> _save;

    public string ExtensionName { get; }
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
