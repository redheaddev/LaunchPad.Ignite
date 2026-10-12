using System.Collections.Generic;
using System.Linq;

namespace LaunchPad.Ignite.App.Services;

public sealed record ColorTheme(
    string Id,
    string Name,
    string WindowBackground,
    string PanelBackground,
    string InputBackground,
    string Border,
    string Text,
    string SecondaryText,
    string MutedText,
    string Accent,
    string Highlight,
    string TextSelection,
    string CommandFocus,
    string NavigationSelected,
    string Target,
    string RequiredArgument,
    string OptionalArgument,
    string Checked,
    string Status)
{
    public static IReadOnlyList<(string Name, string Property, string Usage)> EditableColors { get; } =
    [
        ("Window background", nameof(WindowBackground), "Main launcher and Settings window surfaces."),
        ("Panel background", nameof(PanelBackground), "Cards and grouped settings panels."),
        ("Input background", nameof(InputBackground), "Command entry and editable fields."),
        ("Border", nameof(Border), "Outlines around fields, cards, and dividers."),
        ("Text", nameof(Text), "Primary labels, commands, and navigation."),
        ("Secondary text", nameof(SecondaryText), "Descriptions and supporting information."),
        ("Muted text / ghost completion", nameof(MutedText), "Hints, placeholders, keyboard shortcuts, and suggested command text."),
        ("Accent", nameof(Accent), "Emphasized actions and command tokens."),
        ("Selection highlight", nameof(Highlight), "Background behind the selected suggestion."),
        ("Text selection", nameof(TextSelection), "Background behind selected command text."),
        ("Command focus", nameof(CommandFocus), "Border around the command box when it has focus."),
        ("Selected navigation", nameof(NavigationSelected), "Background behind the selected Settings tab."),
        ("Target preview", nameof(Target), "Resolved command target preview (URLs and paths)."),
        ("Required argument", nameof(RequiredArgument), "Required arguments in Usage text."),
        ("Optional argument", nameof(OptionalArgument), "Optional arguments in Usage text."),
        ("Checked value", nameof(Checked), "Completed and selected values."),
        ("Status", nameof(Status), "Status text and less prominent results.")
    ];

    public static ColorTheme Default { get; } = new(
        "default", "Default", "#FF0F172A", "#F2111827", "#FF020617", "#FF334155",
        "#FFE2E8F0", "#FF94A3B8", "#FF64748B", "#FF67E8F9", "#FF1E3A5F", "#FFFF008C", "#FFFF008C", "#FF1E3A5F",
        "#FFE86AAE", "#FF86EFAC", "#FF94A3B8", "#FF67E8F9", "#FF64748B");

    public static ColorTheme CharcoalLimeYellow { get; } = new(
        "charcoal-lime-yellow", "Charcoal / Lime / Yellow", "#FF202321", "#FF292D28", "#FF171A18", "#FF454B40",
        "#FFF1F3E8", "#FFB9C0AC", "#FF858D78", "#FFA3E635", "#FF3A4230", "#FFA3E635", "#FFA3E635", "#FF3A4230",
        "#FFFACC15", "#FFA3E635", "#FFB9C0AC", "#FFFACC15", "#FF858D78");

    public ColorTheme WithColor(string property, string value) => property switch
    {
        nameof(WindowBackground) => this with { WindowBackground = value },
        nameof(PanelBackground) => this with { PanelBackground = value },
        nameof(InputBackground) => this with { InputBackground = value },
        nameof(Border) => this with { Border = value },
        nameof(Text) => this with { Text = value },
        nameof(SecondaryText) => this with { SecondaryText = value },
        nameof(MutedText) => this with { MutedText = value },
        nameof(Accent) => this with { Accent = value },
        nameof(Highlight) => this with { Highlight = value },
        nameof(TextSelection) => this with { TextSelection = value },
        nameof(CommandFocus) => this with { CommandFocus = value },
        nameof(NavigationSelected) => this with { NavigationSelected = value },
        nameof(Target) => this with { Target = value },
        nameof(RequiredArgument) => this with { RequiredArgument = value },
        nameof(OptionalArgument) => this with { OptionalArgument = value },
        nameof(Checked) => this with { Checked = value },
        nameof(Status) => this with { Status = value },
        _ => this
    };

}
