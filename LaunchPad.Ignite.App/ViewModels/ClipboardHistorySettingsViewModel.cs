using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchPad.Ignite.App.Services.Clipboard;
using LaunchPad.Ignite.App.Services.Profiles;

namespace LaunchPad.Ignite.App.ViewModels;

public sealed record ClipboardHistoryEntryViewModel(ClipboardHistoryEntry Entry)
{
    public string IdDisplay => Entry.Id.ToString(CultureInfo.InvariantCulture);

    public string Preview
    {
        get
        {
            var preview = Entry.Text.Replace('\r', ' ').Replace('\n', ' ');
            return preview.Length <= 320 ? preview : $"{preview[..320]}...";
        }
    }

    public string CapturedAtDisplay => Entry.CapturedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}

public sealed partial class ClipboardHistorySettingsViewModel : ObservableObject, IDisposable
{
    private const string EnabledSettingName = "app:clipboardHistoryEnabled";

    private readonly ProfileSettingsStore _settingsStore;
    private readonly ClipboardHistoryService _clipboardHistory;
    private bool _isEnabled;
    private string _searchText = string.Empty;
    private string _status = string.Empty;

    public ObservableCollection<ClipboardHistoryEntryViewModel> Entries { get; } = [];

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _clipboardHistory.IsEnabled = value;
                _settingsStore.SetValue(EnabledSettingName, value ? "On" : "Off");
                Status = value ? "Clipboard history enabled." : "Clipboard history paused.";
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(VisibleEntries));
                OnPropertyChanged(nameof(HasNoMatches));
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public IReadOnlyList<ClipboardHistoryEntryViewModel> VisibleEntries
        => Entries.Where(entry => string.IsNullOrWhiteSpace(SearchText)
            || entry.Entry.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToArray();

    public bool HasEntries => Entries.Count > 0;
    public bool HasNoEntries => Entries.Count == 0;
    public bool HasNoMatches => HasEntries && VisibleEntries.Count == 0;

    public ClipboardHistorySettingsViewModel(ProfileSettingsStore settingsStore, ClipboardHistoryService clipboardHistory)
    {
        _settingsStore = settingsStore;
        _clipboardHistory = clipboardHistory;
        _isEnabled = string.Equals(settingsStore.GetValue(EnabledSettingName, "Off"), "On", StringComparison.OrdinalIgnoreCase);
        _clipboardHistory.IsEnabled = _isEnabled;
        _clipboardHistory.HistoryChanged += ClipboardHistory_HistoryChanged;
        RefreshEntries();
    }

    [RelayCommand]
    private async Task CopyAsync(ClipboardHistoryEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            await _clipboardHistory.CopyToClipboardAsync(entry.Entry.Text);
            Status = "Copied to clipboard.";
        }
        catch (Exception exception)
        {
            Status = $"Could not copy clipboard entry: {exception.Message}";
        }
    }

    [RelayCommand]
    private void Remove(ClipboardHistoryEntryViewModel? entry)
    {
        if (entry is not null)
        {
            _clipboardHistory.Remove(entry.Entry);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _clipboardHistory.Clear();
        Status = "Clipboard history cleared.";
    }

    public void Dispose()
        => _clipboardHistory.HistoryChanged -= ClipboardHistory_HistoryChanged;

    private void ClipboardHistory_HistoryChanged(object? sender, EventArgs eventArgs)
        => RefreshEntries();

    private void RefreshEntries()
    {
        Entries.Clear();
        foreach (var entry in _clipboardHistory.Entries)
        {
            Entries.Add(new ClipboardHistoryEntryViewModel(entry));
        }

        OnPropertyChanged(nameof(VisibleEntries));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(HasNoMatches));
    }
}