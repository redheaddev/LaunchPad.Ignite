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

    [Fact]
    public void ParsesNumpadKeysSeparatelyFromRegularDigits()
    {
        Assert.True(KeyboardShortcutChord.TryParse("alt+num+8", out var numpad));
        Assert.True(KeyboardShortcutChord.TryParse("alt+8", out var regular));

        Assert.Equal("alt+num+8", numpad.ToString());
        Assert.Equal("num8", numpad.Key);
        Assert.NotEqual(numpad, regular);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("p")]
    [InlineData("ctrl+ctrl+p")]
    [InlineData("ctrl+alt+pp")]
    [InlineData("ctrl++p")]
    [InlineData("alt+num")]
    [InlineData("alt+8+num+2")]
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
    public void FiltersShortcutAliasesByCaseInsensitivePrefix()
    {
        var resolver = new ShortcutResolver(
        [
            CreateShortcut("plc", "ado:pipeline customer ", null),
            CreateShortcut("plr", "ado:pull-request ", null),
            CreateShortcut("weather", "weather forecast ", null),
            CreateCommand("pipeline", null)
        ]);

        var matches = resolver.GetAliasExpansions("PL");

        Assert.Equal(
            new[]
            {
                new KeyValuePair<string, string>("plc", "ado:pipeline customer "),
                new KeyValuePair<string, string>("plr", "ado:pull-request ")
            },
            matches);
    }

    [Fact]
    public void ShortcutExpansionCanResolveVariablesInItsExpandedText()
    {
        var shortcut = CreateShortcut("plc", "ado:pipeline $project ", null);
        var resolver = new ShortcutResolver([shortcut]);
        var variableExpander = new LaunchPad.Ignite.Core.Execution.CommandTextVariableExpander(
            [new CommandVariableDefinition("project", "architecture")]);

        Assert.True(resolver.TryExpandAlias("plc", out var shortcutExpansion));
        var preview = variableExpander.Expand(shortcutExpansion);

        Assert.Equal("ado:pipeline architecture ", preview.ExpandedText);
        Assert.Equal(["project"], preview.ResolvedVariables);
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
    public void ResolvesActionKeyboardChordToCommandAndAction()
    {
        var action = new CommandActionDefinition("start", "", "", "start", [])
        {
            KeyboardShortcut = "alt+num+8",
            ExecuteOnKeyboardShortcut = true
        };
        var resolver = new ShortcutResolver([new CommandDefinition("media", "url", "", [action])]);

        Assert.True(KeyboardShortcutChord.TryParse("alt+num+8", out var chord));
        Assert.True(resolver.TryExpandChord(chord, out var expansion, out var executeImmediately));

        Assert.Equal("media start ", expansion);
        Assert.True(executeImmediately);
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
    public void ValidatesActionKeyboardShortcutSyntaxAndUniqueness()
    {
        var command = new CommandDefinition(
            "media",
            "url",
            "",
            [
                new CommandActionDefinition("start", "", "", "start", []) { KeyboardShortcut = "alt+num+8" },
                new CommandActionDefinition("stop", "", "", "stop", []) { KeyboardShortcut = "alt+num+8" },
                new CommandActionDefinition("next", "", "", "next", []) { KeyboardShortcut = "invalid" }
            ]);

        var errors = new CommandSchemaValidator().Validate(new CommandSchema([], [command]));

        Assert.Contains(errors, error => error.Contains("assigned more than once", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("invalid keyboard shortcut 'invalid'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsArgumentsOnShortcutCommands()
    {
        var shortcut = CreateShortcut("plc", "ado:pipeline customer ", null);
        var action = shortcut.Actions[0] with { Arguments = [new CommandArgumentDefinition("value")] };

        var errors = new CommandSchemaValidator().Validate(new CommandSchema([], [shortcut with { Actions = [action] }]));

        Assert.Contains(errors, error => error.Contains("no arguments or execution", StringComparison.Ordinal));
    }

    [Fact]
    public void KeyboardChordResolutionDefaultsToNonImmediateExecution()
    {
        var shortcut = CreateShortcut("plc", "ado:pipeline customer ", "ctrl+alt+p");
        var resolver = new ShortcutResolver([shortcut]);

        Assert.True(KeyboardShortcutChord.TryParse("ctrl+alt+p", out var chord));
        Assert.True(resolver.TryExpandChord(chord, out var expansion));

        // Keyboard chord expansion should not be immediate by default
        Assert.True(resolver.TryExpandChord(chord, out expansion, out var executeImmediately));
        Assert.False(executeImmediately);

        // But it should still provide the correct expansion text
        Assert.Equal("ado:pipeline customer ", expansion);
    }

    [Fact]
    public void ImmediateExecutionFlagCanBeConfiguredForCommands()
    {
        var commandWithImmediateExecution = new CommandDefinition(
            "pipeline",
            "url",
            "ADO pipelines",
            [new CommandActionDefinition("default", "", "", "https://example.test", [])],
            Namespace: "ado",
            KeyboardShortcut: "ctrl+alt+p",
            ExecuteOnKeyboardShortcut: true);
        var resolver = new ShortcutResolver([commandWithImmediateExecution]);

        Assert.True(KeyboardShortcutChord.TryParse("ctrl+alt+p", out var chord));
        Assert.True(resolver.TryExpandChord(chord, out var expansion));

        // Immediate execution should be true for this command
        Assert.True(resolver.TryExpandChord(chord, out expansion, out var executeImmediately));
        Assert.True(executeImmediately);

        // Expansion text should remain consistent
        Assert.Equal("ado:pipeline ", expansion);
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
