using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class SettingEditorViewModelTests
{
    [Fact]
    public void ImmediateSavePersistsAndAppliesValueChanges()
    {
        using var directory = new TemporaryDirectory();
        var settingsStore = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var settingsChangedCount = 0;
        var definition = new CommandSettingDefinition(
            "keepWindowOpen",
            "Keep window open",
            "",
            "Off",
            "select",
            Namespace: "app",
            Options: ["Off", "On"]);
        var editor = new SettingEditorViewModel(
            definition,
            "Off",
            settingsStore,
            () => settingsChangedCount++);

        editor.Value = "On";

        Assert.Equal("On", settingsStore.GetValue("app:keepWindowOpen", "Off"));
        Assert.Equal(1, settingsChangedCount);
        Assert.False(editor.IsSaveButtonVisible);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
