using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Services.Clipboard;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class ClipboardCommandModel : IAsyncCommandExecutor
{
    private readonly ClipboardHistoryService _clipboardHistory;

    public static CommandDefinition Definition { get; } = new(
        "clipboard",
        "clipboard",
        "List and manage clipboard history",
        [
            new CommandActionDefinition(
                "list",
                "List recent clipboard history items",
                "clipboard list",
                "clipboard list",
                []),
            new CommandActionDefinition(
                "copy",
                "Restore a history item to the clipboard",
                "clipboard copy <id>",
                "clipboard copy",
                [new CommandArgumentDefinition("id", Ref: "list:clipboard", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition(
                "clear",
                "Clear clipboard history",
                "clipboard clear",
                "clipboard clear",
                [])
            {
                ConfirmationPrompt = "Clear all clipboard history items for this session?"
            }
        ]) { Handler = "clipboard" };

    public string Kind => "clipboard";

    public ClipboardCommandModel(ClipboardHistoryService clipboardHistory)
        => _clipboardHistory = clipboardHistory;

    public async Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        if (string.Equals(command.Action.Name, "list", StringComparison.OrdinalIgnoreCase))
        {
            var items = _clipboardHistory.Entries;
            var output = items.Count == 0
                ? "Clipboard history is empty. Enable it in Settings > Clipboard and copy some text."
                : string.Join(Environment.NewLine, items.Select(item => $"{item.Id} | {FormatPreview(item.Text)}"));
            return new CommandExecutionResult(true, output) { IsValueOutput = true };
        }

        if (string.Equals(command.Action.Name, "copy", StringComparison.OrdinalIgnoreCase))
        {
            if (!command.Arguments.TryGetValue("id", out var idText)
                || !long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || _clipboardHistory.Find(id) is not { } entry)
            {
                return new CommandExecutionResult(false, "Clipboard item not found. Run 'clipboard list' to see current item IDs.");
            }

            try
            {
                await _clipboardHistory.CopyToClipboardAsync(entry.Text);
                return new CommandExecutionResult(true, $"Copied clipboard item {id} to the clipboard.");
            }
            catch (Exception exception)
            {
                return new CommandExecutionResult(false, $"Could not copy clipboard item: {exception.Message}");
            }
        }

        if (string.Equals(command.Action.Name, "clear", StringComparison.OrdinalIgnoreCase))
        {
            _clipboardHistory.Clear();
            return new CommandExecutionResult(true, "Clipboard history cleared.");
        }

        return new CommandExecutionResult(false, $"Unknown clipboard action '{command.Action.Name}'.");
    }

    private static string FormatPreview(string text)
    {
        var preview = text.Replace('\r', ' ').Replace('\n', ' ');
        return preview.Length <= 160 ? preview : $"{preview[..160]}...";
    }
}