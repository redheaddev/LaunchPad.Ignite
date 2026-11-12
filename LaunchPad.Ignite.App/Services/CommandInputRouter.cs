using System;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services.DirectFiles;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Services;

public sealed record CommandInputRoutingResult(
    CommandParseResult ParseResult,
    bool ParsedWithNewWindowOption,
    CommandExecutionResult? ExecutionResult);

public sealed class CommandInputRouter
{
    private readonly CommandLineParser _parser;
    private readonly DirectFilePathHandler _directFilePathHandler;
    private readonly UrlCommandModel _urlCommandModel;

    public CommandInputRouter(
        CommandLineParser parser,
        DirectFilePathHandler directFilePathHandler,
        UrlCommandModel urlCommandModel)
    {
        _parser = parser;
        _directFilePathHandler = directFilePathHandler;
        _urlCommandModel = urlCommandModel;
    }

    public CommandInputRoutingResult Route(
        string input,
        bool allowDirectExecution,
        bool newWindow,
        Func<ParsedCommand, bool> supportsNewWindowOption)
    {
        var parseResult = _parser.Parse(input);
        if (allowDirectExecution && _directFilePathHandler.TryOpen(input, out var directFileResult))
        {
            return new CommandInputRoutingResult(parseResult, false, directFileResult);
        }

        if (allowDirectExecution
            && parseResult.Status == CommandParseStatus.Invalid
            && _directFilePathHandler.TryLaunchCommand(input, out var directCommandResult))
        {
            return new CommandInputRoutingResult(parseResult, false, directCommandResult);
        }

        var hasNewWindowOption = UrlCommandModel.TryRemoveNewWindowOption(input, out var textWithoutNewWindowOption);
        if (allowDirectExecution
            && _urlCommandModel.TryExecuteDirectUrl(input, out var directUrlResult, newWindow))
        {
            return new CommandInputRoutingResult(parseResult, false, directUrlResult);
        }

        if (allowDirectExecution
            && hasNewWindowOption
            && _urlCommandModel.TryExecuteDirectUrl(textWithoutNewWindowOption, out directUrlResult, newWindow: true))
        {
            return new CommandInputRoutingResult(parseResult, false, directUrlResult);
        }

        var parsedWithNewWindowOption = false;
        if (hasNewWindowOption)
        {
            var optionFreeParseResult = _parser.Parse(textWithoutNewWindowOption);
            if (optionFreeParseResult.Status == CommandParseStatus.Valid
                && supportsNewWindowOption(optionFreeParseResult.ParsedCommand!))
            {
                parseResult = optionFreeParseResult;
                parsedWithNewWindowOption = true;
            }
        }

        return new CommandInputRoutingResult(parseResult, parsedWithNewWindowOption, null);
    }
}
