using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services.Clipboard;
using LaunchPad.Ignite.App.Services.Completion;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class ClipboardCommandModelTests
{
    [Fact]
    public void ClipboardCommandDefinitionPassesCommandCatalogValidation()
    {
        var catalog = new CommandCatalog(
            new CommandSchema([], [ClipboardCommandModel.Definition]),
            ["clipboard"]);

        Assert.True(catalog.TryGetCommand("clipboard", out var command));
        Assert.Equal(["list", "copy", "clear"], command.Actions.Select(action => action.Name));
    }

    [Fact]
    public async Task ListShowsStableIdsAndCopyRestoresTheSelectedEntry()
    {
        string? clipboardText = "first item";
        string? writtenText = null;
        var history = new ClipboardHistoryService(
            () => Task.FromResult<string?>(clipboardText),
            text =>
            {
                writtenText = text;
                return Task.CompletedTask;
            },
            () => false)
        {
            IsEnabled = true
        };
        await history.CaptureCurrentClipboardAsync();
        var firstItemId = Assert.Single(history.Entries).Id;
        clipboardText = "newer item";
        await history.CaptureCurrentClipboardAsync();
        var model = new ClipboardCommandModel(history);

        var listResult = await model.ExecuteAsync(CreateCommand("list"));
        var copyResult = await model.ExecuteAsync(CreateCommand("copy", new Dictionary<string, string>
        {
            ["id"] = firstItemId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));

        Assert.True(listResult.Succeeded);
        Assert.True(listResult.IsValueOutput);
        Assert.Contains($"{firstItemId} | first item", listResult.Message);
        Assert.True(copyResult.Succeeded);
        Assert.Equal("first item", writtenText);
    }

    [Fact]
    public async Task CopyOffersHistoryPreviewsInSingleSelectPicklist()
    {
        string? clipboardText = "older item";
        string? writtenText = null;
        var history = new ClipboardHistoryService(
            () => Task.FromResult<string?>(clipboardText),
            text =>
            {
                writtenText = text;
                return Task.CompletedTask;
            },
            () => false)
        {
            IsEnabled = true
        };
        await history.CaptureCurrentClipboardAsync();
        clipboardText = "latest item";
        await history.CaptureCurrentClipboardAsync();

        var schema = new CommandSchema([], [ClipboardCommandModel.Definition]);
        var valueProvider = new CommandSchemaValueProvider(
            schema,
            new CompositeArgumentValueProvider(new ClipboardHistoryArgumentValueProvider(history)));
        var catalog = new CommandCatalog(schema, ["clipboard"]);
        var completion = new CommandCompletionSession(catalog, valueProvider);
        var state = completion.UpdateInputText("clipboard copy ");

        Assert.True(state.IsListSelectionActive);
        Assert.Equal(history.Entries.Select(entry => entry.Id.ToString()), state.Items.Select(item => item.Value));
        Assert.Equal($"{history.Entries[0].Id} | latest item", state.Items[0].DisplayText);
        state = completion.HandleKey(CommandInputKey.Tab);
        Assert.Equal($"clipboard copy {history.Entries[0].Id}", state.InputText);

        var parsed = new CommandLineParser(catalog, valueProvider).Parse(state.InputText);
        Assert.Equal(CommandParseStatus.Valid, parsed.Status);
        var result = await new ClipboardCommandModel(history).ExecuteAsync(parsed.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal("latest item", writtenText);
    }

    [Fact]
    public async Task ClearRequiresConfirmationMetadataAndRemovesHistory()
    {
        var history = new ClipboardHistoryService(
            () => Task.FromResult<string?>("some item"),
            _ => Task.CompletedTask,
            () => false)
        {
            IsEnabled = true
        };
        await history.CaptureCurrentClipboardAsync();
        var model = new ClipboardCommandModel(history);
        var clearAction = ClipboardCommandModel.Definition.Actions.Single(action => action.Name == "clear");

        var result = await model.ExecuteAsync(CreateCommand("clear"));

        Assert.False(string.IsNullOrWhiteSpace(clearAction.ConfirmationPrompt));
        Assert.True(result.Succeeded);
        Assert.Empty(history.Entries);
    }

    private static ParsedCommand CreateCommand(string actionName, IReadOnlyDictionary<string, string>? arguments = null)
    {
        var action = ClipboardCommandModel.Definition.Actions.Single(candidate => candidate.Name == actionName);
        return new ParsedCommand(
            ClipboardCommandModel.Definition,
            action,
            arguments ?? new Dictionary<string, string>());
    }
}