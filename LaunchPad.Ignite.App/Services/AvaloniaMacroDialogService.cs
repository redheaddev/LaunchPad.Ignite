using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Views;

namespace LaunchPad.Ignite.App.Services;

public sealed class AvaloniaMacroDialogService : IMacroDialogService
{
    public Task<MacroEditorResult?> ShowEditorAsync(string qualifiedName, MacroDefinition? macro)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
        {
            return Task.FromResult<MacroEditorResult?>(null);
        }

        var dialog = new MacroEditorWindow(qualifiedName, macro);
        return owner is MainWindow mainWindow
            ? mainWindow.ShowMacroEditorAsync(dialog)
            : dialog.ShowDialog<MacroEditorResult?>(owner);
    }

    public Task<bool> ConfirmRemoveAsync(string qualifiedName)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not MainWindow owner)
        {
            return Task.FromResult(false);
        }

        return owner.ConfirmMacroRemovalAsync(qualifiedName);
    }
}
