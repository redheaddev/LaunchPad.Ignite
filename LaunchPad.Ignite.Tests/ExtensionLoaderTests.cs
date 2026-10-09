using System;
using System.IO;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class ExtensionLoaderTests
{
    [Fact]
    public void LoadsManifestSchemaAndExecutesExtensionCommand()
    {
        using var directory = new TemporaryDirectory();
        var extensionDirectory = Path.Combine(directory.Path, "sample");
        Directory.CreateDirectory(extensionDirectory);
        var assemblyPath = Path.Combine(extensionDirectory, "extension-plugin.dll");
        File.Copy(typeof(TestExtensionExecutor).Assembly.Location, assemblyPath);
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), """
            {
              "id": "sample",
              "name": "Sample extension",
              "namespace": "sample",
              "assembly": "extension-plugin.dll",
              "schema": {
                "variables": [{ "name": "endpoint", "value": "https://example.test" }],
                "lists": [{ "type": "enum", "name": "environment", "values": ["dev"] }],
                "commands": [{
                  "name": "sample",
                  "kind": "sample",
                  "description": "Sample command",
                  "actions": [{
                    "name": "default",
                    "description": "",
                    "usage": "sample:sample",
                    "targetTemplate": "sample:",
                    "arguments": []
                  }]
                }],
                "functions": [{ "name": "lookup", "parameters": [{ "name": "value" }] }]
              }
            }
            """);

        var extensions = new ExtensionLoader([directory.Path]).Load();
        var catalog = new CommandCatalog(extensions.Schema, extensions.Executors.Keys);
        var command = catalog.Commands.Single();
        var action = Assert.Single(command.Actions);
        var executor = extensions.Executors["sample"];

        Assert.True(catalog.TryGetCommand("sample:sample", out _));
        Assert.Equal("sample:endpoint", Assert.Single(extensions.Schema.Variables).QualifiedName);
        Assert.Equal("dev", Assert.Single(extensions.Schema.Lists).Values[0]);
        var result = executor.Execute(new ParsedCommand(command, action, new Dictionary<string, string>()));
        Assert.True(result.Succeeded);
        Assert.Equal("Executed by extension.", result.Message);
    }

    [Fact]
    public void PreservesExplicitCommandNamespacesAndUsesManifestNamespaceAsFallback()
    {
        using var directory = new TemporaryDirectory();
        var extensionDirectory = Path.Combine(directory.Path, "sample");
        Directory.CreateDirectory(extensionDirectory);
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), """
            {
              "id": "sample",
              "name": "Sample extension",
              "namespace": "fallback",
              "schema": {
                "lists": [],
                "commands": [
                  {
                    "name": "bare",
                    "namespace": "",
                    "kind": "url",
                    "description": "Bare command",
                    "actions": [{ "name": "default", "usage": "bare", "targetTemplate": "https://example.test", "arguments": [] }]
                  },
                  {
                    "name": "grouped",
                    "namespace": "gh",
                    "kind": "url",
                    "description": "Grouped command",
                    "actions": [{ "name": "default", "usage": "gh:grouped", "targetTemplate": "https://example.test", "arguments": [] }]
                  },
                  {
                    "name": "defaulted",
                    "kind": "url",
                    "description": "Defaulted command",
                    "actions": [{ "name": "default", "usage": "fallback:defaulted", "targetTemplate": "https://example.test", "arguments": [] }]
                  }
                ]
              }
            }
            """);

        var extensions = new ExtensionLoader([directory.Path]).Load();
        var catalog = new CommandCatalog(extensions.Schema, extensions.Executors.Keys);

        Assert.True(catalog.TryGetCommand("bare", out _));
        Assert.True(catalog.TryGetCommand("gh:grouped", out _));
        Assert.True(catalog.TryGetCommand("fallback:defaulted", out _));
    }

    [Fact]
    public void LoadsRegisteredScriptFunctionsAndResolvesTheirPathsInsideTheExtension()
    {
        using var directory = new TemporaryDirectory();
        var extensionDirectory = Path.Combine(directory.Path, "sample");
        var scriptsDirectory = Path.Combine(extensionDirectory, "scripts");
        Directory.CreateDirectory(scriptsDirectory);
        File.WriteAllText(Path.Combine(scriptsDirectory, "greet.py"), "print('hello')");
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), """
            {
              "id": "sample",
              "name": "Sample extension",
              "namespace": "sample",
              "schema": {
                "lists": [],
                "commands": [],
                "functions": [{
                  "name": "greet",
                  "kind": "script",
                  "parameters": [{ "name": "name" }, { "name": "prefix", "optional": true }],
                  "interpreter": "python3",
                  "scriptPath": "scripts/greet.py"
                }]
              }
            }
            """);

        var extensions = new ExtensionLoader([directory.Path]).Load();
        var function = Assert.Single(extensions.Schema.Functions);

        Assert.Equal("sample:greet", function.QualifiedName);
        Assert.Equal(Path.GetFullPath(Path.Combine(scriptsDirectory, "greet.py")), function.ScriptPath);
        Assert.True(function.Parameters[1].Optional);
    }

    [Fact]
    public void LoadsNamedFunctionExecutorsWithoutRestrictingCommandKinds()
    {
        using var directory = new TemporaryDirectory();
        var extensionDirectory = Path.Combine(directory.Path, "sample");
        Directory.CreateDirectory(extensionDirectory);
        var assemblyPath = Path.Combine(extensionDirectory, "extension-plugin.dll");
        File.Copy(typeof(TestExtensionFunctionExecutor).Assembly.Location, assemblyPath);
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), """
            {
              "id": "sample",
              "name": "Sample extension",
              "namespace": "sample",
              "assembly": "extension-plugin.dll",
              "schema": {
                "lists": [],
                "commands": [{
                  "name": "custom",
                  "kind": "sample",
                  "description": "Custom command kind",
                  "actions": [{ "name": "default", "usage": "sample:custom", "targetTemplate": "sample:", "arguments": [] }]
                }],
                "functions": [{ "name": "lookup", "parameters": [{ "name": "value" }] }]
              }
            }
            """);

        var extensions = new ExtensionLoader([directory.Path]).Load();

        Assert.Contains("sample", extensions.Executors.Keys);
        Assert.Contains("sample:lookup", extensions.FunctionExecutors.Keys);
        Assert.Equal("value", Assert.Single(extensions.Schema.Functions).Parameters[0].Name);
    }

    [Fact]
    public void RejectsRequiredFunctionParametersAfterOptionalParameters()
    {
        using var directory = new TemporaryDirectory();
        var extensionDirectory = Path.Combine(directory.Path, "sample");
        Directory.CreateDirectory(extensionDirectory);
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), """
            {
              "id": "sample",
              "name": "Sample extension",
              "namespace": "sample",
              "schema": {
                "lists": [],
                "commands": [],
                "functions": [{
                  "name": "lookup",
                  "kind": "httpGet",
                  "parameters": [{ "name": "optional", "optional": true }, { "name": "required" }],
                  "targetTemplate": "https://example.test/<required>"
                }]
              }
            }
            """);

        Assert.Throws<InvalidDataException>(() => new ExtensionLoader([directory.Path]).Load());
    }

    [Fact]
    public void RejectsUnsupportedFunctionKindsInExtensionManifests()
    {
        using var directory = new TemporaryDirectory();
        var extensionDirectory = Path.Combine(directory.Path, "sample");
        Directory.CreateDirectory(extensionDirectory);
        File.WriteAllText(Path.Combine(extensionDirectory, "extension.json"), """
            {
              "id": "sample",
              "name": "Sample extension",
              "namespace": "sample",
              "schema": {
                "lists": [],
                "commands": [],
                "functions": [{
                  "name": "temp",
                  "kind": "weatherTemp",
                  "parameters": [{ "name": "zip" }]
                }]
              }
            }
            """);

        Assert.Throws<InvalidDataException>(() => new ExtensionLoader([directory.Path]).Load());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
            => Directory.Delete(Path, recursive: true);
    }
}

public sealed class TestExtensionExecutor : ICommandExecutor
{
    public string Kind => "sample";

    public CommandExecutionResult Execute(ParsedCommand command)
        => new(true, "Executed by extension.");
}

public sealed class TestExtensionFunctionExecutor : IExtensionFunctionExecutor
{
    public string FunctionName => "lookup";

    public Task<string> ExecuteAsync(
        CommandFunctionDefinition function,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
        => Task.FromResult(arguments[0]);
}
