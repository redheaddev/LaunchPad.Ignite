using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LaunchPad.Ignite.App.Services.Clipboard;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services.Completion;

public sealed class ClipboardHistoryArgumentValueProvider : IArgumentValueProvider, IArgumentValueDisplayProvider
{
    private const string Reference = "list:clipboard";
    private readonly ClipboardHistoryService _clipboardHistory;

    public ClipboardHistoryArgumentValueProvider(ClipboardHistoryService clipboardHistory)
        => _clipboardHistory = clipboardHistory;

    public IReadOnlyList<string> GetValues(string reference)
        => IsClipboardReference(reference)
            ? _clipboardHistory.Entries
                .Select(entry => entry.Id.ToString(CultureInfo.InvariantCulture))
                .ToArray()
            : [];

    public IReadOnlyList<string> GetDisplayValues(string reference)
        => IsClipboardReference(reference)
            ? _clipboardHistory.Entries
                .Select(entry => $"{entry.Id} | {FormatPreview(entry.Text)}")
                .ToArray()
            : [];

    private static bool IsClipboardReference(string reference)
        => string.Equals(reference, Reference, StringComparison.OrdinalIgnoreCase);

    private static string FormatPreview(string text)
    {
        var preview = text.Replace('\r', ' ').Replace('\n', ' ');
        return preview.Length <= 120 ? preview : $"{preview[..120]}...";
    }
}