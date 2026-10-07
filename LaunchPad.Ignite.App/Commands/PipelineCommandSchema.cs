using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class PipelineCommandSchema
{
    public static CommandSchema Create()
        => new(
            [new CommandListDefinition(
                "enum",
                "project",
                [
                    "claims",
                    "architecture",
                    "case-management",
                    "corporate",
                    "customer",
                    "documents",
                    "policy",
                    "regulatory",
                    "saif"
                ])],
            [new CommandDefinition(
                "pipeline",
                "url",
                "Launch ADO pipeline URLs",
                [new CommandActionDefinition(
                    "default",
                    "ADO project",
                    "pipeline <enum:project> [csv:pipelineId]",
                    "https://dev.azure.com/saifcorporation/<project>/_build?definitionId=<pipelineId>",
                    [
                        new CommandArgumentDefinition("project", Ref: "enum:project", AllowCustomValues: true),
                        new CommandArgumentDefinition("pipelineId", Optional: true, InputType: "csv")
                    ],
                    new CommandExecutionDefinition(CommandExecutionMode.TargetPerValue, ["pipelineId"]))])]);
}
