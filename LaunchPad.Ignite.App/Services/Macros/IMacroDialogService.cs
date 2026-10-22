using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Models;

namespace LaunchPad.Ignite.App.Services.Macros;

public interface IMacroDialogService
{
    Task<MacroEditorResult?> ShowEditorAsync(string qualifiedName, MacroDefinition? macro);

    Task<bool> ConfirmRemoveAsync(string qualifiedName);
}

public sealed record MacroEditorResult(
    IReadOnlyList<MacroArgumentDefinition> Arguments,
    MacroErrorBehavior OnError,
    string Script);
