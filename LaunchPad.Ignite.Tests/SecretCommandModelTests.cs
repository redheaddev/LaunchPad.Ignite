using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class SecretCommandModelTests
{
    [Fact]
    public async Task SetAndCopyEnvNeverReturnTheSecretInTheExecutionResult()
    {
        var store = new FakeSecretStore();
        var clipboard = new FakeClipboardService();
        var model = CreateModel(store: store, clipboard: clipboard, dialog: new FakeDialogService("a'b"));

        var setResult = await ExecuteAsync(model, "secret set apiToken");
        var copyResult = await ExecuteAsync(model, "secret copy-env apiToken");

        Assert.True(setResult.Succeeded);
        Assert.True(copyResult.Succeeded);
        Assert.DoesNotContain("a'b", setResult.Message);
        Assert.DoesNotContain("a'b", copyResult.Message);
        Assert.Equal("$env:apiToken='a''b'", clipboard.Value);
        Assert.Equal(TimeSpan.FromSeconds(15), clipboard.Expiry);
    }

    [Fact]
    public async Task ShowRequiresUnlockAndNeverReturnsTheSecretInTheExecutionResult()
    {
        const string secretValue = "sensitive";
        var dialog = new FakeDialogService("unused");
        var model = CreateModel(
            store: new FakeSecretStore(("token", secretValue)),
            dialog: dialog);

        var result = await ExecuteAsync(model, "secret show token");

        Assert.True(result.Succeeded);
        Assert.Equal(secretValue, dialog.DisplayedSecret);
        Assert.DoesNotContain(secretValue, result.Message);
    }

    [Fact]
    public void ThemeCommandPersistsAndAppliesSelectedTheme()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var theme = ColorThemeService.ToProfileTheme(ColorTheme.Default with { Id = "custom-night", Name = "Night" });
        store.SetTheme(theme);
        var applied = (ColorTheme?)null;
        var schema = new CommandSchema([], [ThemeCommandDefinition.Definition]);
        var parsed = new CommandLineParser(
            new CommandCatalog(schema, ["theme"]),
            new ThemeNamesArgumentValueProvider(store)).Parse("theme Night");

        Assert.Equal(CommandParseStatus.Valid, parsed.Status);
        var result = new ThemeCommandModel(store, selected => applied = selected).Execute(parsed.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal("custom-night", store.GetValue("app:colorTheme", "default"));
        Assert.Equal("custom-night", applied!.Id);
    }

    [Fact]
    public void AsteriskCommandListsSecretNamesWithoutReadingOrReturningValues()
    {
        var store = new FakeSecretStore(("zebra", "hidden-zebra"), ("alpha", "hidden-alpha"));
        var schema = new CommandSchema([], [SecretListCommandDefinition.Definition]);
        var catalog = new CommandCatalog(schema, ["secretList"]);
        var parseResult = new CommandLineParser(catalog, new DictionaryArgumentValueProvider(
            new Dictionary<string, IReadOnlyList<string>>())).Parse("*");
        var model = new SecretListCommandModel(store);

        Assert.Equal(CommandParseStatus.Valid, parseResult.Status);
        var result = model.Execute(parseResult.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal($"alpha{Environment.NewLine}zebra", result.Message);
        Assert.DoesNotContain("hidden-alpha", result.Message);
        Assert.DoesNotContain("hidden-zebra", result.Message);
    }

    [Fact]
    public async Task WrongPasswordBlocksShowCopyAndShellActions()
    {
        var store = new FakeSecretStore(("token", "sensitive"));
        var gate = new FakeSecretAccessGate(hasMasterPassword: true, isUnlocked: false);
        var clipboard = new FakeClipboardService();
        var terminal = new FakeTerminalLauncher();
        var dialog = new FakeDialogService("unused");
        var model = CreateModel(store, gate, clipboard, terminal, dialog);

        var copyResult = await ExecuteAsync(model, "secret copy-value token");
        var showResult = await ExecuteAsync(model, "secret show token");
        var shellResult = await ExecuteAsync(model, "secret shell token");

        Assert.False(copyResult.Succeeded);
        Assert.False(showResult.Succeeded);
        Assert.False(shellResult.Succeeded);
        Assert.Null(clipboard.Value);
        Assert.Null(dialog.DisplayedSecret);
        Assert.Equal(3, dialog.Titles.Count);
        Assert.All(dialog.Titles, title => Assert.Equal("Unlock secrets", title));
        Assert.Null(terminal.Environment);
    }

    [Fact]
    public async Task LockedExposureActionsPromptAndContinueAfterCorrectPassword()
    {
        var store = new FakeSecretStore(("token", "sensitive"));
        var gate = new FakeSecretAccessGate(hasMasterPassword: true, isUnlocked: false);
        var clipboard = new FakeClipboardService();
        var terminal = new FakeTerminalLauncher();
        var dialog = new FakeDialogService("master-password");
        var model = CreateModel(store, gate, clipboard, terminal, dialog);

        var showResult = await ExecuteAsync(model, "secret show token");
        gate.Lock();
        var copyEnvResult = await ExecuteAsync(model, "secret copy-env token");
        gate.Lock();
        var copyValueResult = await ExecuteAsync(model, "secret copy-value token");
        gate.Lock();
        var shellResult = await ExecuteAsync(model, "secret shell token");

        Assert.True(showResult.Succeeded);
        Assert.True(copyEnvResult.Succeeded);
        Assert.True(copyValueResult.Succeeded);
        Assert.True(shellResult.Succeeded);
        Assert.Equal("sensitive", dialog.DisplayedSecret);
        Assert.Equal("sensitive", clipboard.Value);
        Assert.Equal("sensitive", terminal.Environment!["token"]);
        Assert.Equal(4, dialog.Titles.Count);
        Assert.All(dialog.Titles, title => Assert.Equal("Unlock secrets", title));
        Assert.All(dialog.ConfirmationRequests, requireConfirmation => Assert.False(requireConfirmation));
    }

    [Fact]
    public async Task SecretManagementActionsRemainAvailableWhileLocked()
    {
        var store = new FakeSecretStore(("oldName", "old-value"));
        var gate = new FakeSecretAccessGate(hasMasterPassword: true, isUnlocked: false);
        var dialog = new FakeDialogService("new-value");
        var model = CreateModel(store, gate, dialog: dialog);

        var setResult = await ExecuteAsync(model, "secret set newName");
        var renameResult = await ExecuteAsync(model, "secret rename oldName renamed");
        var removeResult = await ExecuteAsync(model, "secret remove renamed");

        Assert.True(setResult.Succeeded);
        Assert.True(renameResult.Succeeded);
        Assert.True(removeResult.Succeeded);
        Assert.Equal(["newName"], store.GetNames());
        Assert.Equal(["Set secret 'newName'"], dialog.Titles);
    }

    [Fact]
    public async Task MasterPasswordSetupUsesSingleEntryAndUnlocksImmediately()
    {
        var gate = new FakeSecretAccessGate();
        var dialog = new FakeDialogService("master-password");
        var model = CreateModel(gate: gate, dialog: dialog);

        var result = await ExecuteAsync(model, "secret master set");

        Assert.True(result.Succeeded);
        Assert.True(gate.HasMasterPassword);
        Assert.True(gate.IsUnlocked);
        Assert.Equal(["Set master password"], dialog.Titles);
        Assert.Equal([false], dialog.ConfirmationRequests);
    }

    [Fact]
    public void SecretNameSuggestionsUseLiveStoreAndAllowCustomNamesForSet()
    {
        var store = new FakeSecretStore(("existing", "value"));
        var schema = new CommandSchema([], [SecretCommandDefinition.Definition]);
        var valueProvider = new CommandSchemaValueProvider(schema, new SecretNamesArgumentValueProvider(store));
        var session = new CommandCompletionSession(new CommandCatalog(schema, ["secret"]), valueProvider);

        var state = session.UpdateInputText("secret show ");
        Assert.Contains(state.Items, item => item.Value == "existing");
        Assert.False(state.IsChecklistActive);

        state = session.UpdateInputText("secret copy-env ");
        Assert.Contains(state.Items, item => item.Value == "existing");
        Assert.False(state.IsChecklistActive);
        state = session.UpdateInputText("secret copy-env ex");
        Assert.Contains(state.Items, item => item.Value == "existing");
        Assert.DoesNotContain(state.Items, item => item.Value == "addedLater");
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret copy-env existing", state.InputText);
        Assert.False(state.IsChecklistActive);
        Assert.Empty(state.Items);

        state = session.UpdateInputText("secret copy-value ");
        Assert.Contains(state.Items, item => item.Value == "existing");
        Assert.False(state.IsChecklistActive);

        state = session.UpdateInputText("secret set ");
        Assert.Contains(state.Items, item => item.Value == "existing");

        store.Set("addedLater", "later-value");
        state = session.UpdateInputText("secret set a");
        Assert.Contains(state.Items, item => item.Value == "addedLater");

        var parser = new CommandLineParser(new CommandCatalog(schema, ["secret"]), valueProvider);
        Assert.Equal(CommandParseStatus.Valid, parser.Parse("secret set brandNew").Status);
        Assert.Equal(CommandParseStatus.Valid, parser.Parse("secret rename existing brandNew").Status);
        Assert.Equal(CommandParseStatus.Invalid, parser.Parse("secret show brandNew").Status);

        state = session.UpdateInputText("secret shell ");
        Assert.True(state.IsChecklistActive);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell existing", state.InputText);
        Assert.Equal(",addedLater", state.GhostText);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell existing,addedLater", state.InputText);

        var shellParse = parser.Parse("secret shell existing,addedLater C:\\work");
        Assert.Equal(CommandParseStatus.Valid, shellParse.Status);
        Assert.Equal("existing,addedLater", shellParse.ParsedCommand!.Arguments["secretNames"]);
    }

    [Fact]
    public async Task ShellReceivesSelectedSecretsAsEnvironmentAndWorkingDirectory()
    {
        var store = new FakeSecretStore(("first", "one"), ("second", "two"));
        var terminal = new FakeTerminalLauncher();
        var model = CreateModel(store: store, terminal: terminal);

        var result = await ExecuteAsync(model, "secret shell first,second C:\\work");

        Assert.True(result.Succeeded);
        Assert.Equal("C:\\work", terminal.WorkingDirectory);
        Assert.Equal("one", terminal.Environment!["first"]);
        Assert.Equal("two", terminal.Environment["second"]);
        Assert.DoesNotContain("one", result.Message);
        Assert.DoesNotContain("two", result.Message);
    }

    [Fact]
    public async Task RenameAndRemoveOperateOnTheSelectedSecret()
    {
        var store = new FakeSecretStore(("oldName", "value"));
        var model = CreateModel(store: store);

        var renameResult = await ExecuteAsync(model, "secret rename oldName newName");
        var removeResult = await ExecuteAsync(model, "secret remove newName");

        Assert.True(renameResult.Succeeded);
        Assert.True(removeResult.Succeeded);
        Assert.Empty(store.GetNames());
    }

    [Fact]
    public void MasterPasswordGateExpiresAndStartsLockedAfterRestart()
    {
        using var directory = new TemporaryDirectory();
        var statePath = Path.Combine(directory.Path, "master.json");
        var timeProvider = new AdjustableTimeProvider();
        var gate = new SecretAccessGate(statePath, timeProvider);

        Assert.False(gate.HasMasterPassword);
        Assert.True(gate.IsUnlocked);
        Assert.True(gate.TrySetMasterPassword("master-password"));
        Assert.True(gate.IsUnlocked);

        timeProvider.Advance(TimeSpan.FromSeconds(60));
        Assert.False(gate.IsUnlocked);

        var restartedGate = new SecretAccessGate(statePath, timeProvider);
        Assert.True(restartedGate.HasMasterPassword);
        Assert.False(restartedGate.IsUnlocked);
        Assert.False(restartedGate.TryUnlock("wrong-password"));
        Assert.True(restartedGate.TryUnlock("master-password"));
        Assert.True(restartedGate.IsUnlocked);
    }

    private static SecretCommandModel CreateModel(
        FakeSecretStore? store = null,
        FakeSecretAccessGate? gate = null,
        FakeClipboardService? clipboard = null,
        FakeTerminalLauncher? terminal = null,
        FakeDialogService? dialog = null)
        => new(
            store ?? new FakeSecretStore(),
            gate ?? new FakeSecretAccessGate(),
            dialog ?? new FakeDialogService("secret-value"),
            clipboard ?? new FakeClipboardService(),
            terminal ?? new FakeTerminalLauncher());

    private static async Task<CommandExecutionResult> ExecuteAsync(SecretCommandModel model, string commandText)
    {
        var schema = new CommandSchema([], [SecretCommandDefinition.Definition]);
        var catalog = new CommandCatalog(schema, ["secret"]);
        var tokens = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var secretNames = tokens.Length > 2 ? tokens[2].Split(',') : [];
        var dynamicProvider = new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>
        {
            ["list:secrets"] = secretNames
        });
        var parser = new CommandLineParser(catalog, new CommandSchemaValueProvider(schema, dynamicProvider));
        var parseResult = parser.Parse(commandText);
        Assert.Equal(CommandParseStatus.Valid, parseResult.Status);
        return await model.ExecuteAsync(parseResult.ParsedCommand!);
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

        public FakeSecretStore(params (string Name, string Value)[] secrets)
        {
            foreach (var (name, value) in secrets)
            {
                _secrets[name] = value;
            }
        }

        public IReadOnlyList<string> GetNames() => _secrets.Keys.ToArray();

        public bool TryGet(string name, out string value) => _secrets.TryGetValue(name, out value!);

        public void Set(string name, string value) => _secrets[name] = value;

        public bool Remove(string name) => _secrets.Remove(name);
    }

    private sealed class FakeSecretAccessGate : ISecretAccessGate
    {
        public FakeSecretAccessGate(bool hasMasterPassword = false, bool isUnlocked = true)
        {
            HasMasterPassword = hasMasterPassword;
            IsUnlocked = !hasMasterPassword || isUnlocked;
        }

        public bool HasMasterPassword { get; private set; }

        public bool IsUnlocked { get; private set; }

        public bool TrySetMasterPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return false;
            }

            HasMasterPassword = true;
            IsUnlocked = true;
            return true;
        }

        public bool TryUnlock(string password)
        {
            IsUnlocked = HasMasterPassword && password == "master-password";
            return IsUnlocked;
        }

        public void Lock() => IsUnlocked = false;
    }

    private sealed class FakeDialogService : ISecretEntryDialogService
    {
        private readonly string? _value;

        public FakeDialogService(string? value)
        {
            _value = value;
        }

        public string? DisplayedSecret { get; private set; }

        public List<string> Titles { get; } = [];

        public List<bool> ConfirmationRequests { get; } = [];

        public Task<string?> ShowAsync(string title, bool requireConfirmation)
        {
            Titles.Add(title);
            ConfirmationRequests.Add(requireConfirmation);
            return Task.FromResult(_value);
        }

        public Task ShowSecretAsync(string title, string value)
        {
            DisplayedSecret = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? Value { get; private set; }

        public TimeSpan Expiry { get; private set; }

        public Task CopyWithExpiryAsync(string value, TimeSpan expiry)
        {
            Value = value;
            Expiry = expiry;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTerminalLauncher : ISecretTerminalLauncher
    {
        public IReadOnlyDictionary<string, string>? Environment { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public void OpenShell(IReadOnlyDictionary<string, string> environment, string? workingDirectory)
        {
            Environment = environment;
            WorkingDirectory = workingDirectory;
        }
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
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
            => Directory.Delete(Path, recursive: true);
    }
}
