using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Completion;

public sealed class CommandCompletionSession
{
    private const int PageSize = 5;
    private readonly CommandCatalog _catalog;
    private readonly IArgumentValueProvider _valueProvider;
    private readonly CommandCompletionResolver _resolver;
    private string _inputText = string.Empty;
    private CompletionResolution _resolution;
    private List<string> _selectedValues = [];
    private string _listFilter = string.Empty;
    private int _listFilterStart;
    private string? _preferredHighlightValue;
    private int _highlightedIndex;
    private int? _caretIndex;

    public CommandCompletionSession(
        CommandCatalog catalog,
        IArgumentValueProvider valueProvider,
        IEnumerable<CommandVariableDefinition>? variables = null,
        IEnumerable<CommandFunctionDefinition>? functions = null)
    {
        _catalog = catalog;
        _valueProvider = valueProvider;
        _resolver = new CommandCompletionResolver(catalog, valueProvider, variables, functions);
        _resolution = _resolver.Resolve(_inputText);
        State = BuildState();
    }

    public CommandCompletionState State { get; private set; }

    public CommandCompletionState UpdateVariables(IEnumerable<CommandVariableDefinition> variables)
    {
        _resolver.UpdateVariables(variables);
        Recalculate(resetSelection: true);
        return State;
    }

    public CommandCompletionState UpdateInputText(string inputText)
    {
        if (string.Equals(_inputText, inputText, StringComparison.Ordinal))
        {
            return State;
        }

        _inputText = inputText;
        _caretIndex = null;
        if (ShouldOpenListImmediately(_inputText))
        {
            _inputText += ' ';
        }

        _preferredHighlightValue = null;
        Recalculate(resetSelection: true);
        return State;
    }

    private bool ShouldOpenListImmediately(string inputText)
    {
        return string.Equals(inputText, "*", StringComparison.Ordinal)
            && _catalog.TryGetCommand("*", out var command)
            && _catalog.TryGetDefaultAction(command, out var action)
            && action.Arguments.Any(argument => argument.Ref?.StartsWith("list:", StringComparison.OrdinalIgnoreCase) == true);
    }

    public CommandCompletionState HandleKey(CommandInputKey key)
    {
        _caretIndex = null;
        switch (key)
        {
            case CommandInputKey.Tab:
                AcceptOrToggle();
                break;
            case CommandInputKey.Right:
                AcceptToken();
                break;
            case CommandInputKey.Space:
                ExitChecklist();
                break;
            case CommandInputKey.Up:
                MoveHighlight(-1);
                break;
            case CommandInputKey.Down:
                MoveHighlight(1);
                break;
            case CommandInputKey.PageUp:
                MoveHighlight(-PageSize);
                break;
            case CommandInputKey.PageDown:
                MoveHighlight(PageSize);
                break;
            case CommandInputKey.First:
                MoveHighlightToBoundary(last: false);
                break;
            case CommandInputKey.Last:
                MoveHighlightToBoundary(last: true);
                break;
            case CommandInputKey.SelectAll:
                SelectAll();
                break;
            case CommandInputKey.DeselectAll:
                DeselectAll();
                break;
            case CommandInputKey.Escape:
            case CommandInputKey.Clear:
                _inputText = string.Empty;
                _preferredHighlightValue = null;
                Recalculate(resetSelection: true);
                break;
            case CommandInputKey.Enter:
                break;
        }

        return State;
    }

