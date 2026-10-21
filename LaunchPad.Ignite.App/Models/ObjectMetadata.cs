using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LaunchPad.Ignite.App.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MetadataObjectType
{
    Command,
    Function,
    List,
    Variable,
    Macro,
    Snippet,
    Shortcut,
    Secret
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MetadataFieldType
{
    Text,
    Date,
    Number,
    YesNo,
    Choices
}

public sealed record MetadataFieldDefinition(
    string Id,
    string Name,
    MetadataFieldType Type,
    IReadOnlyList<string>? Choices = null);

public sealed record ObjectMetadataSchema(
    MetadataObjectType ObjectType,
    IReadOnlyList<MetadataFieldDefinition> Fields);

public sealed record ObjectMetadataRecord(
    MetadataObjectType ObjectType,
    string ObjectId,
    IReadOnlyDictionary<string, string> Values);
