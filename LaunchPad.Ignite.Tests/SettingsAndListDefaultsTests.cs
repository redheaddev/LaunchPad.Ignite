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
    public void ListDefaultOverrideStore_PersistsAndAppliesOnlyTargetedListDefault()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var extensionList = new CommandListDefinition("enum", "project", ["architecture", "claims"], "architecture");
        var unrelatedList = new CommandListDefinition("enum", "environment", ["dev", "prod"], "dev");
        var overrides = new ListDefaultOverrideStore(store);

        overrides.SetDefault(extensionList, "claims");
        var reloaded = new ListDefaultOverrideStore(new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")));
        var applied = reloaded.ApplyOverrides([extensionList, unrelatedList]);

        Assert.Equal("claims", applied[0].Default);
        Assert.Equal("dev", applied[1].Default);
        reloaded.SetDefault(extensionList, null);
        Assert.Equal("architecture", new ListDefaultOverrideStore(
            new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"))).ApplyOverrides([extensionList])[0].Default);
    }

    [Fact]
    public void ListDefaultOverrideStore_RejectsValuesNotInExtensionList()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var list = new CommandListDefinition("enum", "project", ["architecture", "claims"], "architecture");

        Assert.Throws<ArgumentException>(() => new ListDefaultOverrideStore(store).SetDefault(list, "unknown"));
    }

    [Fact]
    public void ListDefaultEditor_UsesExtensionDefaultOptionToClearOnlyThatListsOverride()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var overrides = new ListDefaultOverrideStore(store);
        var firstList = new CommandListDefinition("enum", "project", ["architecture", "claims"], "architecture", "ado");
        var secondList = new CommandListDefinition("enum", "environment", ["dev", "prod"], "dev", "ado");
        overrides.SetDefault(firstList, "claims");

        var editor = new ListDefaultEditorViewModel(firstList, overrides);

        Assert.Equal("claims", editor.SelectedDefault);
        editor.SelectedDefault = ListDefaultEditorViewModel.ExtensionDefaultOption;

        Assert.Null(overrides.GetDefaultOverride(firstList));
        Assert.Null(overrides.GetDefaultOverride(secondList));
        Assert.Contains(ListDefaultEditorViewModel.ExtensionDefaultOption, editor.Values);
    }

    [Fact]
    public void ListOverrideStore_PersistsEnumValuesAndOrdersDefaultFirstThenAlphabetically()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var list = new CommandListDefinition("enum", "project", ["original"], "original", "ado");
        var store = new ProfileSettingsStore(path);

        new ListDefaultOverrideStore(store).SetList(list, ["zebra", "claims", "architecture"], "claims");
        var reloaded = new ListDefaultOverrideStore(new ProfileSettingsStore(path));
        var applied = Assert.Single(reloaded.ApplyOverrides([list]));

        Assert.Equal(["claims", "architecture", "zebra"], applied.Values);
        Assert.Equal("claims", applied.Default);
    }

    [Fact]
    public void EnumListEditor_SavePersistsValuesAndSelectedDefault()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var list = new CommandListDefinition("enum", "project", ["architecture", "claims"], "architecture", "ado");
        var editor = new EnumListEditorViewModel(list, new ListDefaultOverrideStore(store), () => { });
        editor.ValuesText = "zebra\nclaims\narchitecture";
        editor.SelectedDefault = "claims";

        Assert.True(editor.SaveChanges());

        var applied = Assert.Single(new ListDefaultOverrideStore(store).ApplyOverrides([list]));
        Assert.Equal(["claims", "architecture", "zebra"], applied.Values);
        Assert.Equal("claims", applied.Default);
        Assert.Equal("List saved.", editor.Status);
    }

    [Fact]
    public void EnumListEditor_RejectsDefaultRemovedFromValues()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var list = new CommandListDefinition("enum", "project", ["architecture", "claims"], "architecture", "ado");
        var editor = new EnumListEditorViewModel(list, new ListDefaultOverrideStore(store), () => { });
        editor.ValuesText = "claims";

        Assert.False(editor.SaveChanges());

        Assert.False(store.HasValue(ListDefaultOverrideStore.GetListOverrideSettingName(list)));
        Assert.Contains("select a default", editor.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandsSettings_ExposesExtensionListDefaultsAndShellLaunchPreferences()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var shellCommand = CreateShellCommand("editor");
        var urlCommand = CreateShellCommand("docs", "url");
        var extensions = new LoadedExtensions(
            new CommandSchema(
                [new CommandListDefinition("enum", "project", ["architecture", "claims"], "architecture", "ado")],
                [shellCommand, urlCommand]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            [new LoadedExtensionInfo("ado", "1.0", true) { Namespace = "ado" }]);
        var viewModel = new SettingsViewModel(
            [], extensions, store,
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { }, commands: [shellCommand, urlCommand],
            lists: extensions.Schema.Lists);

        viewModel.SelectedNavigationItem = viewModel.NavigationItems.Single(item => item.Category == "extension");
        Assert.True(viewModel.HasSelectedExtensionSecretPermissions);
        var listEditor = Assert.Single(viewModel.ListDefaultEditors);
        Assert.Equal("enum:ado:project", listEditor.ListName);
        Assert.Equal(ListDefaultEditorViewModel.ExtensionDefaultOption, listEditor.SelectedDefault);
        listEditor.SelectedDefault = "claims";
        Assert.Equal("claims", store.GetValue("app:listDefault:enum:ado:project", ""));
        viewModel.SelectedNavigationItem = viewModel.NavigationItems.Single(item => item.Category == "commands");
        Assert.False(viewModel.HasSelectedExtensionSecretPermissions);
        Assert.Single(viewModel.CommandLaunchPreferences);
        Assert.Empty(viewModel.ListDefaultEditors);
        viewModel.SelectedNavigationItem = viewModel.NavigationItems.Single(item => item.Category == "lists");
        Assert.True(viewModel.IsListsSelected);
        Assert.Single(viewModel.EnumListEditors);
        Assert.Equal("enum:ado:project", viewModel.SelectedEnumListEditor?.ListName);
    }

    [Fact]
    public void ExtensionLoader_AssignsNamespaceToExtensionListDefinitions()
    {
        using var directory = new TemporaryDirectory();
        var extensionRoot = Path.Combine(directory.Path, "extensions");
        var extensionDirectory = Path.Combine(extensionRoot, "sample");
        Directory.CreateDirectory(extensionDirectory);
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), """
            {
              "id": "sample",
              "name": "Sample",
              "namespace": "sample",
              "version": "1.0",
              "schema": {
                "lists": [{ "type": "enum", "name": "mode", "values": ["one", "two"], "default": "one" }],
                "commands": [], "variables": [], "functions": [], "settings": []
              }
            }
            """);

        var result = new ExtensionLoader([extensionRoot]).Load();

        var list = Assert.Single(result.Schema.Lists);
        Assert.Equal("sample:mode", list.QualifiedName);
        Assert.Equal("sample", list.Namespace);
    }

    [Fact]
    public void CommandsSettings_ShowsSelectedCommandIdentityAndActionMetadata()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var command = new CommandDefinition("pipeline", "shell", "Runs pipeline", [
            new CommandActionDefinition("default", "Starts a pipeline", "pipeline <project>", "pwsh.exe", [
                new CommandArgumentDefinition("project", Ref: "enum:ado:project")
            ])
        ], "ado");
        var extensions = new LoadedExtensions(
            new CommandSchema([], [command]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        var viewModel = new SettingsViewModel(
            [], extensions, store,
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { }, commands: [command]);

        var details = Assert.Single(viewModel.CommandDetails);
        Assert.Equal("pipeline", details.Name);
        Assert.Equal("ado", details.Namespace);
        Assert.Equal("ado:pipeline", details.QualifiedName);
        Assert.Equal("shell", details.Kind);
        var action = Assert.Single(details.Actions);
        Assert.Equal("default", action.Name);
        Assert.Equal("pipeline <project>", action.Usage);
        Assert.Equal("pwsh.exe", action.TargetTemplate);
        Assert.Equal("project", Assert.Single(action.Arguments).Name);
    }

    [Fact]
    public void CommandsSettings_FiltersCommandsBySourceAndDefaultsToUser()
    {
        using var directory = new TemporaryDirectory();
        var userCommand = CreateShellCommand("user-command");
        var packCommand = CreateShellCommand("pack-command") with { Namespace = "pack" };
        var extensionCommand = CreateShellCommand("extension-command") with { Namespace = "extension" };
        var appCommand = CreateShellCommand("app-command");
        var commands = new[] { userCommand, packCommand, extensionCommand, appCommand };
        var extensions = new LoadedExtensions(
            new CommandSchema([], commands),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        var viewModel = new SettingsViewModel(
            [], extensions, new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { },
            commands: commands,
            commandSources: new Dictionary<string, string> { [extensionCommand.QualifiedName] = "ext" },
            userCommandNames: new HashSet<string>([userCommand.QualifiedName], StringComparer.OrdinalIgnoreCase),
            commandPackSources: new Dictionary<string, string> { [packCommand.QualifiedName] = "pack-id" });

        Assert.Equal("User", viewModel.SelectedCommandSourceFilter);
        Assert.Equal(2, viewModel.FilteredCommandDetails.Count);
        Assert.Contains(viewModel.FilteredCommandDetails, details => details.QualifiedName == "user-command");
        Assert.Contains(viewModel.FilteredCommandDetails, details => details.QualifiedName == "pack:pack-command");
        Assert.Contains(viewModel.FilteredCommandDetails, details => details == viewModel.SelectedCommandDetails);

        viewModel.SelectedCommandSourceFilter = "Extension";
        Assert.Equal("extension:extension-command", Assert.Single(viewModel.FilteredCommandDetails).QualifiedName);
        viewModel.SelectedCommandSourceFilter = "App";
        Assert.Equal("app-command", Assert.Single(viewModel.FilteredCommandDetails).QualifiedName);
        viewModel.SelectedCommandSourceFilter = "All";
        Assert.Equal(4, viewModel.FilteredCommandDetails.Count);
    }

    [Fact]
    public void OpenCommandEditor_SelectsRequestedCommandAndItsSourceFilter()
    {
        using var directory = new TemporaryDirectory();
        var userCommand = CreateShellCommand("user-command");
        var extensionCommand = CreateShellCommand("extension-command") with { Namespace = "extension" };
        var appCommand = CreateShellCommand("app-command");
        var commands = new[] { userCommand, extensionCommand, appCommand };
        var extensions = new LoadedExtensions(
            new CommandSchema([], commands),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        var viewModel = new SettingsViewModel(
            [], extensions, new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { },
            commands: commands,
            commandSources: new Dictionary<string, string> { [extensionCommand.QualifiedName] = "ext" },
            userCommandNames: new HashSet<string>([userCommand.QualifiedName], StringComparer.OrdinalIgnoreCase));

        viewModel.OpenCommandEditor("EXTENSION:extension-command");

        Assert.True(viewModel.IsCommandsSelected);
        Assert.Equal("Extension", viewModel.SelectedCommandSourceFilter);
        Assert.Equal("extension:extension-command", viewModel.SelectedCommandDetails?.QualifiedName);

        viewModel.OpenCommandEditor("app-command");

        Assert.Equal("App", viewModel.SelectedCommandSourceFilter);
        Assert.Equal("app-command", viewModel.SelectedCommandDetails?.QualifiedName);
    }

    [Fact]
    public void OpenCommandEditorWithoutNameOpensCommandsAndKeepsDefaultUserFilter()
    {
        var viewModel = CreateSettingsViewModel(new ProfileSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), _ => { });

        viewModel.OpenCommandEditor(null);

        Assert.True(viewModel.IsCommandsSelected);
        Assert.Equal("User", viewModel.SelectedCommandSourceFilter);
    }

    [Theory]
    [InlineData(false, false, "App")]
    [InlineData(false, true, "User")]
    [InlineData(true, true, "User")]
    public void CommandSourceLabels_IdentifiesCommandSource(
        bool fromExtensionOrPack,
        bool fromUserSchema,
        string expected)
    {
        var command = CreateShellCommand("pipeline", "shell") with { Namespace = "ado" };
        IReadOnlyDictionary<string, string> sources = fromExtensionOrPack
            ? new Dictionary<string, string> { [command.QualifiedName] = "ado" }
            : new Dictionary<string, string>();
        IReadOnlySet<string> userCommands = fromUserSchema
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { command.QualifiedName }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected, CommandSourceLabels.GetLabel(command, sources, userCommands));
    }

    [Theory]
    [InlineData(false, "Extension [ado]")]
    [InlineData(true, "Pack [ado]")]
    public void CommandSourceLabels_DistinguishesExtensionFromPack(bool fromPack, string expected)
    {
        var command = CreateShellCommand("pipeline", "shell") with { Namespace = "ado" };
        IReadOnlyDictionary<string, string> sources = fromPack
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [command.QualifiedName] = "ado" };
        IReadOnlyDictionary<string, string> packSources = fromPack
            ? new Dictionary<string, string> { [command.QualifiedName] = "ado" }
            : new Dictionary<string, string>();

        Assert.Equal(expected, CommandSourceLabels.GetLabel(command, sources, new HashSet<string>(), packSources));
    }

    [Fact]
    public void CommandDetails_OnlyUserCommandsCanEditAndSave()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var userCommand = CreateShellCommand("user-command") with { Description = "before" };
        var appCommand = CreateShellCommand("app-command") with { Description = "app definition" };
        var extensions = new LoadedExtensions(
            new CommandSchema([], [userCommand, appCommand]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        CommandDefinition? saved = null;
        var viewModel = new SettingsViewModel(
            [], extensions, store,
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { },
            commands: [userCommand, appCommand],
            userCommandNames: new HashSet<string>([userCommand.QualifiedName], StringComparer.OrdinalIgnoreCase),
            saveUserCommand: command =>
            {
                saved = command;
                return true;
            });

        var userDetails = viewModel.CommandDetails.Single(details => details.QualifiedName == "user-command");
        var appDetails = viewModel.CommandDetails.Single(details => details.QualifiedName == "app-command");
        Assert.True(userDetails.IsEditable);
        Assert.False(appDetails.IsEditable);
        Assert.All(appDetails.Actions, action => Assert.False(action.IsEditable));
        userDetails.Description = "after";
        Assert.True(userDetails.HasUnsavedChanges);
        userDetails.SaveChanges();

        Assert.Equal("after", saved?.Description);
        Assert.False(userDetails.HasUnsavedChanges);
        userDetails.Name = "renamed";
        userDetails.Namespace = "custom";
        Assert.Equal("custom:renamed", userDetails.QualifiedName);
        Assert.True(userDetails.HasUnsavedChanges);
        userDetails.SaveChanges();
        Assert.Equal("renamed", saved?.Name);
        Assert.Equal("custom", saved?.Namespace);
        appDetails.Description = "attempted edit";
        Assert.Equal("app definition", appDetails.Description);
        appDetails.Name = "attempted-rename";
        Assert.Equal("app-command", appDetails.Name);
    }

    [Fact]
    public void CommandDetails_PackCommandsCanEditIdentityAndDescription()
    {
        using var directory = new TemporaryDirectory();
        var command = CreateShellCommand("status", "shell") with { Namespace = "tools", Description = "Before" };
        var extensions = new LoadedExtensions(
            new CommandSchema([], [command]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        CommandDefinition? saved = null;
        CommandDefinition? original = null;
        string? savedPackId = null;
        var viewModel = new SettingsViewModel(
            [], extensions, new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { },
            commands: [command],
            commandPackSources: new Dictionary<string, string> { [command.QualifiedName] = "tools-pack" },
            saveOwnedCommand: (packId, updated, existing) =>
            {
                savedPackId = packId;
                saved = updated;
                original = existing;
                return true;
            });

        var details = Assert.Single(viewModel.CommandDetails);
        Assert.True(details.IsEditable);
        details.Name = "health";
        details.Namespace = "infra";
        details.Description = "After";
        Assert.Equal("infra:health", details.QualifiedName);
        Assert.True(details.SaveChanges());

        Assert.Equal("tools-pack", savedPackId);
        Assert.Equal("tools:status", original?.QualifiedName);
        Assert.Equal("infra:health", saved?.QualifiedName);
        Assert.Equal("After", saved?.Description);
    }

    [Fact]
    public void CommandDetails_UserCanEditAndSaveFirstActionArguments()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var command = new CommandDefinition("user-command", "shell", "User command", [
            new CommandActionDefinition("default", "Runs", "user-command <project>", "pwsh.exe", [
                new CommandArgumentDefinition("project", Ref: "enum:project")
            ])
        ]);
        var extensions = new LoadedExtensions(
            new CommandSchema([new CommandListDefinition("enum", "project", ["one", "two"])], [command]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        CommandDefinition? saved = null;
        var viewModel = new SettingsViewModel(
            [], extensions, store,
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { },
            commands: [command],
            userCommandNames: new HashSet<string>([command.QualifiedName], StringComparer.OrdinalIgnoreCase),
            saveUserCommand: updated =>
            {
                saved = updated;
                return true;
            });

        var details = Assert.Single(viewModel.CommandDetails);
        var firstAction = Assert.Single(details.Actions);
        var argument = Assert.Single(firstAction.Arguments);
        argument.Name = "projects";
        argument.InputType = "CSV";
        argument.IsOptional = true;
        argument.Reference = "list:projects";
        argument.AllowCustomValues = true;
        argument.IsSingleSelection = true;
        argument.IsSensitive = true;
        Assert.True(details.HasUnsavedChanges);
        details.SaveChanges();

        var savedArgument = Assert.Single(Assert.Single(saved!.Actions).Arguments);
        Assert.Equal("projects", savedArgument.Name);
        Assert.True(savedArgument.Optional);
        Assert.Equal("csv", savedArgument.InputType);
        Assert.Equal("list:projects", savedArgument.Ref);
        Assert.True(savedArgument.AllowCustomValues);
        Assert.Equal(ListSelectionMode.Single, savedArgument.ListMode);
        Assert.True(savedArgument.Sensitive);
        Assert.False(details.HasUnsavedChanges);
    }

    [Fact]
    public void CommandDetails_NonUserCommandArgumentsRemainReadOnly()
    {
        using var directory = new TemporaryDirectory();
        var command = new CommandDefinition("app-command", "shell", "App command", [
            new CommandActionDefinition("default", "Runs", "app-command <project>", "pwsh.exe", [
                new CommandArgumentDefinition("project")
            ])
        ]);
        var extensions = new LoadedExtensions(
            new CommandSchema([], [command]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        var viewModel = new SettingsViewModel(
            [], extensions, new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { }, commands: [command]);

        var details = Assert.Single(viewModel.CommandDetails);
        var argument = Assert.Single(Assert.Single(details.Actions).Arguments);
        argument.Name = "changed";

        Assert.Equal("project", argument.Name);
        Assert.False(details.HasUnsavedChanges);
    }

    [Fact]
    public void CommandDetails_SelectsAndEditsMultipleActionsAndCanAddOrRemoveActions()
    {
        using var directory = new TemporaryDirectory();
        var command = new CommandDefinition("user-command", "shell", "User command", [
            new CommandActionDefinition("default", "Default action", "user-command", "cmd.exe", []),
            new CommandActionDefinition("open", "Open item", "user-command open <path>", "explorer.exe", [
                new CommandArgumentDefinition("path"),
                new CommandArgumentDefinition("mode")
            ])
        ]);
        var extensions = new LoadedExtensions(
            new CommandSchema([], [command]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(), []);
        CommandDefinition? saved = null;
        var viewModel = new SettingsViewModel(
            [], extensions, new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { }, commands: [command],
            userCommandNames: new HashSet<string>([command.QualifiedName], StringComparer.OrdinalIgnoreCase),
            saveUserCommand: updated => { saved = updated; return true; });

        var details = Assert.Single(viewModel.CommandDetails);
        var secondAction = details.Actions[1];
        details.SelectedAction = secondAction;
        secondAction.Arguments[1].MoveEarlierCommand.Execute(null);
        secondAction.Description = "Updated action";
        secondAction.TargetTemplate = "new-target.exe";
        Assert.True(details.HasUnsavedChanges);
        details.SaveChanges();

        Assert.Equal("Updated action", saved!.Actions[1].Description);
        Assert.Equal("new-target.exe", saved.Actions[1].TargetTemplate);
        Assert.Equal(["mode", "path"], saved.Actions[1].Arguments.Select(argument => argument.Name));

        details.AddNewAction();
        Assert.Equal(3, details.Actions.Count);
        Assert.Equal(details.Actions[2], details.SelectedAction);
        details.SelectedAction!.RemoveSelectedAction();
        Assert.Equal(2, details.Actions.Count);
        details.SaveChanges();
        Assert.DoesNotContain(saved.Actions, action => action.Name == "action1");
        Assert.Equal(2, saved.Actions.Count);
    }

    [Fact]
    public void CommandArgumentEditor_OffersEnumReferencesAndSupportsDynamicListReferences()
    {
        using var directory = new TemporaryDirectory();
        var command = new CommandDefinition("user-command", "shell", "User command", [
            new CommandActionDefinition("default", "Runs", "user-command", "cmd.exe", [new CommandArgumentDefinition("value")])
        ]);
        var extensions = new LoadedExtensions(
            new CommandSchema([new CommandListDefinition("enum", "mode", ["one"], Namespace: "sample")], [command]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(), []);
        var viewModel = new SettingsViewModel(
            [], extensions, new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { }, commands: [command],
            userCommandNames: new HashSet<string>([command.QualifiedName], StringComparer.OrdinalIgnoreCase),
            saveUserCommand: _ => true);

        var details = Assert.Single(viewModel.CommandDetails);
        var argument = Assert.Single(Assert.Single(details.Actions).Arguments);
        Assert.Contains(details.ReferenceChoices, choice => choice.Reference == "enum:sample:mode");
        argument.SelectedReference = details.ReferenceChoices.Single(choice => choice.Reference == "enum:sample:mode");
        Assert.Equal("enum:sample:mode", argument.Definition.Ref);
        argument.SelectedReference = new CommandListReferenceChoice("list:runtimeItems", "list:runtimeItems");
        Assert.Equal("list:runtimeItems", argument.Definition.Ref);
        Assert.True(argument.IsListReference);
    }

    [Fact]
    public void CommandDetails_RetainsDraftWhenSaveCallbackRejects()
    {
        using var directory = new TemporaryDirectory();
        var command = CreateShellCommand("user-command");
        var extensions = new LoadedExtensions(
            new CommandSchema([], [command]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(), []);
        var viewModel = new SettingsViewModel(
            [], extensions, new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { }, commands: [command],
            userCommandNames: new HashSet<string>([command.QualifiedName], StringComparer.OrdinalIgnoreCase),
            saveUserCommand: _ => false);

        var details = Assert.Single(viewModel.CommandDetails);
        details.Description = "Unsaved draft";
        details.SaveChanges();

        Assert.Equal("Unsaved draft", details.Description);
        Assert.True(details.HasUnsavedChanges);
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
    public void ThemeNamesArgumentValueProvider_ListsSavedThemesByName()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        store.SetTheme(ColorThemeService.ToProfileTheme(ColorTheme.Default with
        {
            Id = "custom-zeta",
            Name = "Zeta"
        }));
        store.SetTheme(ColorThemeService.ToProfileTheme(ColorTheme.Default with
        {
            Id = "custom-alpha",
            Name = "Alpha"
        }));

        var provider = new ThemeNamesArgumentValueProvider(store);

        Assert.Equal(["Default", "Zeta", "Alpha"], provider.GetValues("list:themes"));
        Assert.Empty(provider.GetValues("list:secrets"));
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
    public void SecretPermissionCheckbox_PersistsPerExtensionGrantAndDefaultsToDeny()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var extensions = new LoadedExtensions(
            new CommandSchema([], []) { Settings = [] },
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            [new LoadedExtensionInfo("ado", "1.0", true) { Namespace = "ado" },
             new LoadedExtensionInfo("vscode", "1.0", true) { Namespace = "vscode" }]);
        var viewModel = new SettingsViewModel(
            [], extensions, store,
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { }, ["pat", "token"]);

        var ado = Assert.Single(viewModel.ExtensionSecretPermissions.Where(extension => extension.ExtensionName == "ado"));
        var vscode = Assert.Single(viewModel.ExtensionSecretPermissions.Where(extension => extension.ExtensionName == "vscode"));
        Assert.All(ado.Secrets, secret => Assert.False(secret.IsAllowed));
        Assert.All(vscode.Secrets, secret => Assert.False(secret.IsAllowed));

        Assert.DoesNotContain(viewModel.NavigationItems, item => item.Category == "secrets");
        var adoPage = viewModel.NavigationItems.Single(item => item.Category == "extension" && item.Id == "ado");
        var vscodePage = viewModel.NavigationItems.Single(item => item.Category == "extension" && item.Id == "vscode");
        viewModel.SelectedNavigationItem = adoPage;
        Assert.Same(ado, viewModel.SelectedExtensionSecretPermissions);
        viewModel.SelectedNavigationItem = vscodePage;
        Assert.Same(vscode, viewModel.SelectedExtensionSecretPermissions);

        var patPermission = ado.Secrets.Single(secret => secret.Name == "pat");
        patPermission.IsAllowed = true;

        Assert.Equal("[\"pat\"]", store.GetValue(ExtensionSecretAccessPolicy.GetSettingName("ado"), "[]"));
        Assert.Equal("[]", store.GetValue(ExtensionSecretAccessPolicy.GetSettingName("vscode"), "[]"));
    }

    [Fact]
    public void CommandLaunchPreferenceStore_DefaultsToNormalAndPersistsPerCommand()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var preferences = new CommandLaunchPreferenceStore(store);
        var editor = CreateShellCommand("editor");
        var secondCommand = CreateShellCommand("editor2");

        Assert.Equal(CommandLaunchMode.Normal, preferences.GetLaunchMode(editor));

        preferences.SetLaunchMode(editor, CommandLaunchMode.Elevated);
        Assert.Equal(CommandLaunchMode.Elevated, preferences.GetLaunchMode(editor));
        Assert.Equal(CommandLaunchMode.Normal, preferences.GetLaunchMode(secondCommand));

        preferences.SetLaunchMode(editor, CommandLaunchMode.DifferentUser);
        Assert.Equal(CommandLaunchMode.DifferentUser, new CommandLaunchPreferenceStore(
            new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"))).GetLaunchMode(editor));

        preferences.SetLaunchMode(editor, CommandLaunchMode.Normal);
        Assert.Equal(CommandLaunchMode.Normal, preferences.GetLaunchMode(editor));
    }

    [Fact]
    public void CommandsSettings_PersistSelectedLaunchModeForShellCommands()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var shellCommand = CreateShellCommand("editor");
        var nonShellCommand = CreateShellCommand("open", "url");
        var extensions = new LoadedExtensions(
            new CommandSchema([], [shellCommand, nonShellCommand]),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        var viewModel = new SettingsViewModel(
            [], extensions, store,
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { }, commands: [shellCommand, nonShellCommand]);

        var editorPreference = Assert.Single(viewModel.CommandLaunchPreferences);
        Assert.Equal("editor", editorPreference.CommandName);
        Assert.Equal(nameof(CommandLaunchMode.Normal), editorPreference.SelectedLaunchMode);

        editorPreference.SelectedLaunchMode = nameof(CommandLaunchMode.Elevated);

        Assert.Equal(
            nameof(CommandLaunchMode.Elevated),
            store.GetValue(CommandLaunchPreferenceStore.GetSettingName(shellCommand), nameof(CommandLaunchMode.Normal)));
    }

    private static CommandDefinition CreateShellCommand(string name, string kind = "shell")
        => new(name, kind, "", [new CommandActionDefinition("default", "", name, "app.exe", [])]);

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
