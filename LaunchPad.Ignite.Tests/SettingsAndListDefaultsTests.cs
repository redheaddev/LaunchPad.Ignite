using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;
using System.Text.Json.Nodes;

namespace LaunchPad.Ignite.Tests;

public sealed class SettingsAndListDefaultsTests
{
    [Fact]
    public void ProfileSettingsStore_PersistsValuesAndUsesDefinitionDefault()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ProfileSettingsStore(path);
        var setting = new CommandSettingDefinition("orgUrl", "Organization URL", "ADO organization", "https://example.test", Namespace: "ado");

        Assert.Equal("https://example.test", store.GetValue(setting.QualifiedName, setting.DefaultValue));
        store.SetValue(setting.QualifiedName, "https://custom.test");

        var reloaded = new ProfileSettingsStore(path);
        Assert.Equal("https://custom.test", reloaded.GetValue(setting.QualifiedName, setting.DefaultValue));
    }

    [Fact]
    public void ProfileSettingsStore_SerializesThemesUnderSettingsThemesAndKeepsOtherValuesAtRoot()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ProfileSettingsStore(path);
        var theme = ColorThemeService.ToProfileTheme(ColorTheme.CharcoalLimeYellow);
        store.SetValue("app:colorTheme", theme.Id);
        store.SetTheme(theme);

        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var themeNode = Assert.Single(document["settings"]!["themes"]!.AsArray()!);

        Assert.Equal(theme.Id, themeNode!["id"]!.GetValue<string>());
        Assert.Equal(theme.Name, themeNode["name"]!.GetValue<string>());
        Assert.Equal(theme.Colors[nameof(ColorTheme.Accent)], themeNode["colors"]![nameof(ColorTheme.Accent)]!.GetValue<string>());
        Assert.Equal(theme.Id, document["app:colorTheme"]!.GetValue<string>());
        Assert.Null(document[ColorThemeService.CustomThemeNameKey(theme.Id)]);
    }

    [Fact]
    public void ProfileSettingsStore_PersistsImportedThemeReferenceUrl()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ProfileSettingsStore(path);
        var theme = ColorThemeService.ToProfileTheme(ColorTheme.Default with { Id = "imported-source", Name = "Source" })
            with { Url = "https://example.test/palette" };
        store.SetTheme(theme);

        var reloaded = new ProfileSettingsStore(path);

        Assert.True(reloaded.TryGetTheme(theme.Id, out var persisted));
        Assert.Equal(theme.Url, persisted.Url);
        Assert.Equal(theme.Colors, persisted.Colors);
    }

    [Fact]
    public void ThemeReferenceUrl_CanBeAddedToExistingThemeAndCleared()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var theme = ColorThemeService.ToProfileTheme(ColorTheme.CharcoalLimeYellow);
        store.SetTheme(theme);
        var viewModel = CreateSettingsViewModel(store, _ => { });

        viewModel.SelectColorTheme(theme.Id);
        viewModel.ThemeReferenceUrl = "https://example.test/reference";
        Assert.True(viewModel.SaveThemeReferenceUrl());

        Assert.True(store.TryGetTheme(theme.Id, out var updated));
        Assert.Equal("https://example.test/reference", updated.Url);
        Assert.True(viewModel.HasSelectedColorThemeUrl);

        viewModel.ThemeReferenceUrl = string.Empty;
        Assert.True(viewModel.SaveThemeReferenceUrl());
        Assert.True(store.TryGetTheme(theme.Id, out updated));
        Assert.Null(updated.Url);
        Assert.False(viewModel.HasSelectedColorThemeUrl);
    }

    [Fact]
    public void ThemeReferenceUrl_RejectsNonHttpsUrls()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var theme = ColorThemeService.ToProfileTheme(ColorTheme.CharcoalLimeYellow) with
        {
            Url = "https://existing.test/reference"
        };
        store.SetTheme(theme);
        var viewModel = CreateSettingsViewModel(store, _ => { });
        viewModel.SelectColorTheme(theme.Id);
        viewModel.ThemeReferenceUrl = "http://insecure.test";

        Assert.False(viewModel.SaveThemeReferenceUrl());
        Assert.True(store.TryGetTheme(theme.Id, out var persisted));
        Assert.Equal(theme.Url, persisted.Url);
    }

    [Fact]
    public void ProfileSettingsStore_MigratesLegacyThemeKeysIntoThemeNodes()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{\"app:colorTheme\":\"legacy-one\",\"app:customTheme:legacy-one:name\":\"Legacy one\",\"app:theme:legacy-one:Accent\":\"#FF123456\"}");

        var store = new ProfileSettingsStore(path);
        var theme = Assert.Single(store.GetThemes());

        Assert.Equal("Legacy one", theme.Name);
        Assert.Equal("#FF123456", theme.Colors[nameof(ColorTheme.Accent)]);
        Assert.Equal("legacy-one", store.GetValue("app:colorTheme", "default"));
        store.SetValue("ordinary", "kept");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("kept", document["ordinary"]!.GetValue<string>());
        Assert.Equal("#FF123456", document["settings"]!["themes"]![0]!["colors"]![nameof(ColorTheme.Accent)]!.GetValue<string>());
        Assert.Null(document["app:theme:legacy-one:Accent"]);
    }

    [Fact]
    public void ProfileSettingsStore_RemovesLegacyCharcoalMigrationSetting()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{\"app:themes:legacyCharcoalMigrated\":\"true\",\"app:colorTheme\":\"default\"}");

        var store = new ProfileSettingsStore(path);
        ColorThemeService.MigrateLegacyThemes(store);
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        Assert.Null(document["app:themes:legacyCharcoalMigrated"]);
        Assert.NotNull(document["settings"]!["themes"]);
        Assert.DoesNotContain(store.GetThemes(), theme => theme.Id == ColorTheme.CharcoalLimeYellow.Id);
    }

    [Fact]
    public void ThemeColorDrafts_AreNotPersistedOrAppliedUntilSavedAndSurviveThemeSwitch()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var applyCount = 0;
        var viewModel = CreateSettingsViewModel(store, _ => applyCount++);
        viewModel.SelectColorTheme("custom-one");
        var applyCountBeforeDraft = applyCount;
        viewModel.SetThemeColor(nameof(ColorTheme.Accent), "#FF123456");

        Assert.True(viewModel.HasUnsavedThemeColorChanges);
        Assert.False(store.HasValue(ColorThemeService.ColorSettingKey("custom-one", nameof(ColorTheme.Accent))));
        Assert.Equal(applyCountBeforeDraft, applyCount);

        viewModel.SelectColorTheme("custom-two");
        viewModel.SelectColorTheme("custom-one");
        Assert.Contains(
            viewModel.ThemeColors,
            color => color.Property == nameof(ColorTheme.Accent)
                && string.Equals(color.Value, "#FF123456", StringComparison.OrdinalIgnoreCase));

        var applyCountBeforeSave = applyCount;
        Assert.True(viewModel.SaveThemeColorChanges());
        Assert.False(viewModel.HasUnsavedThemeColorChanges);
        Assert.True(store.HasValue(ColorThemeService.ColorSettingKey("custom-one", nameof(ColorTheme.Accent))));
        Assert.Equal(applyCountBeforeSave + 1, applyCount);
        Assert.Equal("#FF123456", ColorThemeService.Resolve("custom-one", store).Accent, ignoreCase: true);
    }

    [Fact]
    public void ThemeColorDrafts_InvalidColorBlocksSavingWithoutWritingAnyDraft()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var viewModel = CreateSettingsViewModel(store, _ => { });
        viewModel.SelectColorTheme("custom-one");
        viewModel.SetThemeColor(nameof(ColorTheme.Accent), "not-a-hex-color");
        viewModel.SetThemeColor(nameof(ColorTheme.Text), "#FF123456");

        Assert.False(viewModel.SaveThemeColorChanges());
        Assert.False(store.HasValue(ColorThemeService.ColorSettingKey("custom-one", nameof(ColorTheme.Accent))));
        Assert.False(store.HasValue(ColorThemeService.ColorSettingKey("custom-one", nameof(ColorTheme.Text))));
        Assert.True(viewModel.HasUnsavedThemeColorChanges);
    }

    [Fact]
    public void SwitchingThemes_RefreshesPickerColorsFromEachThemeAndPreservesDrafts()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        store.SetValue(ColorThemeService.ColorSettingKey("custom-one", nameof(ColorTheme.Accent)), "#FF112233");
        store.SetValue(ColorThemeService.ColorSettingKey("custom-two", nameof(ColorTheme.Accent)), "#FF445566");
        var viewModel = CreateSettingsViewModel(store, _ => { });

        viewModel.SelectColorTheme("custom-one");
        AssertEditorColor(viewModel, "#FF112233");
        viewModel.SetThemeColor(nameof(ColorTheme.Accent), "#FFABCDEF");

        viewModel.SelectColorTheme("custom-two");
        AssertEditorColor(viewModel, "#FF445566");
        viewModel.SelectColorTheme("custom-one");
        AssertEditorColor(viewModel, "#FFABCDEF");
    }

    [Fact]
    public void ImportedTheme_LoadsItsPersistedColorsIntoTheEditor()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var imported = ThemePaletteImporter.CreateTheme(
            ["#FF696C6D", "#FFE2E1DF", "#FF0B354B", "#FF30AAB3", "#FFEAB244", "#FFE64448"],
            ColorTheme.Default) with { Id = "imported-test", Name = "Imported test" };
        var values = ColorTheme.EditableColors.Select(color => new KeyValuePair<string, string>(
            ColorThemeService.ColorSettingKey(imported.Id, color.Property),
            typeof(ColorTheme).GetProperty(color.Property)!.GetValue(imported)!.ToString()!))
            .Append(new KeyValuePair<string, string>(ColorThemeService.CustomThemeNameKey(imported.Id), imported.Name))
            .Append(new KeyValuePair<string, string>("app:colorTheme", imported.Id));
        store.SetValues(values);

        var viewModel = CreateSettingsViewModel(store, _ => { });

        Assert.Equal(imported.Id, viewModel.SelectedColorThemeId);
        AssertEditorColor(viewModel, imported.Accent);
        Assert.DoesNotContain(viewModel.ThemeColors, color => color.Value == "#FFFFFFFF");
    }

    private static void AssertEditorColor(SettingsViewModel viewModel, string expected)
    {
        var editor = Assert.Single(viewModel.ThemeColors, color => color.Property == nameof(ColorTheme.Accent));
        Assert.Equal(expected, editor.Value, ignoreCase: true);
        Assert.Equal(Avalonia.Media.Color.Parse(expected), editor.PickerColor);
        Assert.Equal(Avalonia.Media.Color.Parse(expected), ((Avalonia.Media.SolidColorBrush)editor.PickerBrush).Color);
    }

    [Fact]
    public void ThemePaletteImporter_ReadsSchemeColorPaletteAndMapsColors()
    {
        const string html = "<div id=\"palette-source\" data-palette=\"696c6d-e2e1df-0b354b-30aab3-eab244-e64448\"></div>";

        var colors = ThemePaletteImporter.ParseColors(html);

        Assert.Equal(["#FF696C6D", "#FFE2E1DF", "#FF0B354B", "#FF30AAB3", "#FFEAB244", "#FFE64448"], colors);
    }

    [Fact]
    public void ThemePaletteImporter_UsesDarkSurfacesAndReadableTextForImportedPalettes()
    {
        var theme = ThemePaletteImporter.CreateTheme(
            ["#FF696C6D", "#FFE2E1DF", "#FF0B354B", "#FF30AAB3", "#FFEAB244", "#FFE64448"],
            ColorTheme.Default);

        Assert.Equal("#FF0B354B", theme.WindowBackground);
        Assert.True(Avalonia.Media.Color.Parse(theme.PanelBackground).R < Avalonia.Media.Color.Parse(theme.Text).R);
        Assert.Equal("#FFE2E1DF", theme.Text);
        Assert.Equal("#FF30AAB3", theme.Accent);
    }

    [Fact]
    public void ImportedTheme_IsAddedSeparatelyAndDoesNotChangeBuiltInPalette()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var builtIn = ColorTheme.Default;
        var imported = ThemePaletteImporter.CreateTheme(
            ["#FF696C6D", "#FFE2E1DF", "#FF0B354B", "#FF30AAB3", "#FFEAB244", "#FFE64448"],
            builtIn) with { Id = "imported-test", Name = "Imported test" };

        store.SetValue(ColorThemeService.CustomThemeNameKey(imported.Id), imported.Name);
        foreach (var color in ColorTheme.EditableColors)
        {
            var value = imported.WithColor(color.Property, "#FF123456");
            var colorValue = color.Property switch
            {
                nameof(ColorTheme.WindowBackground) => value.WindowBackground,
                nameof(ColorTheme.PanelBackground) => value.PanelBackground,
                nameof(ColorTheme.InputBackground) => value.InputBackground,
                nameof(ColorTheme.Border) => value.Border,
                nameof(ColorTheme.Text) => value.Text,
                nameof(ColorTheme.SecondaryText) => value.SecondaryText,
                nameof(ColorTheme.MutedText) => value.MutedText,
                nameof(ColorTheme.Accent) => value.Accent,
                nameof(ColorTheme.Highlight) => value.Highlight,
                nameof(ColorTheme.TextSelection) => value.TextSelection,
                nameof(ColorTheme.CommandFocus) => value.CommandFocus,
                nameof(ColorTheme.NavigationSelected) => value.NavigationSelected,
                nameof(ColorTheme.Target) => value.Target,
                nameof(ColorTheme.RequiredArgument) => value.RequiredArgument,
                nameof(ColorTheme.OptionalArgument) => value.OptionalArgument,
                nameof(ColorTheme.Checked) => value.Checked,
                nameof(ColorTheme.Status) => value.Status,
                _ => throw new InvalidOperationException()
            };
            store.SetValue(ColorThemeService.ColorSettingKey(imported.Id, color.Property), colorValue);
        }

        var themes = ColorThemeService.LoadThemes(store);

        Assert.Contains(themes, theme => theme.Id == imported.Id && theme.Name == imported.Name);
        Assert.Equal(builtIn, ColorTheme.Default);
        Assert.Equal(builtIn.WindowBackground, ColorThemeService.Resolve("default", store).WindowBackground);
    }

    [Fact]
    public void DefaultTheme_UsesPinkForCommandFocusAndTextSelectionOnly()
    {
        Assert.Equal("#FFFF008C", ColorTheme.Default.CommandFocus);
        Assert.Equal("#FFFF008C", ColorTheme.Default.TextSelection);
        Assert.Equal("#FF1E3A5F", ColorTheme.Default.Highlight);
    }

    [Fact]
    public void InvalidThemeColorOverride_IsRemovedWithoutSavingFallbackColor()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var key = ColorThemeService.ColorSettingKey(ColorTheme.Default.Id, nameof(ColorTheme.Text));
        store.SetValue(key, "White");

        var loaded = ColorThemeService.Resolve(ColorTheme.Default.Id, store);

        Assert.Equal(ColorTheme.Default.Text, loaded.Text);
        Assert.False(store.HasValue(key));
        Assert.False(new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")).HasValue(key));
    }

    [Fact]
    public void LegacyCharcoalTheme_IsMigratedIntoProfileBeforeThemeResolution()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));

        ColorThemeService.MigrateLegacyThemes(store);
        var theme = ColorThemeService.Resolve(ColorTheme.CharcoalLimeYellow.Id, store);

        Assert.Equal(ColorTheme.CharcoalLimeYellow.Name, theme.Name);
        Assert.Equal(ColorTheme.CharcoalLimeYellow.Accent, theme.Accent, ignoreCase: true);
        Assert.True(store.HasValue(ColorThemeService.ColorSettingKey(theme.Id, nameof(ColorTheme.Accent))));
    }

    [Fact]
    public void DeleteTheme_RemovesNameAndAllColorsButKeepsDefault()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ProfileSettingsStore(path);
        const string themeId = "custom-remove-me";
        store.SetValue(ColorThemeService.CustomThemeNameKey(themeId), "Remove me");
        store.SetValue(ColorThemeService.ColorSettingKey(themeId, nameof(ColorTheme.Accent)), "#FF123456");

        Assert.True(ColorThemeService.DeleteTheme(themeId, store));

        var reloaded = new ProfileSettingsStore(path);
        Assert.False(reloaded.HasValue(ColorThemeService.CustomThemeNameKey(themeId)));
        Assert.False(reloaded.HasValue(ColorThemeService.ColorSettingKey(themeId, nameof(ColorTheme.Accent))));
        Assert.DoesNotContain(ColorThemeService.LoadThemes(reloaded), theme => theme.Id == themeId);
        Assert.False(ColorThemeService.DeleteTheme(ColorTheme.Default.Id, reloaded));
        Assert.Contains(ColorThemeService.LoadThemes(reloaded), theme => theme.Id == ColorTheme.Default.Id);
    }

    [Fact]
    public void RenameTheme_PersistsNameAndPreservesPaletteColors()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ProfileSettingsStore(path);
        const string themeId = "custom-rename-me";
        store.SetValue(ColorThemeService.CustomThemeNameKey(themeId), "Old name");
        store.SetValue(ColorThemeService.ColorSettingKey(themeId, nameof(ColorTheme.Accent)), "#FF123456");

        Assert.True(ColorThemeService.RenameTheme(themeId, "  Citrus  ", store));

        var renamed = Assert.Single(ColorThemeService.LoadThemes(new ProfileSettingsStore(path)), theme => theme.Id == themeId);
        Assert.Equal("Citrus", renamed.Name);
        Assert.Equal("#FF123456", renamed.Accent, ignoreCase: true);
    }

    [Theory]
    [InlineData("default", "Cannot rename")]
    [InlineData("custom-rename", " ")]
    [InlineData("missing-theme", "New name")]
    public void RenameTheme_RejectsDefaultBlankNamesAndUnknownThemes(string themeId, string name)
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        store.SetValue(ColorThemeService.CustomThemeNameKey("custom-rename"), "Original");

        Assert.False(ColorThemeService.RenameTheme(themeId, name, store));
    }

    [Fact]
    public void DeletedMigratedCharcoalTheme_IsNotRecreatedByMigration()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ProfileSettingsStore(path);
        ColorThemeService.MigrateLegacyThemes(store);

        Assert.True(ColorThemeService.DeleteTheme(ColorTheme.CharcoalLimeYellow.Id, store));
        ColorThemeService.MigrateLegacyThemes(new ProfileSettingsStore(path));

        Assert.DoesNotContain(
            ColorThemeService.LoadThemes(new ProfileSettingsStore(path)),
            theme => theme.Id == ColorTheme.CharcoalLimeYellow.Id);
    }

    [Theory]
    [InlineData("#123456", true)]
    [InlineData("#FF123456", true)]
    [InlineData("White", false)]
    [InlineData("red", false)]
    public void ThemeColorValidation_OnlyAcceptsHexColors(string value, bool expected)
        => Assert.Equal(expected, ColorThemeService.IsValidHexColor(value));

    [Fact]
    public void EnumDefault_IsFirstSuggestionWithoutRemovingOtherValues()
    {
        var schema = new CommandSchema(
            [new CommandListDefinition("enum", "project", ["alpha", "architecture", "beta"], "architecture")],
            []);

        var values = new CommandSchemaValueProvider(schema).GetValues("enum:project");

        Assert.Equal(["architecture", "alpha", "beta"], values);
    }

    [Fact]
    public void EnumDefault_DoesNotFillInTheRequiredArgument()
    {
        var schema = new CommandSchema(
            [new CommandListDefinition("enum", "project", ["alpha", "architecture", "beta"], "architecture")],
            [new CommandDefinition("tool", "url", "", [new CommandActionDefinition(
                "default", "", "tool <enum:project>", "https://example.test", [new CommandArgumentDefinition("project", Ref: "enum:project")])])]);
        var parser = new LaunchPad.Ignite.Core.Parsing.CommandLineParser(
            new LaunchPad.Ignite.Core.Schema.CommandCatalog(schema),
            new CommandSchemaValueProvider(schema));

        var result = parser.Parse("tool");

        Assert.Equal(LaunchPad.Ignite.Core.Parsing.CommandParseStatus.Incomplete, result.Status);
    }

    [Fact]
    public void ApplicationSettingsLoader_ReadsJsonDefinitions()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "app-settings.json");
        File.WriteAllText(path, "{\"settings\":[{\"name\":\"home\",\"label\":\"Home\",\"description\":\"Home directory\",\"defaultValue\":\"C:\\\\Users\",\"type\":\"directory\"}]}");

        var settings = new ApplicationSettingsLoader(path).Load();

        Assert.Single(settings);
        Assert.Equal("home", settings[0].QualifiedName);
        Assert.Equal("directory", settings[0].Type);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static SettingsViewModel CreateSettingsViewModel(ProfileSettingsStore store, Action<ColorTheme> themeChanged)
    {
        foreach (var id in new[] { "custom-one", "custom-two" })
        {
            store.SetValue(ColorThemeService.CustomThemeNameKey(id), id);
        }

        var extensions = new LoadedExtensions(
            new CommandSchema([], []),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        return new SettingsViewModel(
            [],
            extensions,
            store,
            new ExtensionStateStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            () => { },
            themeChanged);
    }
}
