using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Services;
using CommunityToolkit.Mvvm.Input;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.ViewModels;

public sealed partial class MediaShortcutEditorViewModel : ViewModelBase
{
    private readonly ProfileSettingsStore _settingsStore;
    private readonly IReadOnlyList<CommandDefinition> _commands;
    private readonly Action _settingsChanged;
    private string _shortcut;
    private string _status = string.Empty;
    private bool _isCapturing;

    public string ActionName { get; }
    public string Label { get; }
    public string Shortcut
    {
        get => _shortcut;
        set => SetProperty(ref _shortcut, value);
    }
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }
    public bool IsCapturing
    {
        get => _isCapturing;
        private set => SetProperty(ref _isCapturing, value);
    }

    public MediaShortcutEditorViewModel(
        string actionName,
        string label,
        string? defaultShortcut,
        IReadOnlyList<CommandDefinition> commands,
        ProfileSettingsStore settingsStore,
        Action settingsChanged)
    {
        ActionName = actionName;
        Label = label;
        _commands = commands;
        _settingsStore = settingsStore;
        _settingsChanged = settingsChanged;
        _shortcut = MediaShortcutSettings.GetValue(actionName, defaultShortcut, settingsStore.GetValues());
    }

    public void BeginCapture()
    {
        IsCapturing = true;
        Status = "Press a modifier chord and key. Press Esc to cancel.";
    }

    public void CancelCapture()
    {
        IsCapturing = false;
        Status = "Capture canceled.";
    }

    public bool Capture(bool control, bool alt, bool shift, bool meta, string key)
    {
        IsCapturing = false;
        var chord = BuildChord(control, alt, shift, meta, key);
        return Save(chord);
    }

    [RelayCommand]
    private void SaveShortcut()
    {
        Save(Shortcut);
    }

    [RelayCommand]
    private void ResetShortcut()
    {
        _settingsStore.RemoveValue(MediaShortcutSettings.GetSettingName(ActionName));
        _shortcut = GetDefaultShortcut();
        OnPropertyChanged(nameof(Shortcut));
        Status = "Restored the default shortcut.";
        _settingsChanged();
    }

    private bool Save(string? chord)
    {
        var normalized = chord?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length > 0 && !MediaShortcutSettings.IsAvailable(normalized, _commands, ActionName, _settingsStore.GetValues()))
        {
            Status = "Enter a valid, unused chord (Ctrl/Alt/Shift/Meta plus a letter, number, or numpad key).";
            return false;
        }

        if (normalized.Length == 0)
        {
            _settingsStore.RemoveValue(MediaShortcutSettings.GetSettingName(ActionName));
            _shortcut = GetDefaultShortcut();
            OnPropertyChanged(nameof(Shortcut));
            Status = "Restored the default shortcut.";
        }
        else
        {
            _settingsStore.SetValue(MediaShortcutSettings.GetSettingName(ActionName), normalized);
            _shortcut = normalized;
            OnPropertyChanged(nameof(Shortcut));
            Status = $"Saved {normalized}.";
        }

        _settingsChanged();
        return true;
    }

    private string GetDefaultShortcut()
        => MediaShortcutSettings.GetDefaultShortcut(ActionName);

    private static string BuildChord(bool control, bool alt, bool shift, bool meta, string key)
        => string.Join('+', new[]
        {
            control ? "ctrl" : null,
            alt ? "alt" : null,
            shift ? "shift" : null,
            meta ? "meta" : null,
            key
        }.Where(part => part is not null));
}
