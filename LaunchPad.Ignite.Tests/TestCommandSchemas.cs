using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

internal static class TestCommandSchemas
{
    public static CommandSchema Pipeline { get; } = new(
        [new CommandListDefinition("enum", "project", ["claims", "architecture", "case-management"])],
        [new CommandDefinition(
            "pipeline",
            "url",
            "Launch pipeline URLs",
            [new CommandActionDefinition(
                "default",
                "ADO project",
                "pipeline <enum:project> [csv:pipelineId]",
                "https://dev.azure.com/saifcorporation/<project>/_build?definitionId=<pipelineId>",
                [
                    new CommandArgumentDefinition("project", Ref: "enum:project"),
                    new CommandArgumentDefinition("pipelineId", Optional: true, InputType: "csv")
                ],
                new CommandExecutionDefinition(CommandExecutionMode.TargetPerValue, ["pipelineId"]))])]);

    public static CommandSchema SecretList(ListSelectionMode mode = ListSelectionMode.Normal)
        => new(
            [],
            [new CommandDefinition(
                "secret",
                "url",
                "Test list command",
                [new CommandActionDefinition(
                    "shell",
                    "Select list values",
                    "secret shell <list:secrets>",
                    "https://example.test/",
                    [new CommandArgumentDefinition("secrets", Ref: "list:secrets", ListMode: mode)])])]);

    public static IArgumentValueProvider CreatePipelineProvider()
        => new CommandSchemaValueProvider(Pipeline);

    public static IArgumentValueProvider CreateSecretListProvider()
        => new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["list:secrets"] = ["token1", "token2", "token3"]
        });
}

internal sealed class DictionaryArgumentValueProvider : IArgumentValueProvider
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _values;

    public DictionaryArgumentValueProvider(IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        _values = values;
    }

    public IReadOnlyList<string> GetValues(string reference)
        => _values.TryGetValue(reference, out var values) ? values : Array.Empty<string>();
}
