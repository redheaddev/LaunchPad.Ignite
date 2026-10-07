using CommunityToolkit.Mvvm.ComponentModel;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly CommandCompletionSession _completionSession;
    private readonly CommandCatalog _catalog;
    private readonly CommandLineParser _parser;
    private readonly CommandLineTokenizer _tokenizer = new();
    private readonly UrlTargetBuilder _targetBuilder = new();
    private readonly IUrlTargetOpener _urlTargetOpener;
    private bool _syncingInput;

    public MainViewModel()
    {
        var schema = PipelineCommandSchema.Create();
        _catalog = new CommandCatalog(schema);
        var valueProvider = new CommandSchemaValueProvider(schema);
        _completionSession = new CommandCompletionSession(_catalog, valueProvider);
        _parser = new CommandLineParser(_catalog, valueProvider);
        _urlTargetOpener = new SystemUrlTargetOpener();
        ApplyCompletionState(_completionSession.State);
    }

    [ObservableProperty]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private string _completionGhostText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<CompletionOptionViewModel> _completionItems = Array.Empty<CompletionOptionViewModel>();

    [NotifyPropertyChangedFor(nameof(HasUsageText))]
    [ObservableProperty]
    private string _usageText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Start typing to see commands";

    public bool IsChecklistActive => _completionSession.State.IsChecklistActive;

    public bool HasCompletionOptions => _completionSession.State.Items.Count > 0;

    public bool HasUsageText => !string.IsNullOrEmpty(UsageText);

    public bool CanComplete => _completionSession.State.CanComplete;

    public bool IsCommandTextEmpty => string.IsNullOrEmpty(CommandText);

    public void HandleInputKey(CommandInputKey key)
    {
        if (key == CommandInputKey.Enter)
        {
            ExecuteCommand();
            return;
        }

        ApplyCompletionState(_completionSession.HandleKey(key));
    }

    partial void OnCommandTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsCommandTextEmpty));
        if (!_syncingInput)
        {
            ApplyCompletionState(_completionSession.UpdateInputText(value));
        }
    }

    private void ApplyCompletionState(CommandCompletionState state)
    {
        if (!string.Equals(CommandText, state.InputText, StringComparison.Ordinal))
        {
            _syncingInput = true;
            CommandText = state.InputText;
            _syncingInput = false;
        }

        CompletionGhostText = state.InputText.Length == 0 ? string.Empty : state.GhostText;
        UsageText = GetUsageText(CommandText);
        CompletionItems = state.Items.Select(item => new CompletionOptionViewModel(
            state.IsChecklistActive ? $"{(item.IsSelected ? "[x]" : "[ ]")} {item.Value}" : item.Value,
            item.IsHighlighted,
            item.IsSelected)).ToArray();
        OnPropertyChanged(nameof(IsChecklistActive));
        OnPropertyChanged(nameof(HasCompletionOptions));
        OnPropertyChanged(nameof(CanComplete));
    }

public sealed record CompletionOptionViewModel(string DisplayText, bool IsHighlighted, bool IsSelected);

    private string GetUsageText(string inputText)
    {
        var tokens = _tokenizer.Tokenize(inputText);
        if (tokens.Count == 0 || !_catalog.TryGetCommand(tokens[0].Value, out var command))
        {
            return string.Empty;
        }

        var action = command.Actions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "default", StringComparison.OrdinalIgnoreCase));
        if (tokens.Count > 1 && _catalog.TryGetAction(command, tokens[1].Value, out var explicitAction)
            && !string.Equals(explicitAction.Name, "default", StringComparison.OrdinalIgnoreCase))
        {
            action = explicitAction;
        }

        return action is null ? string.Empty : $"Usage: {action.Usage}";
    }

    private void ExecuteCommand()
    {
        var parseResult = _parser.Parse(CommandText);
        if (parseResult.Status == CommandParseStatus.Incomplete)
        {
            StatusMessage = "Command is incomplete.";
            return;
        }

        if (parseResult.Status == CommandParseStatus.Invalid)
        {
            StatusMessage = parseResult.Error ?? "Command is invalid.";
            return;
        }

        var targetResult = _targetBuilder.Build(parseResult);
        if (!targetResult.Succeeded)
        {
            StatusMessage = targetResult.Error ?? "Unable to build URL target.";
            return;
        }

        try
        {
            _urlTargetOpener.Open(targetResult.Targets);
            StatusMessage = targetResult.Targets.Count == 1
                ? "Opened URL."
                : $"Opened {targetResult.Targets.Count} URLs.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Unable to open URL: {exception.Message}";
        }
    }
}
