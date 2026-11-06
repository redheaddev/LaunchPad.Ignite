using System.IO;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services.Clipboard;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class ClipboardHistoryTests
{
    [Fact]
    public async Task CaptureIsDisabledByDefaultAndDeduplicatesRecentText()
    {
        string? clipboardText = "first";
        var service = CreateService(() => Task.FromResult<string?>(clipboardText), () => false);

        await service.CaptureCurrentClipboardAsync();
        Assert.Empty(service.Entries);

        service.IsEnabled = true;
        await service.CaptureCurrentClipboardAsync();
        await service.CaptureCurrentClipboardAsync();
        clipboardText = "second";
        await service.CaptureCurrentClipboardAsync();

        Assert.Equal(["second", "first"], service.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public async Task CaptureSkipsSensitiveContentAndOversizedText()
    {
        var isSensitive = true;
        var service = CreateService(
            () => Task.FromResult<string?>(new string('x', ClipboardHistoryService.MaximumTextLength + 1)),
            () => isSensitive);
        service.IsEnabled = true;

        await service.CaptureCurrentClipboardAsync();
        Assert.Empty(service.Entries);

        isSensitive = false;
        await service.CaptureCurrentClipboardAsync();
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task SettingsTogglePersistsAndHistoryCanBeFilteredAndCleared()
    {
        using var directory = new TemporaryDirectory();
        var settingsStore = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        string? clipboardText = "alpha entry";
        var service = CreateService(() => Task.FromResult<string?>(clipboardText), () => false);
        var viewModel = new ClipboardHistorySettingsViewModel(settingsStore, service);

        Assert.False(viewModel.IsEnabled);
        viewModel.IsEnabled = true;
        Assert.Equal("On", settingsStore.GetValue("app:clipboardHistoryEnabled", "Off"));

        await service.CaptureCurrentClipboardAsync();
        Assert.Single(viewModel.VisibleEntries);
        viewModel.SearchText = "missing";
        Assert.Empty(viewModel.VisibleEntries);
        Assert.True(viewModel.HasNoMatches);

        viewModel.ClearCommand.Execute(null);
        Assert.Empty(viewModel.Entries);
        Assert.True(viewModel.HasNoEntries);
        viewModel.Dispose();
    }

    [Fact]
    public void SettingsExposeClipboardAsAnIndependentNavigationItem()
    {
        using var directory = new TemporaryDirectory();
        var settingsStore = new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var extensions = new LoadedExtensions(
            new CommandSchema([], []),
            new Dictionary<string, ICommandExecutor>(),
            new Dictionary<string, ICommandFunctionExecutor>(),
            []);
        var historyService = CreateService(() => Task.FromResult<string?>(null), () => false);
        var historyViewModel = new ClipboardHistorySettingsViewModel(settingsStore, historyService);
        var viewModel = new SettingsViewModel(
            [],
            extensions,
            settingsStore,
            new ExtensionStateStore(Path.Combine(directory.Path, "extensions.json")),
            () => { },
            _ => { },
            clipboardHistory: historyViewModel);

        viewModel.SelectedNavigationItem = viewModel.NavigationItems.Single(item => item.Category == "clipboard");

        Assert.True(viewModel.IsClipboardSelected);
        Assert.False(viewModel.IsSettingsListVisible);
        historyViewModel.Dispose();
        extensions.Unload();
    }

    private static ClipboardHistoryService CreateService(Func<Task<string?>> readClipboardText, Func<bool> isSensitive)
        => new(readClipboardText, _ => Task.CompletedTask, isSensitive);

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory()
            => Directory.CreateDirectory(Path);

        public void Dispose()
            => Directory.Delete(Path, recursive: true);
    }
}