using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.Services.Clipboard;

public sealed record ClipboardHistoryEntry(long Id, string Text, DateTimeOffset CapturedAt);

public sealed class ClipboardHistoryService
{
    public const int MaximumEntries = 50;
    public const int MaximumTextLength = 50000;

    private readonly Func<Task<string?>> _readClipboardText;
    private readonly Func<string, Task> _writeClipboardText;
    private readonly Func<bool> _isSensitiveClipboardContent;
    private readonly List<ClipboardHistoryEntry> _entries = [];
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private long _nextEntryId;

    public bool IsEnabled { get; set; }
    public IReadOnlyList<ClipboardHistoryEntry> Entries => _entries;

    public event EventHandler? HistoryChanged;

    public ClipboardHistoryEntry? Find(long id)
        => _entries.FirstOrDefault(entry => entry.Id == id);

    public ClipboardHistoryService(
        Func<Task<string?>> readClipboardText,
        Func<string, Task> writeClipboardText,
        Func<bool> isSensitiveClipboardContent)
    {
        _readClipboardText = readClipboardText;
        _writeClipboardText = writeClipboardText;
        _isSensitiveClipboardContent = isSensitiveClipboardContent;
    }

    public async Task CaptureCurrentClipboardAsync()
    {
        if (!IsEnabled || _isSensitiveClipboardContent())
        {
            return;
        }

        await _captureLock.WaitAsync();
        try
        {
            if (!IsEnabled || _isSensitiveClipboardContent())
            {
                return;
            }

            var text = await _readClipboardText();
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextLength)
            {
                return;
            }

            if (_entries.Count > 0 && string.Equals(_entries[0].Text, text, StringComparison.Ordinal))
            {
                return;
            }

            _entries.RemoveAll(entry => string.Equals(entry.Text, text, StringComparison.Ordinal));
            _entries.Insert(0, new ClipboardHistoryEntry(++_nextEntryId, text, DateTimeOffset.UtcNow));
            if (_entries.Count > MaximumEntries)
            {
                _entries.RemoveRange(MaximumEntries, _entries.Count - MaximumEntries);
            }

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Trace.TraceWarning($"Could not read clipboard history: {exception.Message}");
        }
        finally
        {
            _captureLock.Release();
        }
    }

    public Task CopyToClipboardAsync(string text)
        => _writeClipboardText(text);

    public void Remove(ClipboardHistoryEntry entry)
    {
        if (_entries.Remove(entry))
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _entries.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}