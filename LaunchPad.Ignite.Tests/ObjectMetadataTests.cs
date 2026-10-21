using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.ViewModels;

namespace LaunchPad.Ignite.Tests;

public class ObjectMetadataTests
{
    [Fact]
    public void ProfileSettingsStorePersistsMetadataSchemaAndValues()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ProfileSettingsStore(path);
        var schema = store.GetMetadataSchema(MetadataObjectType.Secret);
        var record = new ObjectMetadataRecord(
            MetadataObjectType.Secret,
            "apiToken",
            new Dictionary<string, string>
            {
                ["expiresOn"] = "2030-01-02",
                ["notes"] = "rotate annually"
            });
        store.SetObjectMetadata(record);
        store.SetValue("test:setting", "value");

        var reopened = new ProfileSettingsStore(path);

        Assert.True(File.Exists(Path.Combine(directory.Path, "metadata.json")));
        Assert.DoesNotContain("metadata", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(schema.Fields, field => field.Id == "expiresOn" && field.Type == MetadataFieldType.Date);
        Assert.Contains(schema.Fields, field => field.Id == "notes" && field.Type == MetadataFieldType.Text);
        Assert.Equal("2030-01-02", reopened.GetObjectMetadata(MetadataObjectType.Secret, "apiToken").Values["expiresOn"]);
        Assert.Equal("rotate annually", reopened.GetObjectMetadata(MetadataObjectType.Secret, "apiToken").Values["notes"]);
    }

    [Fact]
    public void ProfileSettingsStoreMigratesEmbeddedMetadataToSidecarFile()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(settingsPath,
            "{\"settings\":{\"themes\":[],\"metadata\":{\"schemas\":[{\"objectType\":\"Secret\",\"fields\":[{\"id\":\"custom\",\"name\":\"Custom\",\"type\":\"Text\"}]}],\"records\":[{\"objectType\":\"Secret\",\"objectId\":\"apiToken\",\"values\":{\"custom\":\"preserved\"}}]}}}");

        var store = new ProfileSettingsStore(settingsPath);

        Assert.True(File.Exists(Path.Combine(directory.Path, "metadata.json")));
        Assert.Equal("preserved", store.GetObjectMetadata(MetadataObjectType.Secret, "apiToken").Values["custom"]);
        Assert.Contains(store.GetMetadataSchema(MetadataObjectType.Secret).Fields, field => field.Id == "custom");
        Assert.DoesNotContain("metadata", File.ReadAllText(settingsPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProfileSettingsStoreOmitsNullValuesAndRemovesEmptyRecord()
    {
        using var directory = new TemporaryDirectory();
        var metadataPath = Path.Combine(directory.Path, "metadata.json");
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        store.SetObjectMetadata(new ObjectMetadataRecord(
            MetadataObjectType.Secret,
            "apiToken",
            new Dictionary<string, string>
            {
                ["notes"] = "keep",
                ["unused"] = null!
            }));

        Assert.Equal("keep", store.GetObjectMetadata(MetadataObjectType.Secret, "apiToken").Values["notes"]);
        Assert.DoesNotContain("unused", File.ReadAllText(metadataPath), StringComparison.Ordinal);
        store.SetObjectMetadata(new ObjectMetadataRecord(
            MetadataObjectType.Secret,
            "apiToken",
            new Dictionary<string, string> { ["notes"] = null! }));

        Assert.Empty(store.GetObjectMetadata(MetadataObjectType.Secret, "apiToken").Values);
        Assert.DoesNotContain("apiToken", File.ReadAllText(metadataPath), StringComparison.Ordinal);
    }

    [Fact]
    public void SecretMetadataEditorValidatesChoicesAndPersistsValues()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var editor = new SecretMetadataEditorViewModel(store, ["apiToken"]);
        var expiration = editor.Values.Single(value => value.Field.Id == "expiresOn");

        expiration.Value = "not-a-date";
        Assert.Equal(string.Empty, store.GetObjectMetadata(MetadataObjectType.Secret, "apiToken").Values.GetValueOrDefault("expiresOn", string.Empty));
        Assert.Contains("valid date", editor.Status, StringComparison.OrdinalIgnoreCase);

        expiration.Value = "2030-01-02";
        Assert.Equal("2030-01-02", store.GetObjectMetadata(MetadataObjectType.Secret, "apiToken").Values["expiresOn"]);
    }

    [Fact]
    public void SharedMetadataEditorUsesIndependentSchemasAndRecordsPerObjectType()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var snippetId = Guid.NewGuid().ToString("N");
        settings.SetMetadataSchema(new ObjectMetadataSchema(
            MetadataObjectType.Snippet,
            [new MetadataFieldDefinition("owner", "Owner", MetadataFieldType.Text)]));
        var snippets = new ObjectMetadataEditorViewModel(
            settings,
            MetadataObjectType.Snippet,
            [new ObjectMetadataTarget(MetadataObjectType.Snippet, snippetId, "deploy")]);

        Assert.Equal(MetadataObjectType.Snippet, snippets.ObjectType);
        Assert.Equal("Owner", Assert.Single(snippets.Fields).Name);
        snippets.Values[0].Value = "Operations";

        Assert.Equal("Operations", settings.GetObjectMetadata(MetadataObjectType.Snippet, snippetId).Values["owner"]);
        Assert.Contains(settings.GetMetadataSchema(MetadataObjectType.Secret).Fields, field => field.Id == "expiresOn");
    }

