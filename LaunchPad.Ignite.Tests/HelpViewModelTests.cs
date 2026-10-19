using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.Core.Schema;
using System.Linq;

namespace LaunchPad.Ignite.Tests;

public class HelpViewModelTests
{
    [Fact]
    public void GroupsShortcutsByCommandTypeAndExtensionOwnership()
    {
        var urlCommand = new CommandDefinition(
            "search",
            "url",
            "Search the web",
            [
                new CommandActionDefinition("default", "Search", "search <query>", "https://example.test", [])
                {
                    KeyboardShortcut = "ctrl+alt+s",
                    ExecuteOnKeyboardShortcut = true
                },
                new CommandActionDefinition("images", "Search images", "search images <query>", "https://example.test/images", [])
                {
                    KeyboardShortcut = "ctrl+shift+s"
                }
            ],
            Namespace: "web",
            KeyboardShortcut: "ctrl+alt+w",
            ExecuteOnKeyboardShortcut: true);
        var extensionCommand = new CommandDefinition(
            "build",
            "shell",
            "Build the project",
            [new CommandActionDefinition("default", "Build", "build <project>", "build", [])],
            Namespace: "tools",
            KeyboardShortcut: "ctrl+alt+b",
            ExecuteOnKeyboardShortcut: true);
        var helpViewModel = new HelpViewModel(
            [urlCommand, extensionCommand, SecretCommandDefinition.Definition],
            new Dictionary<string, string>
            {
                [urlCommand.QualifiedName] = "web-extension",
                [extensionCommand.QualifiedName] = "tools-extension"
            });

        Assert.Contains(helpViewModel.KeyboardShortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+H" && shortcut.Description == "Open Help");
        Assert.Contains(helpViewModel.CommandInputKeyboardShortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+Shift+Enter" && shortcut.Description.Contains("elevated", StringComparison.Ordinal));
        Assert.DoesNotContain(helpViewModel.CommandInputKeyboardShortcuts, shortcut =>
            shortcut.Shortcut == "Alt+Shift+Enter");
        Assert.DoesNotContain(helpViewModel.CommandInputKeyboardShortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+Enter");
        Assert.Contains(helpViewModel.UrlCommandKeyboardShortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+Enter" && shortcut.Description.Contains("new window", StringComparison.Ordinal));
        Assert.Contains(helpViewModel.CommandKeyboardShortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+L" && shortcut.Description.Contains("secret toggle", StringComparison.Ordinal));
        var toolsExtensionShortcuts = helpViewModel.ExtensionShortcutGroups
            .Single(group => group.ExtensionId == "tools-extension");
        var webExtensionShortcuts = helpViewModel.ExtensionShortcutGroups
            .Single(group => group.ExtensionId == "web-extension");
        Assert.Contains(toolsExtensionShortcuts.Shortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+Alt+B" && shortcut.Description.Contains("Command tools:build", StringComparison.Ordinal));
        Assert.Contains(webExtensionShortcuts.Shortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+Alt+W" && shortcut.Description.Contains("URL command web:search", StringComparison.Ordinal));
        Assert.Contains(webExtensionShortcuts.Shortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+Alt+S" && shortcut.Description.Contains("runs immediately", StringComparison.Ordinal));
        Assert.Contains(webExtensionShortcuts.Shortcuts, shortcut =>
            shortcut.Shortcut == "Ctrl+Shift+S" && shortcut.Description.Contains("expands in the launcher", StringComparison.Ordinal));
        Assert.DoesNotContain(helpViewModel.KeyboardShortcuts, shortcut => shortcut.Description.Contains("secret", StringComparison.Ordinal));
        Assert.DoesNotContain(helpViewModel.KeyboardShortcuts, shortcut => shortcut.Description.Contains("web:search", StringComparison.Ordinal));
        Assert.DoesNotContain(helpViewModel.CommandKeyboardShortcuts, shortcut => shortcut.Description.Contains("tools:build", StringComparison.Ordinal));
        Assert.DoesNotContain(helpViewModel.UrlCommandKeyboardShortcuts, shortcut => shortcut.Description.Contains("web:search", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "Ctrl+.", "Ctrl+H", "Ctrl+I", "Ctrl+M" },
            helpViewModel.KeyboardShortcuts.Select(shortcut => shortcut.Shortcut));
        Assert.Equal(
            new[] { "Ctrl+L" },
            helpViewModel.CommandKeyboardShortcuts.Select(shortcut => shortcut.Shortcut));
        Assert.Equal(
            new[] { "tools-extension", "web-extension" },
            helpViewModel.ExtensionShortcutGroups.Select(group => group.ExtensionId));
        Assert.Equal(
            new[] { "Ctrl+Alt+B" },
            toolsExtensionShortcuts.Shortcuts.Select(shortcut => shortcut.Shortcut));
        Assert.Equal(
            new[] { "Ctrl+Alt+S", "Ctrl+Alt+W", "Ctrl+Shift+S" },
            webExtensionShortcuts.Shortcuts.Select(shortcut => shortcut.Shortcut));
    }
}
