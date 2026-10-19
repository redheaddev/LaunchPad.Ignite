using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia;
using Avalonia.Input;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.Core.Schema;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        AddHandler(KeyDownEvent, CaptureGlobalActivationHotkey, RoutingStrategies.Tunnel);
        EscapeToClose.Attach(this);
        Closed += (_, _) => ColorThemeService.ThemeChanged -= RefreshThemeResources;
        ColorThemeService.ThemeChanged += RefreshThemeResources;
    }

    private void CaptureMediaShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: MediaShortcutEditorViewModel editor })
        {
            editor.BeginCapture();
            Activate();
            Focus();
        }
    }

    private void CaptureCommandShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: CommandDetailsViewModel editor })
        {
            editor.BeginShortcutCapture();
            Activate();
            Focus();
        }
    }

    private void CaptureCommandActionShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: CommandActionEditorViewModel editor })
        {
            editor.BeginShortcutCapture();
            Activate();
            Focus();
        }
    }

    private void ChangeHotkey_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.BeginGlobalActivationHotkeyCapture();
            Activate();
            Focus();
        }
    }

    private void CaptureGlobalActivationHotkey(object? sender, KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel settingsViewModel)
        {
            var capturingMediaEditor = settingsViewModel.MediaShortcutEditors.FirstOrDefault(editor => editor.IsCapturing);
            if (capturingMediaEditor is not null)
            {
                if (e.Key == Key.Escape)
                {
                    capturingMediaEditor.CancelCapture();
                    e.Handled = true;
                    return;
                }

                if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                {
                    e.Handled = true;
                    return;
                }

                var keyName = GetShortcutChordKey(e.Key);
                if (keyName is not null)
                {
                    var mediaModifiers = e.KeyModifiers;
                    capturingMediaEditor.Capture(
                        mediaModifiers.HasFlag(KeyModifiers.Control),
                        mediaModifiers.HasFlag(KeyModifiers.Alt),
                        mediaModifiers.HasFlag(KeyModifiers.Shift),
                        mediaModifiers.HasFlag(KeyModifiers.Meta),
                        keyName);
                    e.Handled = true;
                }
                return;
            }

            var capturingCommandEditor = settingsViewModel.CommandDetails.FirstOrDefault(editor => editor.IsCapturingShortcut);
            if (capturingCommandEditor is not null)
            {
                if (e.Key == Key.Escape)
                {
                    capturingCommandEditor.CancelShortcutCapture();
                    e.Handled = true;
                    return;
                }

                if (IsModifierKey(e.Key))
                {
                    e.Handled = true;
                    return;
                }

                var chord = CreateShortcutChord(e);
                if (chord is not null)
                {
                    capturingCommandEditor.CaptureShortcut(chord);
                    e.Handled = true;
                }
                return;
            }

            var capturingActionEditor = settingsViewModel.CommandDetails
                .SelectMany(editor => editor.Actions)
                .FirstOrDefault(editor => editor.IsCapturingShortcut);
            if (capturingActionEditor is not null)
            {
                if (e.Key == Key.Escape)
                {
                    capturingActionEditor.CancelShortcutCapture();
                    e.Handled = true;
                    return;
                }

                if (IsModifierKey(e.Key))
                {
                    e.Handled = true;
                    return;
                }

                var chord = CreateShortcutChord(e);
                if (chord is not null)
                {
                    capturingActionEditor.CaptureShortcut(chord);
                    e.Handled = true;
                }
                return;
            }
        }

        if (DataContext is not SettingsViewModel viewModel || !viewModel.IsCapturingGlobalActivationHotkey)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.CancelGlobalActivationHotkeyCapture();
            e.Handled = true;
            return;
        }

        var modifiers = e.KeyModifiers;
        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        var hookKeyName = GetHookKeyName(e.Key);
        if (!string.IsNullOrEmpty(hookKeyName))
        {
            viewModel.TrySetGlobalActivationHotkey(
                modifiers.HasFlag(KeyModifiers.Control),
                modifiers.HasFlag(KeyModifiers.Alt),
                modifiers.HasFlag(KeyModifiers.Shift),
                modifiers.HasFlag(KeyModifiers.Meta),
                hookKeyName);
            e.Handled = true;
        }
    }

    private static bool IsModifierKey(Key key)
        => key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string? CreateShortcutChord(KeyEventArgs e)
    {
        var key = GetShortcutChordKey(e.Key);
        if (key is null)
        {
            return null;
        }

        var parts = new System.Collections.Generic.List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("meta");
        parts.Add(key);
        var chord = string.Join('+', parts);
        return KeyboardShortcutChord.TryParse(chord, out _) ? chord : null;
    }

    private static string? GetShortcutChordKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString().ToLowerInvariant();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return key.ToString()[1..];
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"num+{(int)key - (int)Key.NumPad0}";
        }

        return key == Key.Space ? "space" : null;
    }

    private static string GetHookKeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => key.ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => key.ToString(),
        Key.Space => "Space",
        Key.Enter => "Enter",
        Key.Tab => "Tab",
        Key.Escape => "Escape",
        Key.Back => "Backspace",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.OemMinus => "OemMinus",
        Key.OemPlus => "OemPlus",
        Key.OemOpenBrackets => "OemOpenBrackets",
        Key.OemCloseBrackets => "OemCloseBrackets",
        Key.OemPipe => "OemPipe",
        Key.OemSemicolon => "OemSemicolon",
        Key.OemQuotes => "OemQuotes",
        Key.OemComma => "OemComma",
        Key.OemPeriod => "OemPeriod",
        Key.OemQuestion => "OemQuestion",
        _ => string.Empty
    };

    private void OpenThemeReference_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel
            && Uri.TryCreate(viewModel.SelectedColorThemeUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void SaveThemeReference_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SaveThemeReferenceUrl();
        }
    }

    private void CreateTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CreateTheme(viewModel.NewThemeName);
        }
    }

    private void RenameTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.RenameSelectedColorTheme(viewModel.RenameThemeName);
        }
    }

    private void DeleteTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.DeleteSelectedColorTheme();
        }
    }

    private async void ImportTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            await viewModel.ImportThemeColorsAsync(viewModel.ThemeImportUrl);
        }
    }

    private void ThemeColorHex_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is TextBox textBox && textBox.Tag is string property)
        {
            viewModel.SetThemeColor(property, textBox.Text);
        }
    }

    private void SaveThemeColors_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SaveThemeColorChanges();
        }
    }

    private void ExtensionEnabled_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SetExtensionEnabled((sender as CheckBox)?.IsChecked == true);
        }
    }

    private void ColorTheme_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ColorTheme theme)
            {
                viewModel.SelectColorTheme(theme.Id);
                viewModel.RefreshSelectedColorTheme();
            }
        }
    }

    private void RefreshThemeResources()
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.RefreshSelectedColorTheme();
        }
    }
}
