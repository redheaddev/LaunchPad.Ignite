using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Profiles;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class ObjectMetadataEditorViewModel : ObservableObject
{
    private static readonly MetadataFieldType[] AvailableFieldTypes = Enum.GetValues<MetadataFieldType>();
    private static readonly string[] BooleanValues = ["true", "false"];
    private readonly ProfileSettingsStore _settingsStore;
    private readonly IReadOnlyList<ObjectMetadataTarget> _targets;
    private MetadataFieldDefinition? _selectedField;
    private MetadataFieldType _selectedFieldType = MetadataFieldType.Text;
    private ObjectMetadataTarget? _selectedTarget;
    private string _fieldName = string.Empty;
    private string _choicesText = string.Empty;
    private string _status = string.Empty;
    private IReadOnlyList<ObjectMetadataValueEditorViewModel> _values = [];

    public ObjectMetadataEditorViewModel(ProfileSettingsStore settingsStore, MetadataObjectType objectType, IReadOnlyList<ObjectMetadataTarget> targets)
    {
        _settingsStore = settingsStore;
        ObjectType = objectType;
        _targets = targets.OrderBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        Fields = new ObservableCollection<MetadataFieldDefinition>(_settingsStore.GetMetadataSchema(objectType).Fields);
        SelectedTarget = _targets.FirstOrDefault();
        RefreshValues();
        RefreshTargetsWithMetadata();
    }

    public MetadataObjectType ObjectType { get; }
    public IReadOnlyList<MetadataFieldType> FieldTypes => AvailableFieldTypes;
    public IReadOnlyList<ObjectMetadataTarget> Targets => _targets;
    public ObservableCollection<ObjectMetadataTargetViewModel> TargetsWithMetadata { get; } = [];
    public ObservableCollection<MetadataFieldDefinition> Fields { get; }
    public IReadOnlyList<ObjectMetadataValueEditorViewModel> Values => _values;
    public bool HasTargets => _targets.Count > 0;
    public bool HasNoTargets => _targets.Count == 0;
    public bool HasNoMetadataTargets => TargetsWithMetadata.Count == 0;
    public bool IsChoicesType => SelectedFieldType == MetadataFieldType.Choices;

    public ObjectMetadataTarget? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                RefreshValues();
                OnPropertyChanged(nameof(SelectedTargetWithMetadata));
            }
        }
    }

    public ObjectMetadataTargetViewModel? SelectedTargetWithMetadata
    {
        get => TargetsWithMetadata.FirstOrDefault(target =>
            string.Equals(target.Id, SelectedTarget?.Id, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null)
            {
                SelectedTarget = null;
                return;
            }

            SelectedTarget = value.Target;
        }
    }

    public MetadataFieldDefinition? SelectedField
    {
        get => _selectedField;
        set
        {
            if (SetProperty(ref _selectedField, value))
            {
                FieldName = value?.Name ?? string.Empty;
                ChoicesText = string.Join(Environment.NewLine, value?.Choices ?? []);
                SelectedFieldType = value?.Type ?? MetadataFieldType.Text;
            }
        }
    }

    public MetadataFieldType SelectedFieldType
    {
        get => _selectedFieldType;
        set
        {
            if (SetProperty(ref _selectedFieldType, value))
            {
                OnPropertyChanged(nameof(IsChoicesType));
            }
        }
    }

    public string FieldName { get => _fieldName; set => SetProperty(ref _fieldName, value); }
    public string ChoicesText { get => _choicesText; set => SetProperty(ref _choicesText, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool SaveField()
    {
        var name = FieldName.Trim();
        if (!Regex.IsMatch(name, "^[a-zA-Z][a-zA-Z0-9 _-]{0,39}$", RegexOptions.CultureInvariant))
        {
            Status = "Enter a field name beginning with a letter and containing only letters, numbers, spaces, hyphens, or underscores.";
            return false;
        }

        var id = SelectedField?.Id ?? Regex.Replace(name, "[^a-zA-Z0-9]+", "-").Trim('-').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(id) || Fields.Any(field => !string.Equals(field.Id, SelectedField?.Id, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(field.Id, id, StringComparison.OrdinalIgnoreCase))))
        {
            Status = "A field with that name or identifier already exists.";
            return false;
        }

        var choices = SelectedFieldType == MetadataFieldType.Choices
            ? ChoicesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(choice => choice.Trim()).Where(choice => choice.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (SelectedFieldType == MetadataFieldType.Choices && choices.Length == 0)
        {
            Status = "Enter at least one choice, one per line.";
            return false;
        }

        var field = new MetadataFieldDefinition(id, name, SelectedFieldType, choices);
        if (SelectedField is null)
        {
            Fields.Add(field);
        }
        else
        {
            Fields[Fields.IndexOf(SelectedField)] = field;
        }

        SaveSchema();
        SelectedField = field;
        RefreshValues();
        Status = "Field saved.";
        return true;
    }

    public void AddField()
    {
        SelectedField = null;
        SelectedFieldType = MetadataFieldType.Text;
        FieldName = string.Empty;
        ChoicesText = string.Empty;
    }

    public void DeleteField()
    {
        if (SelectedField is null)
        {
            return;
        }

        var id = SelectedField.Id;
        Fields.Remove(SelectedField);
        foreach (var target in _targets)
        {
            var record = _settingsStore.GetObjectMetadata(ObjectType, target.Id);
            var values = new Dictionary<string, string>(record.Values, StringComparer.OrdinalIgnoreCase);
            values.Remove(id);
            _settingsStore.SetObjectMetadata(record with { Values = values });
        }

        SaveSchema();
        SelectedField = null;
        RefreshValues();
        RefreshTargetsWithMetadata();
        Status = "Field deleted.";
    }

    private void SaveSchema()
        => _settingsStore.SetMetadataSchema(new ObjectMetadataSchema(ObjectType, Fields.ToArray()));

    private void RefreshValues()
    {
        var record = SelectedTarget is null
            ? null
            : _settingsStore.GetObjectMetadata(ObjectType, SelectedTarget.Id);
        _values = Fields.Select(field => new ObjectMetadataValueEditorViewModel(
            field,
            record?.Values.GetValueOrDefault(field.Id, string.Empty) ?? string.Empty,
            (id, value) => SaveValue(id, value))).ToArray();
        OnPropertyChanged(nameof(Values));
    }

    private void SaveValue(string fieldId, string value)
    {
        if (SelectedTarget is null)
        {
            return;
        }

        var field = Fields.FirstOrDefault(candidate => string.Equals(candidate.Id, fieldId, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            Status = "The selected field no longer exists.";
            return;
        }

        if (!ValidateValue(field, value, out var error))
        {
            Status = error!;
            return;
        }

        var record = _settingsStore.GetObjectMetadata(ObjectType, SelectedTarget.Id);
        var values = new Dictionary<string, string>(record.Values, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            values.Remove(fieldId);
        }
        else
        {
            values[fieldId] = value;
        }

        _settingsStore.SetObjectMetadata(record with { Values = values });
        RefreshTargetsWithMetadata();
        Status = "Metadata saved.";
    }

    private void RefreshTargetsWithMetadata()
    {
        var targets = _targets
            .Select(target => new
            {
                Target = target,
                Values = _settingsStore.GetObjectMetadata(ObjectType, target.Id).Values
            })
            .Where(item => item.Values.Count > 0)
            .Select(item => new ObjectMetadataTargetViewModel(
                item.Target,
                item.Values.Select(pair => new ObjectMetadataSummaryViewModel(
                    Fields.FirstOrDefault(field => string.Equals(field.Id, pair.Key, StringComparison.OrdinalIgnoreCase))?.Name ?? pair.Key,
                    pair.Value)).ToArray()))
            .ToArray();
        TargetsWithMetadata.Clear();
        foreach (var target in targets)
        {
            TargetsWithMetadata.Add(target);
        }

        OnPropertyChanged(nameof(HasNoMetadataTargets));
        OnPropertyChanged(nameof(SelectedTargetWithMetadata));
    }

    private static bool ValidateValue(MetadataFieldDefinition field, string value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var valid = field.Type switch
        {
            MetadataFieldType.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            MetadataFieldType.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            MetadataFieldType.YesNo => bool.TryParse(value, out _),
            MetadataFieldType.Choices => field.Choices?.Contains(value, StringComparer.OrdinalIgnoreCase) == true,
            _ => true
        };
        if (!valid)
        {
            error = field.Type == MetadataFieldType.Date
                ? "Enter a valid date in yyyy-MM-dd format."
                : $"Enter a valid {field.Type.ToString().ToLowerInvariant()} value.";
        }

        return valid;
    }
}

public sealed record ObjectMetadataTargetViewModel(
    ObjectMetadataTarget Target,
    IReadOnlyList<ObjectMetadataSummaryViewModel> SummaryValues)
{
    public string Id => Target.Id;
    public string DisplayName => Target.DisplayName;
}

public sealed record ObjectMetadataSummaryViewModel(string FieldName, string Value)
{
    public string Summary => $"{FieldName}: {Value}";
}

public sealed partial class ObjectMetadataValueEditorViewModel : ObservableObject
{
    private static readonly string[] BooleanValues = ["true", "false"];
    private readonly Action<string, string> _save;
    private string _value;

    public ObjectMetadataValueEditorViewModel(MetadataFieldDefinition field, string value, Action<string, string> save)
    {
        Field = field;
        _value = value;
        _save = save;
    }

    public MetadataFieldDefinition Field { get; }
    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                _save(Field.Id, value);
            }
        }
    }
    public bool IsYesNo => Field.Type == MetadataFieldType.YesNo;
    public bool IsChoices => Field.Type == MetadataFieldType.Choices;
    public bool IsText => Field.Type is MetadataFieldType.Text or MetadataFieldType.Number or MetadataFieldType.Date;
    public bool IsNotes => string.Equals(Field.Id, "notes", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<string> Choices => Field.Choices ?? [];
    public IReadOnlyList<string> BooleanChoices => BooleanValues;
}
