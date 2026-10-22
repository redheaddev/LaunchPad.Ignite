using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Platform.Terminals;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class SnippetSupportTests
{
    [Fact]
    public void StorePersistsMetadataAndScriptAndSupportsRenameAndRemove()
    {
        using var directory = new TemporaryDirectory();
        var store = new SnippetStore(directory.Path);

        var saved = store.Save("deploy", "powershell", ["PROJECT"], ["TOKEN"], "Write-Output $env:TOKEN");
        var snippetDirectory = Path.Combine(directory.Path, "deploy");
        var metadataPath = Path.Combine(snippetDirectory, "metadata.json");
        var scriptPath = Path.Combine(snippetDirectory, "script.ps1");

        Assert.True(File.Exists(metadataPath));
        Assert.Equal("Write-Output $env:TOKEN", File.ReadAllText(scriptPath));
        Assert.Contains("\"allowedSecrets\"", File.ReadAllText(metadataPath), StringComparison.Ordinal);
        Assert.Equal(["deploy"], store.GetNames());
        Assert.Equal("PROJECT", Assert.Single(store.Find("DEPLOY")!.Metadata.Arguments));

        Assert.True(store.Rename("deploy", "release"));
        Assert.Equal("release", Assert.Single(store.GetAll()).Metadata.Name);
        Assert.True(Directory.Exists(Path.Combine(directory.Path, "release")));
        Assert.False(Directory.Exists(snippetDirectory));
        Assert.True(store.Remove("release"));
        Assert.Empty(store.GetNames());
    }

    [Fact]
    public void StoreRemovalDeletesMetadataByStableSnippetId()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var store = new SnippetStore(Path.Combine(directory.Path, "snippets"), settings);
        var snippet = store.Save("deploy", "powershell", [], [], "Write-Output 'ok'");
        settings.SetObjectMetadata(new ObjectMetadataRecord(
            MetadataObjectType.Snippet,
            snippet.Metadata.Id,
            new Dictionary<string, string> { ["notes"] = "owned by team" }));

        Assert.True(store.Rename("deploy", "release"));
        Assert.Equal("owned by team", settings.GetObjectMetadata(MetadataObjectType.Snippet, snippet.Metadata.Id).Values["notes"]);
        Assert.True(store.Remove("release"));
        Assert.Empty(settings.GetObjectMetadata(MetadataObjectType.Snippet, snippet.Metadata.Id).Values);
    }

    [Fact]
    public void StoreMigratesLegacyIdDirectoryToSnippetName()
    {
        using var directory = new TemporaryDirectory();
        var id = Guid.NewGuid().ToString("N");
        var legacyDirectory = Path.Combine(directory.Path, id);
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllText(Path.Combine(legacyDirectory, "metadata.json"), $$"""
            {
              "id": "{{id}}",
              "name": "deploy",
              "type": "powershell",
              "arguments": [],
              "allowedSecrets": []
            }
            """);
        File.WriteAllText(Path.Combine(legacyDirectory, "script.ps1"), "Write-Output 'migrated'");
        var store = new SnippetStore(directory.Path);

        var migrated = Assert.Single(store.GetAll());

        Assert.Equal("deploy", migrated.Metadata.Name);
        Assert.Equal("Write-Output 'migrated'", migrated.Script);
        Assert.True(Directory.Exists(Path.Combine(directory.Path, "deploy")));
        Assert.False(Directory.Exists(legacyDirectory));
    }

    [Fact]
    public void StoreRefusesToMigrateLegacyDirectoryOverExistingNameDirectory()
    {
        using var directory = new TemporaryDirectory();
        var store = new SnippetStore(directory.Path);
        store.Save("deploy", "powershell", [], [], "Write-Output 'current'");
        var id = Guid.NewGuid().ToString("N");
        var legacyDirectory = Path.Combine(directory.Path, id);
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllText(Path.Combine(legacyDirectory, "metadata.json"), $$"""
            {
              "id": "{{id}}",
              "name": "deploy",
              "type": "powershell",
              "arguments": [],
              "allowedSecrets": []
            }
            """);
        File.WriteAllText(Path.Combine(legacyDirectory, "script.ps1"), "Write-Output 'legacy'");

        Assert.Throws<InvalidDataException>(() => store.GetAll());
        Assert.True(Directory.Exists(legacyDirectory));
        Assert.Equal("Write-Output 'current'", File.ReadAllText(Path.Combine(directory.Path, "deploy", "script.ps1")));
    }

    [Fact]
    public void StoreRejectsInvalidNamesTypesAndOverlappingEnvironmentNames()
    {
        using var directory = new TemporaryDirectory();
        var store = new SnippetStore(directory.Path);

        Assert.Throws<ArgumentException>(() => store.Save("has space", "powershell", [], [], "Write-Output"));
        Assert.Throws<ArgumentException>(() => store.Save("build", "python", [], [], "print(1)"));
        Assert.Throws<ArgumentException>(() => store.Save("build", "powershell", ["TOKEN"], ["token"], "Write-Output"));
    }

    [Fact]
    public void CaretFilterMatchesSnippetNamesAndBuildsRunCommand()
    {
        var matches = SnippetCompletion.Filter(["build", "Deploy", "destroy"], " ^de ");

        Assert.Equal(["Deploy", "destroy"], matches);
        Assert.Empty(SnippetCompletion.Filter(["build"], "build"));
        Assert.Equal("snippet run Deploy", SnippetCompletion.BuildRunCommand("Deploy"));
    }

    [Theory]
    [InlineData("Deploy", "de", "ploy")]
    [InlineData("Deploy", "DEP", "loy")]
    [InlineData("Deploy", "deploy", "")]
    [InlineData("Deploy", "x", "")]
    public void GhostSuffixReturnsUnmatchedCaseInsensitivePrefix(string candidate, string prefix, string expected)
    {
        Assert.Equal(expected, CompletionText.GetGhostSuffix(candidate, prefix));
    }

    [Fact]
    public void SnippetNameArgumentsUseSingleChoiceCompletion()
    {
        var actions = SnippetCommandDefinition.Definition.Actions;

        Assert.All(actions, action => Assert.Equal(ListSelectionMode.Single, action.Arguments[0].ListMode));
        Assert.True(actions.Single(action => action.Name == "set").Arguments[0].AllowCustomValues);
    }

    [Fact]
    public async Task RunPassesArgumentsAndOnlyAllowedStoredSecretsToTerminal()
    {
        using var directory = new TemporaryDirectory();
        var store = new SnippetStore(directory.Path);
        store.Save("deploy", "powershell", ["PROJECT"], ["TOKEN"], "Write-Output $env:TOKEN");
        var secrets = new FakeSecretStore(new Dictionary<string, string>
        {
            ["TOKEN"] = "secret-value",
            ["OTHER"] = "unapproved-value"
        });
        var terminal = new FakeTerminalLauncher();
        var secretGate = new FakeSecretAccessGate { HasMasterPassword = true };
        var secretDialog = new FakeSecretEntryDialogService("correct");
        var dialog = new FakeSnippetDialogService(new Dictionary<string, string> { ["PROJECT"] = "ignite" });
        var model = new SnippetCommandModel(store, secrets, dialog, secretGate, secretDialog, terminal);

        var result = await model.ExecuteAsync(CreateRunCommand("deploy"));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("correct", secretGate.LastPassword);
        Assert.Equal("PROJECT", Assert.Single(dialog.RequestedArguments));
        Assert.Equal("secret-value", terminal.Environment!["TOKEN"]);
        Assert.Equal("ignite", terminal.Environment["PROJECT"]);
        Assert.DoesNotContain("OTHER", terminal.Environment.Keys);
        Assert.Equal("powershell", terminal.Type);
        Assert.True(File.Exists(terminal.ScriptPath));
    }

    [Fact]
    public async Task RunUsesInlineArgumentsWithoutShowingTheArgumentsDialog()
    {
        using var directory = new TemporaryDirectory();
        var store = new SnippetStore(directory.Path);
        store.Save("deploy", "powershell", ["PROJECT", "AD_USERNAME"], [], "Write-Output $env:PROJECT");
        var terminal = new FakeTerminalLauncher();
        var dialog = new FakeSnippetDialogService(new Dictionary<string, string>());
        var model = new SnippetCommandModel(
            store,
            new FakeSecretStore(new Dictionary<string, string>()),
            dialog,
            new FakeSecretAccessGate(),
            new FakeSecretEntryDialogService(null),
            terminal);

        var result = await model.ExecuteAsync(CreateRunCommand(
            "deploy",
            ("project", "ignite"),
            ("AD_USERNAME", "andmil")));

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(dialog.RequestedArguments);
        Assert.Equal("ignite", terminal.Environment!["PROJECT"]);
        Assert.Equal("andmil", terminal.Environment["AD_USERNAME"]);
    }

    [Fact]
    public async Task RunPromptsOnlyForArgumentsNotProvidedInline()
    {
        using var directory = new TemporaryDirectory();
        var store = new SnippetStore(directory.Path);
        store.Save("deploy", "powershell", ["PROJECT", "ENVIRONMENT"], [], "Write-Output $env:PROJECT");
        var terminal = new FakeTerminalLauncher();
        var dialog = new FakeSnippetDialogService(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ENVIRONMENT"] = "test"
        });
        var model = new SnippetCommandModel(
            store,
            new FakeSecretStore(new Dictionary<string, string>()),
            dialog,
            new FakeSecretAccessGate(),
            new FakeSecretEntryDialogService(null),
            terminal);

        var result = await model.ExecuteAsync(CreateRunCommand("deploy", ("PROJECT", "ignite")));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(["ENVIRONMENT"], dialog.RequestedArguments);
        Assert.Equal("ignite", terminal.Environment!["PROJECT"]);
        Assert.Equal("test", terminal.Environment["ENVIRONMENT"]);
    }

    [Fact]
    public async Task RunRejectsInlineArgumentNotDeclaredBySnippet()
    {
        using var directory = new TemporaryDirectory();
        var store = new SnippetStore(directory.Path);
        store.Save("deploy", "powershell", ["PROJECT"], [], "Write-Output $env:PROJECT");
        var dialog = new FakeSnippetDialogService(new Dictionary<string, string>());
        var model = new SnippetCommandModel(
            store,
            new FakeSecretStore(new Dictionary<string, string>()),
            dialog,
            new FakeSecretAccessGate(),
            new FakeSecretEntryDialogService(null),
            new FakeTerminalLauncher());

        var result = await model.ExecuteAsync(CreateRunCommand("deploy", ("UNKNOWN", "value")));

        Assert.False(result.Succeeded);
        Assert.Contains("does not declare", result.Message, StringComparison.Ordinal);
        Assert.Empty(dialog.RequestedArguments);
    }

    private static ParsedCommand CreateRunCommand(string name, params (string Name, string Value)[] arguments)
    {
        var definition = SnippetCommandDefinition.Definition;
        var action = definition.Actions.Single(candidate => candidate.Name == "run");
        var suppliedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = name
        };
        foreach (var (argumentName, value) in arguments)
        {
            suppliedArguments.Add(argumentName, value);
        }

        return new ParsedCommand(
            definition,
            action,
            suppliedArguments);
    }

    private sealed class FakeSnippetDialogService : ISnippetDialogService
    {
        private readonly IReadOnlyDictionary<string, string> _argumentValues;

        public FakeSnippetDialogService(IReadOnlyDictionary<string, string> argumentValues)
        {
            _argumentValues = argumentValues;
        }

        public IReadOnlyList<string> RequestedArguments { get; private set; } = [];

        public Task<SnippetEditorResult?> ShowEditorAsync(string name, SnippetDefinition? snippet, IReadOnlyList<string> availableSecrets)
            => Task.FromResult<SnippetEditorResult?>(null);

        public Task<bool> ConfirmRemoveAsync(string name) => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>?> ShowArgumentsAsync(string name, IReadOnlyList<string> arguments)
        {
            RequestedArguments = arguments.ToArray();
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(_argumentValues);
        }
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public FakeSecretStore(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public IReadOnlyList<string> GetNames() => _values.Keys.ToArray();

        public bool TryGet(string name, out string value) => _values.TryGetValue(name, out value!);

        public void Set(string name, string value) => throw new NotSupportedException();

        public bool Remove(string name) => throw new NotSupportedException();
    }

    private sealed class FakeSecretAccessGate : ISecretAccessGate
    {
        public bool HasMasterPassword { get; init; }

        public bool IsUnlocked { get; private set; }

        public string? LastPassword { get; private set; }

        public bool TrySetMasterPassword(string password) => throw new NotSupportedException();

        public bool TryUnlock(string password)
        {
            LastPassword = password;
            IsUnlocked = password == "correct";
            return IsUnlocked;
        }

        public void Lock() => IsUnlocked = false;
    }

    private sealed class FakeSecretEntryDialogService : ISecretEntryDialogService
    {
        private readonly string? _value;

        public FakeSecretEntryDialogService(string? value)
        {
            _value = value;
        }

        public Task<string?> ShowAsync(string title, bool requireConfirmation) => Task.FromResult(_value);

        public Task ShowSecretAsync(string title, string value) => Task.CompletedTask;
    }

    private sealed class FakeTerminalLauncher : ISecretTerminalLauncher
    {
        public string? ScriptPath { get; private set; }

        public string? Type { get; private set; }

        public IReadOnlyDictionary<string, string>? Environment { get; private set; }

        public void OpenShell(IReadOnlyDictionary<string, string> environment, string? workingDirectory)
            => throw new NotSupportedException();

        public void RunScript(string scriptPath, string type, IReadOnlyDictionary<string, string> environment)
        {
            ScriptPath = scriptPath;
            Type = type;
            Environment = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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
