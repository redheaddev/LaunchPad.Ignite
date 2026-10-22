using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.App.Services.Terminals;

namespace LaunchPad.Ignite.App.Services.DirectFiles;

public sealed class DirectFilePathHandler
{
    private readonly IProcessStarter _processStarter;
    private readonly IDirectFilePathPlatform _platform;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly ISecretTerminalLauncher _terminalLauncher;

    public DirectFilePathHandler(
        IProcessStarter processStarter,
        IDirectFilePathPlatform? platform = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        ISecretTerminalLauncher? terminalLauncher = null)
    {
        _processStarter = processStarter;
        _platform = platform ?? DirectFilePathPlatformFactory.Create();
        _fileExists = fileExists ?? File.Exists;
        _directoryExists = directoryExists ?? Directory.Exists;
        _terminalLauncher = terminalLauncher ?? new SystemSecretTerminalLauncher(processStarter);
    }

    public bool IsDirectPath(string text) => _platform.IsDirectPath(text);

    public bool TryGetCompletionPath(string text, out string path) => _platform.TryGetCompletionPath(text, out path);

    public IReadOnlyList<string> GetSuggestions(string text) => _platform.GetSuggestions(text);

    public bool TryAcceptNextPathSegment(string currentPath, string suggestion, out string acceptedPath)
        => _platform.TryAcceptNextPathSegment(currentPath, suggestion, out acceptedPath);

    public bool IsPathPrefix(string path, string prefix)
        => _platform.IsPathPrefix(path, prefix);

    public string GetSuggestionText(string text)
    {
        var path = text.Trim('"');
        return IsDirectPath(path) && path.Any(char.IsWhiteSpace) ? $"\"{path}\"" : path;
    }

    public bool TryOpen(string text, out CommandExecutionResult result)
    {
        if (!_platform.TryGetCompletionPath(text, out var path))
        {
            result = new CommandExecutionResult(false, string.Empty);
            return false;
        }

        var isDirectory = _directoryExists(path);
        if (!isDirectory && !_fileExists(path))
        {
            result = new CommandExecutionResult(false, $"Path '{path}' does not exist.");
            return true;
        }

        try
        {
            _processStarter.Start(_platform.CreateOpenPathStartInfo(path, isDirectory));
            result = new CommandExecutionResult(true, isDirectory ? "Opened folder." : "Opened file.");
        }
        catch (Exception exception)
        {
            result = new CommandExecutionResult(false, $"Unable to open path: {exception.Message}");
        }

        return true;
    }

    public bool TryOpenInTerminal(string text, out CommandExecutionResult result)
    {
        if (!_platform.TryGetCompletionPath(text, out var path))
        {
            result = new CommandExecutionResult(false, string.Empty);
            return false;
        }

        var isDirectory = _directoryExists(path);
        if (!isDirectory && !_fileExists(path))
        {
            result = new CommandExecutionResult(false, $"Path '{path}' does not exist.");
            return true;
        }

        var directory = isDirectory ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            result = new CommandExecutionResult(false, $"Unable to determine the containing folder for '{path}'.");
            return true;
        }

        try
        {
            _terminalLauncher.OpenShell(new Dictionary<string, string>(), directory);
            result = new CommandExecutionResult(true, "Opened folder in terminal.");
        }
        catch (Exception exception)
        {
            result = new CommandExecutionResult(false, $"Unable to open folder in terminal: {exception.Message}");
        }

        return true;
    }
}
