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
        Assert.DoesNotContain("api-key", log);
        Assert.Equal(1, history.Length);
        AssertHistoryEntry(history[0], "tool safe [REDACTED]");
    }

    [Fact]
    public void MasksSensitiveArgumentsInLogAndHistory()
    {
        using var directory = new TemporaryDirectory();
        var logger = new ProfileCommandLogger(directory.Path);

        logger.RecordCommand("tool safe clear-text-secret", succeeded: true, CreateCatalog());

        var log = File.ReadAllText(Path.Combine(directory.Path, "log.txt"));
        var history = File.ReadAllText(Path.Combine(directory.Path, "history.txt"));

        Assert.DoesNotContain("clear-text-secret", log);
        Assert.DoesNotContain("clear-text-secret", history);
        Assert.Contains("tool safe [REDACTED]", log);
        AssertHistoryEntry(File.ReadAllLines(Path.Combine(directory.Path, "history.txt")).Single(), "tool safe [REDACTED]");
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
