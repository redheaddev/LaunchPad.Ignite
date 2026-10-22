using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services.Profiles;

namespace LaunchPad.Ignite.App.Services.Macros;

public sealed class MacroStore
{
    private const string MetadataFileName = "metadata.json";
    private const string ScriptFileName = "script.txt";
    private const string DefaultNamespaceDirectory = "_default";
    private static readonly Regex NamespacePattern = new("^[a-z][a-z0-9-]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _macrosDirectory;
    private readonly ProfileSettingsStore? _profileSettingsStore;

    public MacroStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "macros"))
    {
    }

    public MacroStore(string macrosDirectory, ProfileSettingsStore? profileSettingsStore = null)
    {
        _macrosDirectory = macrosDirectory;
        _profileSettingsStore = profileSettingsStore;
    }

    public IReadOnlyList<MacroDefinition> GetAll()
    {
        if (!Directory.Exists(_macrosDirectory))
        {
            return Array.Empty<MacroDefinition>();
        }

        return Directory.EnumerateDirectories(_macrosDirectory)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(ReadMacro)
            .OrderBy(macro => macro.Metadata.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public MacroDefinition? Find(string nameOrQualifiedName)
    {
        var separator = nameOrQualifiedName.IndexOf(':');
        var name = separator < 0 ? nameOrQualifiedName : nameOrQualifiedName[(separator + 1)..];
        var macroNamespace = separator < 0 ? null : nameOrQualifiedName[..separator];
        return GetAll().FirstOrDefault(macro =>
            string.Equals(macro.Metadata.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(macro.Metadata.Namespace, macroNamespace, StringComparison.OrdinalIgnoreCase));
    }

    public MacroDefinition Save(MacroDefinition macro)
    {
        Validate(macro);
        var existing = Find(macro.Metadata.QualifiedName);
        var metadata = macro.Metadata with { Id = existing?.Metadata.Id ?? macro.Metadata.Id };
        if (string.IsNullOrWhiteSpace(metadata.Id))
        {
            metadata = metadata with { Id = Guid.NewGuid().ToString("N") };
        }

        var normalized = new MacroDefinition(metadata, macro.Script);
        var directory = GetMacroDirectory(metadata.Namespace, metadata.Name);
        Directory.CreateDirectory(directory);
        var metadataPath = Path.Combine(directory, MetadataFileName);
        var scriptPath = Path.Combine(directory, ScriptFileName);
        var metadataTemporaryPath = metadataPath + ".tmp";
        var scriptTemporaryPath = scriptPath + ".tmp";
        try
        {
            File.WriteAllText(scriptTemporaryPath, normalized.Script);
            File.WriteAllText(metadataTemporaryPath, JsonSerializer.Serialize(normalized.Metadata, JsonOptions));
            File.Move(scriptTemporaryPath, scriptPath, overwrite: true);
            File.Move(metadataTemporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(metadataTemporaryPath);
            DeleteIfExists(scriptTemporaryPath);
        }

        return normalized;
    }

    public bool Remove(string nameOrQualifiedName)
    {
        var macro = Find(nameOrQualifiedName);
        if (macro is null)
        {
            return false;
        }

        Directory.Delete(GetMacroDirectory(macro.Metadata.Namespace, macro.Metadata.Name), recursive: true);
        _profileSettingsStore?.RemoveObjectMetadata(MetadataObjectType.Macro, macro.Metadata.Id);
        return true;
    }

    public MacroDefinition Validate(MacroDefinition macro)
    {
        if (!Guid.TryParseExact(macro.Metadata.Id, "N", out _) && !string.IsNullOrWhiteSpace(macro.Metadata.Id))
        {
            throw new ArgumentException("Macro ID is invalid.", nameof(macro));
        }

        ValidateMetadata(macro.Metadata);
        if (string.IsNullOrWhiteSpace(macro.Script))
        {
            throw new ArgumentException("Macro script cannot be empty.", nameof(macro));
        }

        return macro;
    }

    public static string ValidateMacroName(string name)
        => NormalizeName(name);

    private MacroDefinition ReadMacro(string directory)
    {
        var metadataPath = Path.Combine(directory, MetadataFileName);
        var metadata = JsonSerializer.Deserialize<MacroMetadata>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidDataException($"Macro metadata in '{metadataPath}' is invalid.");
        try
        {
            ValidateMetadata(metadata);
            if (!Guid.TryParseExact(metadata.Id, "N", out _)
                || !string.Equals(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)),
                    metadata.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Macro ID or directory name is invalid.");
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Macro metadata in '{metadataPath}' is invalid.", exception);
        }

        var expectedParent = metadata.Namespace ?? DefaultNamespaceDirectory;
        if (!string.Equals(
            Path.GetFileName(Path.GetDirectoryName(directory)),
            expectedParent,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Macro directory in '{metadataPath}' does not match its namespace.");
        }

        return new MacroDefinition(metadata, File.ReadAllText(Path.Combine(directory, ScriptFileName)));
    }

    private string GetMacroDirectory(string? macroNamespace, string name)
        => Path.Combine(_macrosDirectory, macroNamespace ?? DefaultNamespaceDirectory, NormalizeName(name));

    private static void ValidateMetadata(MacroMetadata metadata)
    {
        NormalizeName(metadata.Name);
        if (!string.IsNullOrWhiteSpace(metadata.Namespace)
            && !NamespacePattern.IsMatch(metadata.Namespace))
        {
            throw new ArgumentException("Macro namespace must contain only letters, digits, or hyphens and start with a letter.");
        }

        if (metadata.Arguments is null)
        {
            throw new ArgumentException("Macro arguments are required.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in metadata.Arguments)
        {
            if (!IsArgumentName(argument.Name) || !names.Add(argument.Name))
            {
                throw new ArgumentException("Macro argument names must be valid and unique.");
            }

            if (!Enum.IsDefined(argument.Type) || !Enum.IsDefined(argument.ListMode))
            {
                throw new ArgumentException($"Macro argument '{argument.Name}' has an invalid type or selection mode.");
            }

            if (argument.Type == MacroArgumentType.List && string.IsNullOrWhiteSpace(argument.ListName))
            {
                throw new ArgumentException($"Macro list argument '{argument.Name}' must specify a list name.");
            }
        }

        if (!Enum.IsDefined(metadata.OnError))
        {
            throw new ArgumentException("Macro error behavior is invalid.");
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 64
            || !IsIdentifierStart(name[0])
            || name.Skip(1).Any(character => !IsIdentifierStart(character) && !char.IsAsciiDigit(character) && character != '-'))
        {
            throw new ArgumentException("Macro names must start with a letter or underscore and contain only letters, digits, underscores, or hyphens.", nameof(name));
        }

        return name;
    }

    private static bool IsArgumentName(string name)
        => !string.IsNullOrWhiteSpace(name)
            && name.Length <= 64
            && IsIdentifierStart(name[0])
            && name.Skip(1).All(character => IsIdentifierStart(character) || char.IsAsciiDigit(character));

    private static bool IsIdentifierStart(char character)
        => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