    [Fact]
    public void SettingsMetadataTargetsSeparateCommandsFromShortcutDefinitions()
    {
        using var directory = new TemporaryDirectory();
        var shortcut = new LaunchPad.Ignite.Core.Schema.CommandDefinition("deploy", "shortcut", "Deploy", [
            new LaunchPad.Ignite.Core.Schema.CommandActionDefinition("default", "", "", "deploy-command ", [])
        ], "ops", Handler: "shortcut");
        var regularCommand = new LaunchPad.Ignite.Core.Schema.CommandDefinition("build", "shell", "Build", []);
        var function = new LaunchPad.Ignite.Core.Schema.CommandFunctionDefinition("lookup", "httpGet", [], "https://example.test", Namespace: "ops");
        var variable = new LaunchPad.Ignite.Core.Schema.CommandVariableDefinition("region", "west", "ops");
        var list = new LaunchPad.Ignite.Core.Schema.CommandListDefinition("enum", "environment", ["dev", "prod"], "dev", "ops");
        var extensions = new LaunchPad.Ignite.App.Commands.LoadedExtensions(
            new LaunchPad.Ignite.Core.Schema.CommandSchema([list], [shortcut, regularCommand])
            {
                Functions = [function],
                Variables = [variable]
            },
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandExecutor>(),
            new Dictionary<string, LaunchPad.Ignite.Core.Execution.ICommandFunctionExecutor>(),
            []);
        var viewModel = new SettingsViewModel(
            [], extensions,
            new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            new LaunchPad.Ignite.App.Commands.ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { }, _ => { },
            commands: [shortcut, regularCommand],
            lists: [list]);

        Assert.Contains(MetadataObjectType.Shortcut, viewModel.MetadataObjectTypes);
        Assert.Contains(MetadataObjectType.Command, viewModel.MetadataObjectTypes);
        Assert.Contains(MetadataObjectType.Function, viewModel.MetadataObjectTypes);
        Assert.Contains(MetadataObjectType.List, viewModel.MetadataObjectTypes);
        Assert.Contains(MetadataObjectType.Variable, viewModel.MetadataObjectTypes);
        viewModel.SelectedMetadataObjectType = MetadataObjectType.Command;
        var commandTarget = Assert.Single(viewModel.MetadataEditor.Targets);
        Assert.Equal("build", commandTarget.Id);
        Assert.Equal("build", commandTarget.DisplayName);

        viewModel.SelectedMetadataObjectType = MetadataObjectType.Function;
        Assert.Equal("ops:lookup", Assert.Single(viewModel.MetadataEditor.Targets).Id);
        viewModel.SelectedMetadataObjectType = MetadataObjectType.List;
        Assert.Equal("ops:environment", Assert.Single(viewModel.MetadataEditor.Targets).Id);
        viewModel.SelectedMetadataObjectType = MetadataObjectType.Variable;
        Assert.Equal("ops:region", Assert.Single(viewModel.MetadataEditor.Targets).Id);

        viewModel.SelectedMetadataObjectType = MetadataObjectType.Shortcut;
        var shortcutTarget = Assert.Single(viewModel.MetadataEditor.Targets);
        Assert.Equal("ops:deploy", shortcutTarget.Id);
        Assert.Equal("ops:deploy", shortcutTarget.DisplayName);
    }

    [Fact]
    public void MetadataEditorListsObjectsWithMetadataAndSynchronizesSelection()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        settings.SetMetadataSchema(new ObjectMetadataSchema(
            MetadataObjectType.Snippet,
            [new MetadataFieldDefinition("owner", "Owner", MetadataFieldType.Text)]));
        settings.SetObjectMetadata(new ObjectMetadataRecord(
            MetadataObjectType.Snippet,
            "snippet-id",
            new Dictionary<string, string> { ["owner"] = "Operations" }));
        var editor = new ObjectMetadataEditorViewModel(
            settings,
            MetadataObjectType.Snippet,
            [
                new ObjectMetadataTarget(MetadataObjectType.Snippet, "snippet-id", "deploy"),
                new ObjectMetadataTarget(MetadataObjectType.Snippet, "empty-id", "empty")
            ]);

        var item = Assert.Single(editor.TargetsWithMetadata);
        Assert.Equal("deploy", item.DisplayName);
        Assert.Equal("Owner: Operations", Assert.Single(item.SummaryValues).Summary);

        editor.SelectedTargetWithMetadata = item;
        Assert.Equal("snippet-id", editor.SelectedTarget?.Id);

        editor.SelectedTarget = editor.Targets.Single(target => target.Id == "empty-id");
        editor.Values.Single(value => value.Field.Id == "owner").Value = "Engineering";
        Assert.Equal(2, editor.TargetsWithMetadata.Count);
        Assert.Equal("empty-id", editor.SelectedTargetWithMetadata?.Id);
        Assert.False(editor.HasNoMetadataTargets);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ignite-metadata-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
