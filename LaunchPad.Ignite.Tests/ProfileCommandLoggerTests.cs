using System.IO;
using System;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class ProfileCommandLoggerTests
{
    [Fact]
    public void StartsNewSessionsAndWritesOnlySuccessfulCommandsToHistory()
    {
        using var directory = new TemporaryDirectory();
        var logger = new ProfileCommandLogger(directory.Path);
        var catalog = CreateCatalog();

        logger.StartSession();
        logger.RecordCommand("tool safe first", succeeded: true, catalog);
        logger.RecordCommand("tool safe second", succeeded: false, catalog);
        logger.StartSession();
        logger.RecordCommand("secret set api-key", succeeded: false, catalog);

        var log = File.ReadAllText(Path.Combine(directory.Path, "log.txt"));
        var history = File.ReadAllLines(Path.Combine(directory.Path, "history.txt"));

        Assert.Equal(2, log.Split("Session started").Length - 1);
        Assert.Contains("[SUCCESS] tool safe [REDACTED]", log);
        Assert.Contains("[FAILED] tool safe [REDACTED]", log);
        Assert.Contains("[FAILED] secret set api-key", log);
        Assert.Equal(1, history.Length);
        AssertHistoryEntry(history[0], "tool safe [REDACTED]");
    }

    [Fact]
    public void MasksSensitiveArgumentsInLogAndHistory()
    {
        using var directory = new TemporaryDirectory();
        var logger = new ProfileCommandLogger(directory.Path);

        var catalog = CreateCatalog();
        logger.RecordCommand("tool safe clear-text-secret", succeeded: true, catalog);
        logger.RecordCommand("tool safe password=clear-text-secret", succeeded: true, catalog);

        var log = File.ReadAllText(Path.Combine(directory.Path, "log.txt"));
        var history = File.ReadAllLines(Path.Combine(directory.Path, "history.txt"));

        Assert.DoesNotContain("clear-text-secret", log);
        Assert.DoesNotContain("clear-text-secret", string.Join(Environment.NewLine, history));
        Assert.Contains("tool safe [REDACTED]", log);
        Assert.Contains("tool safe password=[REDACTED]", log);
        AssertHistoryEntry(history[0], "tool safe [REDACTED]");
        AssertHistoryEntry(history[1], "tool safe password=[REDACTED]");
    }

    [Fact]
    public void ReadsHistoryNewestFirstAndFiltersByTypedPrefix()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllLines(Path.Combine(directory.Path, "history.txt"),
        [
            "2025-01-01T00:00:00.0000000+00:00 alpha old",
            "2025-01-02T00:00:00.0000000+00:00 beta recent",
            "2025-01-03T00:00:00.0000000+00:00 alpha new",
            "2025-01-04T00:00:00.0000000+00:00 alpha [REDACTED]",
            "2025-01-05T00:00:00.0000000+00:00 alpha new",
            "legacy command"
        ]);
        var logger = new ProfileCommandLogger(directory.Path);

        var history = logger.ReadHistoryEntries();
        var filtered = ProfileCommandLogger.FilterHistoryEntries(history, "ALPHA");

        Assert.Equal(
            ["legacy command", "alpha new", "alpha [REDACTED]", "beta recent", "alpha old"],
            history.Select(entry => entry.CommandText));
        Assert.Equal(["alpha new"], filtered.Select(entry => entry.CommandText));
        Assert.Equal(DateTimeOffset.Parse("2025-01-05T00:00:00.0000000+00:00"), history[1].RunTime);
    }

    [Fact]
    public void FormatsHistoryAgeInHours()
    {
        var now = new DateTimeOffset(2025, 1, 2, 1, 0, 0, TimeSpan.Zero);

        var age = ProfileCommandLogger.FormatHistoryAge(now - TimeSpan.FromHours(25), now);

        Assert.Equal("25h", age);
    }

    private static void AssertHistoryEntry(string entry, string expectedCommand)
    {
        var separator = entry.IndexOf(' ');
        Assert.True(separator > 0);
        Assert.True(DateTimeOffset.TryParse(entry[..separator], out _));
        Assert.Equal(expectedCommand, entry[(separator + 1)..]);
    }

    private static CommandCatalog CreateCatalog()
    {
        var tool = new CommandDefinition(
            "tool",
            "shell",
            "Test command",
            [new CommandActionDefinition(
                "default",
                "Run command",
                "tool <label> <password>",
                "tool.exe",
                [
                    new CommandArgumentDefinition("label"),
                    new CommandArgumentDefinition("password", Sensitive: true)
                ])]);
        var secret = new CommandDefinition(
            "secret",
            "secret",
            "Manage secrets",
            [new CommandActionDefinition("set", "Set secret", "secret set <name>", "secret", [new CommandArgumentDefinition("name")])]);
        return new CommandCatalog(new CommandSchema([], [tool, secret]), ["shell", "secret"]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
            => Directory.Delete(Path, recursive: true);
    }
}