    private void AcceptOrToggle()
    {
        if (_resolution.AwaitingSeparator)
        {
            _inputText += ' ';
            _preferredHighlightValue = null;
            Recalculate(resetSelection: true);
            return;
        }

        var items = GetVisibleItems();
        if (items.Count == 0 || _highlightedIndex < 0 || _highlightedIndex >= items.Count)
        {
            return;
        }

        var selectedItem = items[_highlightedIndex].Value;
        if (_resolution.IsChecklist)
        {
            if (_resolution.ListMode == ListSelectionMode.Single)
            {
                if (!_selectedValues.Contains(selectedItem, StringComparer.OrdinalIgnoreCase))
                {
                    _selectedValues.Add(selectedItem);
                }

                _preferredHighlightValue = GetVisibleListValues()
                    .FirstOrDefault(value => !_selectedValues.Contains(value, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                var selectedIndex = _selectedValues.FindIndex(value =>
                    string.Equals(value, selectedItem, StringComparison.OrdinalIgnoreCase));
                if (selectedIndex >= 0)
                {
                    _selectedValues.RemoveAt(selectedIndex);
                }
                else
                {
                    _selectedValues.Add(selectedItem);
                }

                _preferredHighlightValue = GetVisibleListValues()
                    .FirstOrDefault(value => !_selectedValues.Contains(value, StringComparer.OrdinalIgnoreCase));
            }

            ReplaceCurrentArgument(string.Join(',', _selectedValues.Select(FormatListValue)));
            Recalculate(resetSelection: false);
            return;
        }

        var appendSpace = ShouldAppendSpace(selectedItem);
        _inputText = _inputText[.._resolution.TokenStart] + FormatListValue(selectedItem) + (appendSpace ? " " : string.Empty);
        _preferredHighlightValue = null;
        Recalculate(resetSelection: true);
    }

    private void AcceptToken()
    {
        if (_resolution.AwaitingSeparator)
        {
            _inputText += ' ';
            _preferredHighlightValue = null;
            Recalculate(resetSelection: true);
            return;
        }

        var items = GetVisibleItems();
        var selectedValue = _resolution.IsChecklist && _resolution.ListMode == ListSelectionMode.Multiple && _listFilter.Length == 0
            ? items.FirstOrDefault(item => !_selectedValues.Contains(item.Value, StringComparer.OrdinalIgnoreCase))?.Value
            : items.Count > 0 && _highlightedIndex >= 0 && _highlightedIndex < items.Count
                ? items[_highlightedIndex].Value
                : null;
        if (selectedValue is null)
        {
            if (_resolution.IsChecklist
                && _resolution.ListMode == ListSelectionMode.Multiple
                && _listFilter.Length == 0
                && _selectedValues.Count == _valueProvider.GetValues(_resolution.Argument!.Ref!).Count)
            {
                _caretIndex = _inputText.Length;
                State = BuildState();
            }

            return;
        }

        var selectedItem = FormatListValue(selectedValue);
        var replacementStart = _resolution.TokenStart;
        var tokenEnd = FindTokenEnd(_inputText, replacementStart);
        var replacement = selectedItem;
        if (_resolution.IsChecklist && _resolution.ListMode == ListSelectionMode.Multiple)
        {
            var argumentEnd = FindArgumentEnd(_inputText, _resolution.TokenStart);
            tokenEnd = argumentEnd;
            if (_listFilter.Length == 0 && _selectedValues.Count > 0)
            {
                var separator = argumentEnd > _resolution.TokenStart && _inputText[argumentEnd - 1] == ','
                    ? string.Empty
                    : ",";
                replacementStart = argumentEnd;
                replacement = separator + selectedItem;
            }
            else
            {
                replacementStart = _listFilterStart;
                tokenEnd = FindTokenEnd(_inputText, replacementStart);
            }

            if (selectedValue.Any(char.IsWhiteSpace)
                && replacementStart == _resolution.TokenStart
                && tokenEnd == argumentEnd)
            {
                replacement = string.Join(',', _selectedValues.Select(FormatListValue).Append(selectedItem));
                replacementStart = _resolution.TokenStart;
            }

        }

        var suffix = _inputText[tokenEnd..];
        if (suffix.Length == 0 && !_resolution.IsChecklist && ShouldAppendSpace(selectedValue))
        {
            suffix = " ";
        }

        _inputText = _inputText[..replacementStart] + replacement + suffix;
        var acceptedEnd = replacementStart + replacement.Length;
        if (suffix.StartsWith(' '))
        {
            acceptedEnd++;
        }

        _preferredHighlightValue = null;
        Recalculate(resetSelection: true);
        _caretIndex = acceptedEnd;
        State = BuildState();
    }

    private static int FindTokenEnd(string input, int tokenStart)
    {
        return new LaunchPad.Ignite.Core.Parsing.CommandLineTokenizer().FindTokenBoundary(input, tokenStart);
    }

    private static int FindArgumentEnd(string input, int argumentStart)
    {
        return new LaunchPad.Ignite.Core.Parsing.CommandLineTokenizer()
            .FindTokenBoundary(input, argumentStart, commaIsBoundary: false);
    }

    private static string FormatListValue(string value)
        => value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private bool ShouldAppendSpace(string candidate)
    {
        if (_resolution.Kind == CompletionKind.Argument)
        {
            return _resolution.AppendSpaceOnAccept;
        }

        if (_resolution.Kind == CompletionKind.Command
            && _catalog.TryGetCommand(candidate, out var command))
        {
            if (_catalog.TryGetDefaultAction(command, out var defaultAction))
            {
                return defaultAction.Arguments.Any(argument => !argument.Optional);
            }

            return command.Actions.Count > 0;
        }

        if (_resolution.Kind == CompletionKind.Action
            && _catalog.Commands.SelectMany(command => command.Actions).FirstOrDefault(action =>
                string.Equals(action.Name, candidate, StringComparison.OrdinalIgnoreCase)) is { } action)
        {
            return action.Arguments.Any(argument => !argument.Optional);
        }

        return false;
    }

    private void ExitChecklist()
    {
        if (!_resolution.IsChecklist || _selectedValues.Count == 0 || !_resolution.HasNextArgument)
        {
            return;
        }

        if (!_inputText.EndsWith(' '))
        {
            _inputText += ' ';
        }

        _preferredHighlightValue = null;
        Recalculate(resetSelection: true);
    }

    private void MoveHighlight(int offset)
    {
        var items = GetVisibleItems();
        if (items.Count == 0)
        {
            _highlightedIndex = -1;
        }
        else
        {
            _highlightedIndex = (_highlightedIndex + offset + items.Count) % items.Count;
        }

        State = BuildState();
    }

    private void MoveHighlightToBoundary(bool last)
    {
        var items = GetVisibleItems();
        _highlightedIndex = items.Count == 0 ? -1 : last ? items.Count - 1 : 0;
        State = BuildState();
    }

    private void SelectAll()
    {
        if (!_resolution.IsChecklist || _resolution.ListMode == ListSelectionMode.Single)
        {
            return;
        }

        _selectedValues = _valueProvider.GetValues(_resolution.Argument!.Ref!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        ReplaceCurrentArgument(string.Join(',', _selectedValues));
        Recalculate(resetSelection: false);
    }

    private void DeselectAll()
    {
        if (!_resolution.IsChecklist || _resolution.ListMode == ListSelectionMode.Single)
        {
            return;
        }

        _selectedValues.Clear();
        ReplaceCurrentArgument(string.Empty);
        Recalculate(resetSelection: false);
    }

    private void ReplaceCurrentArgument(string value)
    {
        _inputText = _inputText[.._resolution.TokenStart] + value;
    }

    private void Recalculate(bool resetSelection)
    {
        _resolution = _resolver.Resolve(_inputText);
        if (resetSelection || !_resolution.IsChecklist)
        {
            _selectedValues = [];
            _listFilter = string.Empty;
            _highlightedIndex = 0;
        }

        if (_resolution.IsChecklist)
        {
            ReadListInput();
        }

        var visibleValues = _resolution.IsChecklist
            ? GetVisibleListValues()
            : _resolution.Candidates.ToList();
        if (_preferredHighlightValue is not null)
        {
            var preferredIndex = visibleValues.FindIndex(value =>
                string.Equals(value, _preferredHighlightValue, StringComparison.OrdinalIgnoreCase));
            if (preferredIndex >= 0)
            {
                _highlightedIndex = preferredIndex;
            }
        }

        if (visibleValues.Count == 0)
        {
            _highlightedIndex = -1;
        }
        else if (_highlightedIndex < 0 || _highlightedIndex >= visibleValues.Count)
        {
            _highlightedIndex = 0;
        }

        State = BuildState();
    }

    private void ReadListInput()
    {
        var token = _resolution.Prefix;
        var values = _valueProvider.GetValues(_resolution.Argument!.Ref!);
        var parts = token.Split(',');
        var rawArgumentEnd = FindArgumentEnd(_inputText, _resolution.TokenStart);
        var rawParts = _resolution.TokenStart < rawArgumentEnd
            ? _inputText[_resolution.TokenStart..rawArgumentEnd].Split(',')
            : [];
        _selectedValues = [];
        _listFilter = string.Empty;
        _listFilterStart = _resolution.TokenStart;

        var partsToRead = parts.Length;
        if (parts.Length > 0 && parts[^1].Length == 0)
        {
            partsToRead--;
        }

        for (var index = 0; index < partsToRead; index++)
        {
            var value = parts[index];
            var isLastPart = index == parts.Length - 1;
            if (values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                _selectedValues.Add(value);
            }
            else if (isLastPart || _resolution.ListMode == ListSelectionMode.Multiple)
            {
                _listFilter = value;
                _listFilterStart = _resolution.TokenStart + rawParts.Take(index).Sum(part => part.Length + 1);
            }
            else
            {
                _listFilter = value;
                _listFilterStart = _resolution.TokenStart + rawParts.Take(index).Sum(part => part.Length + 1);
            }
        }

        if (partsToRead < parts.Length && parts[^1].Length == 0)
        {
            _listFilter = string.Empty;
        }
    }

    private List<string> GetVisibleListValues()
    {
        if (!_resolution.IsChecklist)
        {
            return [];
        }

        if (_resolution.ListMode == ListSelectionMode.Single && _selectedValues.Count > 0)
        {
            return [];
        }

        var values = _valueProvider.GetValues(_resolution.Argument!.Ref!);
        return values
            .Where(value => value.StartsWith(_listFilter, StringComparison.OrdinalIgnoreCase))
            .Where(value => _resolution.ListMode != ListSelectionMode.Single
                || !_selectedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private List<CompletionItem> GetVisibleItems()
    {
        if (_resolution.IsChecklist)
        {
            return GetVisibleListValues()
                .Select((value, index) => new CompletionItem(
                    value,
                    _selectedValues.Contains(value, StringComparer.OrdinalIgnoreCase),
                    index == _highlightedIndex))
                .ToList();
        }

        return _resolution.Candidates
            .Select((value, index) => new CompletionItem(
                value,
                false,
                index == _highlightedIndex,
                _resolution.CandidateDisplayValues is { } displayValues && index < displayValues.Count
                    ? displayValues[index]
                    : null))
            .ToList();
    }

    private CommandCompletionState BuildState()
    {
        var items = GetVisibleItems();
        var highlighted = items.FirstOrDefault(item => item.IsHighlighted);
        var prefix = _resolution.IsChecklist ? _listFilter : _resolution.Prefix;
        if (_resolution.IsChecklist && _resolution.ListMode == ListSelectionMode.Single)
        {
            items = items
                .Where(item => !string.Equals(item.Value, prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            highlighted = items.FirstOrDefault(item => item.IsHighlighted)
                ?? items.FirstOrDefault();
        }

        var ghostText = string.Empty;
        var ghostCandidate = _resolution.IsChecklist && _resolution.ListMode == ListSelectionMode.Multiple
            ? items.FirstOrDefault(item => !item.IsSelected)
            : highlighted;
        if (ghostCandidate is not null
            && TryGetGhostSuffix(ghostCandidate.Value, prefix, out var ghostSuffix))
        {
            ghostText = ghostSuffix;
            if (_resolution.IsChecklist
                && _resolution.ListMode == ListSelectionMode.Multiple
                && _selectedValues.Count > 0
                && _listFilter.Length == 0
                && !_inputText.EndsWith(','))
            {
                ghostText = "," + ghostText;
            }
        }
        return new CommandCompletionState(
            _inputText,
            ghostText,
            items,
            _resolution.IsChecklist && _resolution.ListMode == ListSelectionMode.Multiple,
            CanComplete: _resolution.AwaitingSeparator || items.Count > 0,
            CaretIndex: _caretIndex);
    }

    private bool TryGetGhostSuffix(string candidate, string prefix, out string suffix)
    {
        if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            suffix = candidate[prefix.Length..];
            return true;
        }

        if (_resolution.Kind == CompletionKind.Command)
        {
            var separatorIndex = candidate.IndexOf(':');
            var commandName = separatorIndex >= 0 ? candidate[(separatorIndex + 1)..] : candidate;
            if (commandName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                suffix = commandName[prefix.Length..];
                return true;
            }
        }

        suffix = string.Empty;
        return false;
    }
}
