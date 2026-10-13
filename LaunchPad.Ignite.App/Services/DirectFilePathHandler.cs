using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services;

public sealed class DirectFilePathHandler
{
    private readonly IProcessStarter _processStarter;
    private readonly DirectFilePathCompletion _completion;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly ISecretTerminalLauncher _terminalLauncher;

    public DirectFilePathHandler(
        IProcessStarter processStarter,
        DirectFilePathCompletion? completion = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        ISecretTerminalLauncher? terminalLauncher = null)
    {
        _processStarter = processStarter;
        _completion = completion ?? new DirectFilePathCompletion();
        _fileExists = fileExists ?? File.Exists;
        _directoryExists = directoryExists ?? Directory.Exists;
        _terminalLauncher = terminalLauncher ?? new SystemSecretTerminalLauncher(processStarter);
    }

    public bool IsDirectPath(string text) => _completion.IsDirectPath(text);

    public bool TryGetCompletionPath(string text, out string path) => _completion.TryGetCompletionPath(text, out path);

    public IReadOnlyList<string> GetSuggestions(string text) => _completion.GetSuggestions(text);

    public string GetSuggestionText(string text)
    {
        var path = text.Trim('"');
        return IsDirectPath(path) && path.Any(char.IsWhiteSpace) ? $"\"{path}\"" : path;
    }

    public bool TryOpen(string text, out CommandExecutionResult result)
    {
        if (!_completion.TryGetCompletionPath(text, out var path))
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

        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(isDirectory ? path : $"/select,{path}");

        try
        {
            _processStarter.Start(startInfo);
            result = new CommandExecutionResult(true, isDirectory ? "Opened folder in Explorer." : "Opened file in Explorer.");
        }
        catch (Exception exception)
        {
            result = new CommandExecutionResult(false, $"Unable to open path in Explorer: {exception.Message}");
        }

        return true;
    }

    public bool TryOpenInTerminal(string text, out CommandExecutionResult result)
    {
        if (!_completion.TryGetCompletionPath(text, out var path))
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
