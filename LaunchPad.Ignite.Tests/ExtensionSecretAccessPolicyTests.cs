using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.Tests;

public sealed class ExtensionSecretAccessPolicyTests
{
    [Fact]
    public void TryGetSecret_DeniesByDefaultWithoutReadingSecretStore()
    {
        using var directory = new TemporaryDirectory();
        var store = new TrackingSecretStore(("pat", "sensitive"));
        var policy = new ExtensionSecretAccessPolicy(
            new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")),
            store);

        var result = policy.TryGetSecret("ado", "pat");

        Assert.False(result.Found);
        Assert.Empty(store.ReadNames);
    }

    [Fact]
    public void TryGetSecret_AllowsGrantedSecretWithoutUnlockingVault()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        settings.SetValue(ExtensionSecretAccessPolicy.GetSettingName("ado"), "[\"azdo_token\"]");
        var store = new TrackingSecretStore(("azdo_token", "pat"));
        var policy = new ExtensionSecretAccessPolicy(settings, store);

        var result = policy.TryGetSecret("ado", "azdo_token");

        Assert.True(result.Found);
        Assert.Equal("pat", result.Value);
    }

    [Fact]
    public void TryGetSecret_GrantIsScopedToExtensionAndSecret()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var settings = new ProfileSettingsStore(settingsPath);
        settings.SetValue(ExtensionSecretAccessPolicy.GetSettingName("ado"), "[\"pat\"]");
        var store = new TrackingSecretStore(("pat", "sensitive"), ("other", "other-value"));
        var policy = new ExtensionSecretAccessPolicy(settings, store);

        var allowed = policy.TryGetSecret("ado", "pat");
        var wrongSecret = policy.TryGetSecret("ado", "other");
        var wrongExtension = policy.TryGetSecret("vscode", "pat");

        Assert.True(allowed.Found);
        Assert.Equal("sensitive", allowed.Value);
        Assert.False(wrongSecret.Found);
        Assert.False(wrongExtension.Found);
        Assert.Equal(["pat"], store.ReadNames);
    }

    [Fact]
    public void TryGetSecret_RequiresExplicitGrantRegardlessOfVaultLockState()
    {
        using var directory = new TemporaryDirectory();
        var settings = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        settings.SetValue(ExtensionSecretAccessPolicy.GetSettingName("ado"), "[\"pat\"]");
        var store = new TrackingSecretStore(("pat", "sensitive"));
        var policy = new ExtensionSecretAccessPolicy(settings, store);

        var result = policy.TryGetSecret("ado", "pat");

        Assert.True(result.Found);
        Assert.Equal("sensitive", result.Value);
        Assert.Equal(["pat"], store.ReadNames);
    }

    private sealed class TrackingSecretStore(params (string Name, string Value)[] secrets) : ISecretStore
    {
        private readonly Dictionary<string, string> _values = secrets.ToDictionary(
            secret => secret.Name,
            secret => secret.Value,
            StringComparer.OrdinalIgnoreCase);

        public List<string> ReadNames { get; } = [];
        public IReadOnlyList<string> GetNames() => _values.Keys.ToArray();
        public bool TryGet(string name, out string value)
        {
            ReadNames.Add(name);
            return _values.TryGetValue(name, out value!);
        }
        public void Set(string name, string value) => _values[name] = value;
        public bool Remove(string name) => _values.Remove(name);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}