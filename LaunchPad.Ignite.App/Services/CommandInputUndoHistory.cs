using System;
using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services;

public sealed class CommandInputUndoHistory
{
    private readonly Stack<(string Text, int CaretIndex)> _states = new();
    private bool _sessionEnded;

    public bool CanUndo => _states.Count > 0;

    public void RecordUserChange(string previousText, string currentText, int previousCaretIndex = 0)
    {
        _sessionEnded = false;
        RecordChange(previousText, currentText, previousCaretIndex);
    }

    public void RecordProgrammaticChange(string previousText, string currentText, int previousCaretIndex = 0)
    {
        if (!_sessionEnded)
        {
            RecordChange(previousText, currentText, previousCaretIndex);
        }
    }

    public bool TryUndo(out string text)
        => TryUndo(out text, out _);

    public bool TryUndo(out string text, out int caretIndex)
    {
        if (!CanUndo)
        {
            text = string.Empty;
            caretIndex = 0;
            return false;
        }

        var state = _states.Pop();
        text = state.Text;
        caretIndex = state.CaretIndex;
        return true;
    }

    public void EndSession()
    {
        _states.Clear();
        _sessionEnded = true;
    }

    private void RecordChange(string previousText, string currentText, int previousCaretIndex)
    {
        if (string.Equals(previousText, currentText, StringComparison.Ordinal))
        {
            return;
        }

        _states.Push((previousText, Math.Clamp(previousCaretIndex, 0, previousText.Length)));
    }
}
