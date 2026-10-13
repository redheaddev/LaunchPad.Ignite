using System;
using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services;

public sealed class HistorySuggestionCycler
{
    private IReadOnlyList<CommandHistoryEntry> _entries = Array.Empty<CommandHistoryEntry>();
    private string? _inputText;
    private string? _prefix;
    private int _selectedIndex = -1;
    private bool _isCycling;

    public int SelectedIndex => _selectedIndex;

    public bool TryGetSuggestions(string inputText, out IReadOnlyList<CommandHistoryEntry> entries)
    {
        if (_isCycling && string.Equals(_inputText, inputText, StringComparison.Ordinal))
        {
            entries = [_entries[_selectedIndex]];
            return true;
        }

        entries = Array.Empty<CommandHistoryEntry>();
        return false;
    }

    public bool TryCycle(
        IReadOnlyList<CommandHistoryEntry> historyEntries,
        string inputText,
        string prefix,
        bool older,
        out CommandHistoryEntry selectedEntry)
    {
        if (!_isCycling
            || !string.Equals(_inputText, inputText, StringComparison.Ordinal)
            || !string.Equals(_prefix, prefix, StringComparison.Ordinal))
        {
            _entries = ProfileCommandLogger.FilterHistoryEntriesByPrefix(historyEntries, prefix);
            if (_entries.Count == 0)
            {
                Reset();
                selectedEntry = null!;
                return false;
            }

            _inputText = inputText;
            _prefix = prefix;
            _selectedIndex = older ? 0 : _entries.Count - 1;
            _isCycling = true;
        }
        else
        {
            _selectedIndex = older
                ? (_selectedIndex + 1) % _entries.Count
                : (_selectedIndex - 1 + _entries.Count) % _entries.Count;
        }

        selectedEntry = _entries[_selectedIndex];
        return true;
    }

    public bool TrySelectBoundary(
        IReadOnlyList<CommandHistoryEntry> historyEntries,
        string inputText,
        string prefix,
        bool newest,
        out CommandHistoryEntry selectedEntry)
    {
        if (!_isCycling
            || !string.Equals(_inputText, inputText, StringComparison.Ordinal)
            || !string.Equals(_prefix, prefix, StringComparison.Ordinal))
        {
            _entries = ProfileCommandLogger.FilterHistoryEntriesByPrefix(historyEntries, prefix);
            if (_entries.Count == 0)
            {
                Reset();
                selectedEntry = null!;
                return false;
            }

            _inputText = inputText;
            _prefix = prefix;
            _isCycling = true;
        }

        _selectedIndex = newest ? 0 : _entries.Count - 1;
        selectedEntry = _entries[_selectedIndex];
        return true;
    }

    public bool TryRetainSelection(
        IReadOnlyList<CommandHistoryEntry> historyEntries,
        string inputText,
        string prefix,
        CommandHistoryEntry selectedEntry)
    {
        if (!selectedEntry.CommandText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            Reset();
            return false;
        }

        _entries = ProfileCommandLogger.FilterHistoryEntriesByPrefix(historyEntries, prefix);
        _selectedIndex = -1;
        for (var index = 0; index < _entries.Count; index++)
        {
            if (_entries[index] == selectedEntry)
            {
                _selectedIndex = index;
                break;
            }
        }

        if (_selectedIndex < 0)
        {
            Reset();
            return false;
        }

        _inputText = inputText;
        _prefix = prefix;
        _isCycling = true;
        return true;
    }

    public void Reset()
    {
        _entries = Array.Empty<CommandHistoryEntry>();
        _inputText = null;
        _prefix = null;
        _selectedIndex = -1;
        _isCycling = false;
    }
}
