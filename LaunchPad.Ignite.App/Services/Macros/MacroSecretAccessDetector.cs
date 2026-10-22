using System;
using System.Linq;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services.Macros;

public sealed class MacroSecretAccessDetector(CommandCatalog catalog)
{
    private readonly CommandLineTokenizer _tokenizer = new();

    public bool UsesSecrets(string script)
    {
        foreach (var line in script.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var tokens = _tokenizer.Tokenize(line);
            if (tokens.Count == 0 || !catalog.TryGetCommand(tokens[0].Value, out var command))
            {
                continue;
            }

            var action = tokens.Count > 1 && catalog.TryGetAction(command, tokens[1].Value, out var explicitAction)
                ? explicitAction
                : command.Actions.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, "default", StringComparison.OrdinalIgnoreCase));
            if (action is null)
            {
                continue;
            }

            if (action.Arguments.Any(argument =>
                    string.Equals(argument.Ref, "list:secrets", StringComparison.OrdinalIgnoreCase))
                || catalog.Schema.Settings.Any(setting =>
                    setting.SecretReference
                    && string.Equals(setting.Namespace, command.Namespace, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
