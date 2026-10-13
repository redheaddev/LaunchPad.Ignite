using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchPad.Ignite.App.Models;

namespace LaunchPad.Ignite.App.Services;

public sealed class SnippetStore
{
    private const string MetadataFileName = "metadata.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _snippetsDirectory;

    public SnippetStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "snippets"))
    {
    }

    public SnippetStore(string snippetsDirectory)
    {
        _snippetsDirectory = snippetsDirectory;
    }

    public IReadOnlyList<SnippetDefinition> GetAll()
    {
        if (!Directory.Exists(_snippetsDirectory))
        {
            return Array.Empty<SnippetDefinition>();
        }

        return Directory.EnumerateDirectories(_snippetsDirectory)
            .Select(ReadSnippet)
            .OrderBy(snippet => snippet.Metadata.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetNames()
    {
        if (!Directory.Exists(_snippetsDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(_snippetsDirectory)
            .Select(directory => ReadMetadata(directory).Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public SnippetDefinition? Find(string name)
        => GetAll().FirstOrDefault(snippet => string.Equals(
            snippet.Metadata.Name,
            name,
            StringComparison.OrdinalIgnoreCase));

    public string GetScriptPath(SnippetDefinition snippet)
        => Path.Combine(GetSnippetDirectory(snippet.Metadata.Id), GetScriptFileName(snippet.Metadata.Type));

    public SnippetDefinition Save(
        string name,
        string type,
        IEnumerable<string> arguments,
        IEnumerable<string> allowedSecrets,
        string script)
    {
        var normalizedName = NormalizeName(name);
        var normalizedType = NormalizeType(type);
        var normalizedArguments = NormalizeIdentifiers(arguments, "argument");
        var normalizedSecrets = NormalizeIdentifiers(allowedSecrets, "allowed secret");
        if (normalizedArguments.Intersect(normalizedSecrets, StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new ArgumentException("Snippet argument names must not overlap allowed secret names.");
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            throw new ArgumentException("Snippet script cannot be empty.", nameof(script));
        }

        var existing = Find(normalizedName);
        var metadata = new SnippetMetadata(
            existing?.Metadata.Id ?? Guid.NewGuid().ToString("N"),
            normalizedName,
            normalizedType,
            normalizedArguments,
            normalizedSecrets);
        var directory = GetSnippetDirectory(metadata.Id);
        Directory.CreateDirectory(directory);
        var metadataPath = Path.Combine(directory, MetadataFileName);
        var scriptPath = Path.Combine(directory, GetScriptFileName(metadata.Type));
        var metadataTemporaryPath = metadataPath + ".tmp";
        var scriptTemporaryPath = scriptPath + ".tmp";
        var previousScriptPath = existing is null
            ? null
            : Path.Combine(directory, GetScriptFileName(existing.Metadata.Type));
        try
        {
            File.WriteAllText(scriptTemporaryPath, script);
            File.WriteAllText(metadataTemporaryPath, JsonSerializer.Serialize(metadata, JsonOptions));
            File.Move(scriptTemporaryPath, scriptPath, overwrite: true);
            File.Move(metadataTemporaryPath, metadataPath, overwrite: true);
            if (previousScriptPath is not null
                && !string.Equals(previousScriptPath, scriptPath, StringComparison.OrdinalIgnoreCase))
            {
                DeleteIfExists(previousScriptPath);
            }
        }
        finally
        {
            DeleteIfExists(metadataTemporaryPath);
            DeleteIfExists(scriptTemporaryPath);
        }

        return new SnippetDefinition(metadata, script);
    }

    public bool Remove(string name)
    {
        var snippet = Find(name);
        if (snippet is null)
        {
            return false;
        }

        Directory.Delete(GetSnippetDirectory(snippet.Metadata.Id), recursive: true);
        return true;
    }

    public bool Rename(string name, string newName)
    {
        var snippet = Find(name);
        if (snippet is null)
        {
            return false;
        }

        var normalizedName = NormalizeName(newName);
        var conflictingSnippet = Find(normalizedName);
        if (conflictingSnippet is not null
            && !string.Equals(conflictingSnippet.Metadata.Id, snippet.Metadata.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"A snippet named '{normalizedName}' already exists.", nameof(newName));
        }

        var renamedMetadata = snippet.Metadata with { Name = normalizedName };
        var metadataPath = Path.Combine(GetSnippetDirectory(snippet.Metadata.Id), MetadataFileName);
        var temporaryPath = metadataPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(renamedMetadata, JsonOptions));
            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }

        return true;
    }

    private SnippetDefinition ReadSnippet(string directory)
    {
        var metadata = ReadMetadata(directory);
        var scriptPath = Path.Combine(directory, GetScriptFileName(metadata.Type));
        return new SnippetDefinition(metadata, File.ReadAllText(scriptPath));
    }

    private SnippetMetadata ReadMetadata(string directory)
    {
        var metadataPath = Path.Combine(directory, MetadataFileName);
        var metadata = JsonSerializer.Deserialize<SnippetMetadata>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidDataException($"Snippet metadata in '{metadataPath}' is invalid.");
        if (!Guid.TryParseExact(metadata.Id, "N", out _)
            || !string.Equals(Path.GetFullPath(directory), Path.GetFullPath(GetSnippetDirectory(metadata.Id)), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Snippet ID in '{metadataPath}' is invalid.");
        }

        try
        {
            ValidateMetadata(metadata);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Snippet metadata in '{metadataPath}' is invalid.", exception);
        }

        return metadata;
    }

    private string GetSnippetDirectory(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new ArgumentException("Snippet ID is invalid.", nameof(id));
        }

        return Path.Combine(_snippetsDirectory, id);
    }

    private static void ValidateMetadata(SnippetMetadata metadata)
    {
        if (metadata.Arguments is null || metadata.AllowedSecrets is null)
        {
            throw new ArgumentException("Snippet argument and secret lists are required.");
        }

        NormalizeName(metadata.Name);
        NormalizeType(metadata.Type);
        var arguments = NormalizeIdentifiers(metadata.Arguments, "argument");
        var secrets = NormalizeIdentifiers(metadata.AllowedSecrets, "allowed secret");
        if (arguments.Intersect(secrets, StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new ArgumentException("Snippet argument names must not overlap allowed secret names.");
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 64
            || !IsIdentifierStart(name[0])
            || name.Skip(1).Any(character => !IsIdentifierStart(character) && !char.IsAsciiDigit(character) && character != '-'))
        {
            throw new ArgumentException("Snippet names must start with a letter or underscore and contain only letters, digits, underscores, or hyphens.", nameof(name));
        }

        return name;
    }

    private static string NormalizeType(string type)
    {
        if (string.Equals(type, "powershell", StringComparison.OrdinalIgnoreCase))
        {
            return "powershell";
        }

        if (string.Equals(type, "shell", StringComparison.OrdinalIgnoreCase))
        {
            return "shell";
        }

        throw new ArgumentException("Snippet type must be 'powershell' or 'shell'.", nameof(type));
    }

    private static IReadOnlyList<string> NormalizeIdentifiers(IEnumerable<string> identifiers, string description)
    {
        var normalized = identifiers
            .Select(identifier => identifier.Trim())
            .Where(identifier => identifier.Length > 0)
            .ToArray();
        if (normalized.Any(identifier => !IsEnvironmentIdentifier(identifier)))
        {
            throw new ArgumentException($"Each {description} must be a valid environment-variable name.", nameof(identifiers));
        }

        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new ArgumentException($"Snippet {description}s must not be repeated.", nameof(identifiers));
        }

        return normalized;
    }

    private static bool IsEnvironmentIdentifier(string identifier)
        => identifier.Length is > 0 and <= 128
            && IsIdentifierStart(identifier[0])
            && identifier.Skip(1).All(character => IsIdentifierStart(character) || char.IsAsciiDigit(character));

    private static string GetScriptFileName(string type)
        => string.Equals(type, "powershell", StringComparison.OrdinalIgnoreCase) ? "script.ps1" : "script.sh";

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
