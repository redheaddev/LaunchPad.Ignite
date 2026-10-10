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

    public DirectFilePathHandler(
        IProcessStarter processStarter,
        DirectFilePathCompletion? completion = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null)
    {
        _processStarter = processStarter;
        _completion = completion ?? new DirectFilePathCompletion();
        _fileExists = fileExists ?? File.Exists;
        _directoryExists = directoryExists ?? Directory.Exists;
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
        if (!IsDirectPath(text))
        {
            result = new CommandExecutionResult(false, string.Empty);
            return false;
        }

        var isDirectory = _directoryExists(text);
        if (!isDirectory && !_fileExists(text))
        {
            result = new CommandExecutionResult(false, $"Path '{text}' does not exist.");
            return true;
        }

        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(isDirectory ? text : $"/select,{text}");

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
}
