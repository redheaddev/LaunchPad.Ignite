using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Models;

namespace LaunchPad.Ignite.App.Services;

public interface ISnippetDialogService
{
    Task<SnippetEditorResult?> ShowEditorAsync(
        string name,
        SnippetDefinition? snippet,
        IReadOnlyList<string> availableSecrets);

    Task<bool> ConfirmRemoveAsync(string name);

    Task<IReadOnlyDictionary<string, string>?> ShowArgumentsAsync(string name, IReadOnlyList<string> arguments);
}

public sealed record SnippetEditorResult(
    string Type,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> AllowedSecrets,
    string Script);
