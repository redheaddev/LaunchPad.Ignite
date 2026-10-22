using System;
using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services.Completion;

public sealed class CommandInputUndoHistory
{
    private readonly Stack<(string Text, int CaretIndex)> _undoStates = new();
    private readonly Stack<(string Text, int CaretIndex)> _redoStates = new();
    private bool _sessionEnded;

    public bool CanUndo => _undoStates.Count > 0;

    public bool CanRedo => _redoStates.Count > 0;

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

    public bool TryUndo(string currentText, int currentCaretIndex, out string text, out int caretIndex)
    {
        if (!CanUndo)
        {
            text = string.Empty;
            caretIndex = 0;
            return false;
        }

        _redoStates.Push((currentText, Math.Clamp(currentCaretIndex, 0, currentText.Length)));
        var state = _undoStates.Pop();
        text = state.Text;
        caretIndex = state.CaretIndex;
        return true;
    }

    public bool TryRedo(string currentText, int currentCaretIndex, out string text, out int caretIndex)
    {
        if (!CanRedo)
        {
            text = string.Empty;
            caretIndex = 0;
            return false;
        }

        _undoStates.Push((currentText, Math.Clamp(currentCaretIndex, 0, currentText.Length)));
        var state = _redoStates.Pop();
        text = state.Text;
        caretIndex = state.CaretIndex;
        return true;
    }

    public void EndSession()
    {
        _undoStates.Clear();
        _redoStates.Clear();
        _sessionEnded = true;
    }

    private void RecordChange(string previousText, string currentText, int previousCaretIndex)
    {
        if (string.Equals(previousText, currentText, StringComparison.Ordinal))
        {
            return;
        }

        _redoStates.Clear();
        _undoStates.Push((previousText, Math.Clamp(previousCaretIndex, 0, previousText.Length)));
    }
}
