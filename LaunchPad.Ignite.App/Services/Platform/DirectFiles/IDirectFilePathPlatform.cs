using System.Collections.Generic;
using System.Diagnostics;

namespace LaunchPad.Ignite.App.Services.Platform.DirectFiles;

public interface IDirectFilePathPlatform
{
    bool IsDirectPath(string text);

    bool TryGetCompletionPath(string text, out string path);

    IReadOnlyList<string> GetSuggestions(string text);

    bool TryAcceptNextPathSegment(string currentPath, string suggestion, out string acceptedPath);

    bool IsPathPrefix(string path, string prefix);

    ProcessStartInfo CreateOpenPathStartInfo(string path, bool isDirectory);
}
