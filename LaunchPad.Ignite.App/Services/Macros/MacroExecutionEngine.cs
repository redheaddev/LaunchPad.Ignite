using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services.Macros;

public sealed record MacroExecutionResult(bool Succeeded, string Message);

public sealed class MacroExecutionEngine
{
    public async Task<MacroExecutionResult> ExecuteAsync(
        MacroDefinition macro,
        IReadOnlyDictionary<string, string> arguments,
        bool newWindow,
        CommandLaunchMode launchMode,
        Func<string, IReadOnlyDictionary<string, string>, bool, CommandLaunchMode, Task<LaunchPad.Ignite.Core.Execution.CommandExecutionResult>> executeCommandAsync)
    {
        var failures = new List<string>();
        var lines = macro.Script.Split(['\r', '\n'], StringSplitOptions.None);
        for (var index = 0; index < lines.Length; index++)
        {
            var commandText = lines[index].Trim();
            if (commandText.Length == 0)
            {
                continue;
            }

            LaunchPad.Ignite.Core.Execution.CommandExecutionResult result;
            try
            {
                result = await executeCommandAsync(commandText, arguments, newWindow, launchMode);
            }
            catch (Exception exception)
            {
                result = new LaunchPad.Ignite.Core.Execution.CommandExecutionResult(false, exception.Message);
            }

            if (result.Succeeded)
            {
                continue;
            }

            failures.Add($"Line {index + 1}: {result.Message}");
            if (macro.Metadata.OnError == MacroErrorBehavior.Break)
            {
                break;
            }
        }

        if (failures.Count == 0)
        {
            return new MacroExecutionResult(true, $"Macro '{macro.Metadata.QualifiedName}' completed.");
        }

        var summary = string.Join(Environment.NewLine, failures);
        var message = macro.Metadata.OnError == MacroErrorBehavior.Break
            ? $"Macro '{macro.Metadata.QualifiedName}' stopped after an error.{Environment.NewLine}{summary}"
            : $"Macro '{macro.Metadata.QualifiedName}' continued after errors.{Environment.NewLine}{summary}";
        return new MacroExecutionResult(false, message);
    }
}
