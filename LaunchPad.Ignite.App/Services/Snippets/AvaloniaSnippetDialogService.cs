using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Views;

namespace LaunchPad.Ignite.App.Services.Snippets;

public sealed class AvaloniaSnippetDialogService : ISnippetDialogService
{
    public Task<SnippetEditorResult?> ShowEditorAsync(
        string name,
        SnippetDefinition? snippet,
        IReadOnlyList<string> availableSecrets)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
        {
            return Task.FromResult<SnippetEditorResult?>(null);
        }

        var dialog = new SnippetEditorWindow(name, snippet, availableSecrets);
        return owner is MainWindow mainWindow
            ? mainWindow.ShowSnippetEditorAsync(dialog)
            : dialog.ShowDialog<SnippetEditorResult?>(owner);
    }

    public Task<bool> ConfirmRemoveAsync(string name)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not MainWindow owner)
        {
            return Task.FromResult(false);
        }

        return owner.ConfirmSnippetRemovalAsync(name);
    }

    public Task<IReadOnlyDictionary<string, string>?> ShowArgumentsAsync(string name, IReadOnlyList<string> arguments)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(null);
        }

        var dialog = new SnippetArgumentsWindow(name, arguments);
        return owner is MainWindow mainWindow
            ? mainWindow.ShowSnippetArgumentsAsync(dialog)
            : dialog.ShowDialog<IReadOnlyDictionary<string, string>?>(owner);
    }
}
