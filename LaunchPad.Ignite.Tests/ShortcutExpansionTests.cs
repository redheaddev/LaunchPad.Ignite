using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class ShortcutExpansionTests
{
    [Fact]
    public void ParsesAndCanonicalizesKeyboardChordModifiers()
    {
        Assert.True(KeyboardShortcutChord.TryParse("ctrl+alt+p", out var first));
        Assert.True(KeyboardShortcutChord.TryParse("ALT+CTRL+P", out var reordered));

        Assert.Equal(first, reordered);
        Assert.Equal("ctrl+alt+p", first.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("p")]
    [InlineData("ctrl+ctrl+p")]
    [InlineData("ctrl+alt+pp")]
    [InlineData("ctrl++p")]
    public void RejectsInvalidKeyboardChords(string? value)
    {
        Assert.False(KeyboardShortcutChord.TryParse(value, out _));
    }

    [Fact]
    public void ResolvesExactAliasAndKeyboardChordToExpansion()
    {
        var shortcut = CreateShortcut("plc", "ado:pipeline customer ", "ctrl+alt+p");
        var resolver = new ShortcutResolver([shortcut]);

        Assert.True(resolver.TryExpandAlias("PLC", out var aliasExpansion));
        Assert.Equal("ado:pipeline customer ", aliasExpansion);
        Assert.False(resolver.TryExpandAlias("plc extra", out _));

        Assert.True(KeyboardShortcutChord.TryParse("alt+ctrl+p", out var chord));
        Assert.True(resolver.TryExpandChord(chord, out var chordExpansion));
        Assert.Equal("ado:pipeline customer ", chordExpansion);
    }

    [Fact]
    public void ResolvesKeyboardChordOnRegularCommandToQualifiedCommandName()
    {
        var command = new CommandDefinition(
            "pipeline",
            "url",
            "ADO pipelines",
            [new CommandActionDefinition("default", "", "", "https://example.test", [])],
            Namespace: "ado",
            KeyboardShortcut: "ctrl+alt+p");
        var resolver = new ShortcutResolver([command]);

        Assert.True(KeyboardShortcutChord.TryParse("ctrl+alt+p", out var chord));
        Assert.True(resolver.TryExpandChord(chord, out var expansion));
        Assert.Equal("ado:pipeline ", expansion);
    }

    [Fact]
    public void ValidatesShortcutCommandShapeAndUniqueChords()
    {
        var schema = new CommandSchema([],
        [
            CreateShortcut("plc", "ado:pipeline customer ", "ctrl+alt+p"),
            CreateCommand("other", "alt+ctrl+p")
        ]);

        var errors = new CommandSchemaValidator().Validate(schema);

        Assert.Contains(errors, error => error.Contains("assigned more than once", StringComparison.Ordinal));
        Assert.Empty(new CommandSchemaValidator().Validate(new CommandSchema([], [CreateShortcut("plc", "ado:pipeline customer ", null)])));
        Assert.Empty(new CommandSchemaValidator().Validate(new CommandSchema([], [CreateCommand("pipeline", "ctrl+alt+p")])));
    }

    [Fact]
    public void RejectsArgumentsOnShortcutCommands()
    {
        var shortcut = CreateShortcut("plc", "ado:pipeline customer ", null);
        var action = shortcut.Actions[0] with { Arguments = [new CommandArgumentDefinition("value")] };

        var errors = new CommandSchemaValidator().Validate(new CommandSchema([], [shortcut with { Actions = [action] }]));

        Assert.Contains(errors, error => error.Contains("no arguments or execution", StringComparison.Ordinal));
    }

    private static CommandDefinition CreateShortcut(string name, string expansion, string? keyboardShortcut)
        => new(
            name,
            "shortcut",
            "Text expansion",
            [new CommandActionDefinition("default", "", name, expansion, [])],
            Namespace: "",
            KeyboardShortcut: keyboardShortcut);

    private static CommandDefinition CreateCommand(string name, string? keyboardShortcut)
        => new(
            name,
            "url",
            "Command",
            [new CommandActionDefinition("default", "", name, "https://example.test", [])],
            KeyboardShortcut: keyboardShortcut);
}
