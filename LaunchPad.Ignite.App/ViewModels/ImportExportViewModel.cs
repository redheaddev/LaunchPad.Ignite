using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.ViewModels;

public sealed class ImportExportViewModel : ViewModelBase
{
    private readonly CommandOwnershipManager _ownershipManager;
    private readonly Func<string, Task<CommandExecutionResult>> _importPack;
    private readonly Action _close;
    private readonly AddonPackageService _addonPackageService;
    private readonly Action _runtimeRefresh;
    private readonly Func<AddonImportPreview, Task<bool>> _confirmAddonReplacement;
    private readonly ObservableCollection<CommandSourceViewModel> _sources = [];
    private CommandDestinationViewModel _selectedDestination;
    private string _importPath = string.Empty;
    private string _importStatus = string.Empty;
    private string _movePreview = "Select commands to move.";
    private string _moveStatus = string.Empty;
    private string _exportPackId = "exported-pack";
    private string _exportNamespace = "exported";
    private string _exportPreview = "Select commands to preview the pack export.";
    private string _exportStatus = string.Empty;
    private string _addonPath = string.Empty;
    private string _addonPreview = "Choose an add-on ZIP to preview its contents.";
    private string _addonStatus = string.Empty;
    private AddonImportPreview? _addonImportPreview;
    private bool _isWorking;
    private CommandPackExportPlan? _exportPlan;

    public ObservableCollection<CommandSourceViewModel> Sources => _sources;
    public IReadOnlyList<CommandDestinationViewModel> Destinations { get; }
    public CommandDestinationViewModel SelectedDestination
    {
        get => _selectedDestination;
        set
        {
            if (SetProperty(ref _selectedDestination, value))
            {
                RefreshMovePreview();
            }
        }
    }
    public string ImportPath
    {
        get => _importPath;
        set => SetProperty(ref _importPath, value);
    }
    public string ImportStatus
    {
        get => _importStatus;
        private set => SetProperty(ref _importStatus, value);
    }
    public string MovePreview
    {
        get => _movePreview;
        private set => SetProperty(ref _movePreview, value);
    }
    public string MoveStatus
    {
        get => _moveStatus;
        private set => SetProperty(ref _moveStatus, value);
    }
    public bool CanMove { get; private set; }
    public string ExportPackId
    {
        get => _exportPackId;
        set
        {
            if (SetProperty(ref _exportPackId, value))
            {
                RefreshExportPreview();
            }
        }
    }
    public string ExportNamespace
    {
        get => _exportNamespace;
        set
        {
            if (SetProperty(ref _exportNamespace, value))
            {
                RefreshExportPreview();
            }
        }
    }
    public string ExportPreview
    {
        get => _exportPreview;
        private set => SetProperty(ref _exportPreview, value);
    }
    public string ExportStatus
    {
        get => _exportStatus;
        private set => SetProperty(ref _exportStatus, value);
    }
    public bool CanExport => _exportPlan is not null;
    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (SetProperty(ref _isWorking, value))
            {
                ImportCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public IAsyncRelayCommand ImportCommand { get; }
    public IAsyncRelayCommand ImportAddonCommand { get; }
    public string AddonPath
    {
        get => _addonPath;
        set
        {
            if (SetProperty(ref _addonPath, value))
            {
                RefreshAddonPreview();
            }
        }
    }
    public string AddonPreview
    {
        get => _addonPreview;
        private set => SetProperty(ref _addonPreview, value);
    }
    public string AddonStatus
    {
        get => _addonStatus;
        private set => SetProperty(ref _addonStatus, value);
    }
    public IRelayCommand MoveCommand { get; }
    public int SelectedCommandCount => _sources.SelectMany(source => source.Commands).Count(command => command.IsSelected);

    public event Action? CloseRequested;

    public ImportExportViewModel(
        CommandOwnershipManager ownershipManager,
        Func<string, Task<CommandExecutionResult>> importPack,
        Action close,
        AddonPackageService addonPackageService,
        Action runtimeRefresh,
        Func<AddonImportPreview, Task<bool>> confirmAddonReplacement)
    {
        _ownershipManager = ownershipManager;
        _importPack = importPack;
        _close = close;
        _addonPackageService = addonPackageService;
        _runtimeRefresh = runtimeRefresh;
        _confirmAddonReplacement = confirmAddonReplacement;
        ImportCommand = new AsyncRelayCommand(ImportPackFromPathAsync, () => !IsWorking);
        ImportAddonCommand = new AsyncRelayCommand(ImportAddonAsync, () => !IsWorking && _addonImportPreview is not null);
        MoveCommand = new RelayCommand(MoveSelectedCommands);
        Destinations = new[] { new CommandDestinationViewModel(CommandOwnershipManager.UserOwnerId, "User commands", null) }
            .Concat(ownershipManager.EnabledPacks.Select(pack => new CommandDestinationViewModel(pack.Id, $"Pack: {pack.Id}", pack.Id)))
            .ToArray();
        _selectedDestination = Destinations[0];
        RefreshInventory();
    }

    private void RefreshAddonPreview()
    {
        _addonImportPreview = null;
        if (string.IsNullOrWhiteSpace(AddonPath))
        {
            AddonPreview = "Choose an add-on ZIP to preview its contents.";
        }
        else
        {
            try
            {
                _addonImportPreview = _addonPackageService.Preview(AddonPath.Trim());
                var lines = new List<string>
                {
                    $"{_addonImportPreview.Manifest.Name} ({_addonImportPreview.Manifest.Id}) v{_addonImportPreview.Manifest.Version}",
                    $"Command packs: {string.Join(", ", _addonImportPreview.PackIds)}",
                    $"Snippets: {string.Join(", ", _addonImportPreview.SnippetNames)}",
                    $"Extensions: {string.Join(", ", _addonImportPreview.ExtensionIds)}"
                };
                if (_addonImportPreview.Conflicts.Count > 0)
                {
                    lines.Add($"Will ask before replacing: {string.Join(", ", _addonImportPreview.Conflicts)}");
                }

                AddonPreview = string.Join(Environment.NewLine, lines);
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or ArgumentException or JsonException)
            {
                AddonPreview = $"Could not read add-on: {exception.Message}";
            }
        }

        ImportAddonCommand.NotifyCanExecuteChanged();
    }

    private async Task ImportAddonAsync()
    {
        if (_addonImportPreview is null || string.IsNullOrWhiteSpace(AddonPath))
        {
            return;
        }

        IsWorking = true;
        try
        {
            var replaceExisting = _addonImportPreview.Conflicts.Count == 0
                || await _confirmAddonReplacement(_addonImportPreview);
            if (!replaceExisting)
            {
                AddonStatus = "Add-on import canceled.";
                return;
            }

            _addonPackageService.Install(AddonPath.Trim(), _addonImportPreview, replaceExisting: true, _runtimeRefresh);
            AddonStatus = $"Installed add-on '{_addonImportPreview.Manifest.Name}'.";
            CloseRequested?.Invoke();
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            AddonStatus = $"Could not install add-on: {exception.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    public void RefreshInventory()
    {
        _sources.Clear();
        var inventory = _ownershipManager.GetEligibleCommands();
        var groups = inventory.GroupBy(item => item.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var userItems = groups.TryGetValue(CommandOwnershipManager.UserOwnerId, out var foundUserItems)
            ? foundUserItems
            : Array.Empty<CommandOwnershipItem>();
        _sources.Add(CreateSource(CommandOwnershipManager.UserOwnerId, "User commands", userItems));
        foreach (var pack in _ownershipManager.EnabledPacks)
        {
            var packItems = groups.TryGetValue(pack.Id, out var foundPackItems)
                ? foundPackItems
                : Array.Empty<CommandOwnershipItem>();
            _sources.Add(CreateSource(pack.Id, $"Pack: {pack.Id}", packItems));
        }

        OnPropertyChanged(nameof(SelectedCommandCount));
        RefreshMovePreview();
        RefreshExportPreview();
    }

    private CommandSourceViewModel CreateSource(string ownerId, string name, CommandOwnershipItem[] items)
    {
        CommandSourceViewModel? source = null;
        var commands = items.Select(item => new CommandSelectionViewModel(item, () =>
        {
            source?.RefreshSelectionState();
            OnSelectionChanged();
        })).ToArray();
        source = new CommandSourceViewModel(ownerId, name, commands);
        return source;
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCommandCount));
        RefreshMovePreview();
        RefreshExportPreview();
    }

    private CommandOwnershipKey[] GetSelectedCommandKeys()
        => _sources.SelectMany(source => source.Commands)
            .Where(command => command.IsSelected)
            .Select(command => new CommandOwnershipKey(command.Item.OwnerId, command.Item.QualifiedName))
            .ToArray();

    private void RefreshMovePreview()
    {
        var keys = GetSelectedCommandKeys();
        if (keys.Length == 0)
        {
            CanMove = false;
            MovePreview = "Select commands to move.";
            OnPropertyChanged(nameof(CanMove));
            return;
        }

        var preview = _ownershipManager.PreviewMove(keys, SelectedDestination.PackId);
        CanMove = preview.CanCommit;
        MovePreview = preview.Conflicts.Count > 0
            ? $"Conflicts prevent this move:{Environment.NewLine}{string.Join(Environment.NewLine, preview.Conflicts)}"
            : preview.CommandsToMove.Count == 0
                ? "The selected commands already belong to this destination."
                : $"Move {preview.CommandsToMove.Count} command(s) to {SelectedDestination.Name}?";
        OnPropertyChanged(nameof(CanMove));
    }

    private void RefreshExportPreview()
    {
        _exportPlan = null;
        var keys = GetSelectedCommandKeys();
        if (keys.Length == 0)
        {
            ExportPreview = "Select commands to preview the pack export.";
            OnPropertyChanged(nameof(CanExport));
            return;
        }

        try
        {
            _exportPlan = _ownershipManager.CreateExportPlan(keys, ExportPackId, ExportNamespace);
            var lines = new[]
            {
                $"Commands: {string.Join(", ", _exportPlan.ExportedCommandNames)}",
                $"Included lists: {_exportPlan.Dependencies.Lists.Count}; variables: {_exportPlan.Dependencies.Variables.Count}."
            }.ToList();
            if (_exportPlan.Dependencies.UnresolvedListReferences.Count > 0)
            {
                lines.Add($"External/unresolved lists: {string.Join(", ", _exportPlan.Dependencies.UnresolvedListReferences)}");
            }

            if (_exportPlan.Dependencies.UnresolvedVariableReferences.Count > 0)
            {
                lines.Add($"External/unresolved variables: {string.Join(", ", _exportPlan.Dependencies.UnresolvedVariableReferences)}");
            }

            ExportPreview = string.Join(Environment.NewLine, lines);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException)
        {
            ExportPreview = exception.Message;
        }

        OnPropertyChanged(nameof(CanExport));
    }

    public void ExportToFile(string path)
    {
        if (_exportPlan is null)
        {
            ExportStatus = "Select valid commands and pack settings before exporting.";
            return;
        }

        try
        {
            _ownershipManager.WriteExportManifest(_exportPlan, path);
            ExportStatus = $"Exported pack '{_exportPlan.Manifest.Id}'.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            ExportStatus = $"Could not export pack: {exception.Message}";
        }
    }

    public async Task ImportPackFromPathAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportPath))
        {
            ImportStatus = "Choose a command-pack JSON file first.";
            return;
        }

        IsWorking = true;
        try
        {
            var result = await _importPack(ImportPath.Trim());
            ImportStatus = result.Message;
            if (result.Succeeded)
            {
                CloseRequested?.Invoke();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            ImportStatus = $"Could not import pack: {exception.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    public void MoveSelectedCommands()
    {
        var result = _ownershipManager.MoveCommands(GetSelectedCommandKeys(), SelectedDestination.PackId);
        MoveStatus = result.Message;
        if (result.Succeeded)
        {
            RefreshInventory();
        }
    }

    public void Close() => _close();
}

public sealed class CommandSourceViewModel : ViewModelBase
{
    public string OwnerId { get; }
    public string Name { get; }
    public IReadOnlyList<CommandSelectionViewModel> Commands { get; }
    public IRelayCommand ToggleAllCommand { get; }
    public string ToggleAllText => Commands.Any(command => !command.IsSelected) ? "Select all" : "Clear selection";

    public CommandSourceViewModel(string ownerId, string name, IReadOnlyList<CommandSelectionViewModel> commands)
    {
        OwnerId = ownerId;
        Name = name;
        Commands = commands;
        ToggleAllCommand = new RelayCommand(ToggleAll);
    }

    public void RefreshSelectionState()
        => OnPropertyChanged(nameof(ToggleAllText));

    private void ToggleAll()
    {
        var selectAll = Commands.Any(command => !command.IsSelected);
        foreach (var command in Commands)
        {
            command.IsSelected = selectAll;
        }

        RefreshSelectionState();
    }
}

public sealed class CommandSelectionViewModel : ViewModelBase
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public CommandOwnershipItem Item { get; }
    public string QualifiedName => Item.QualifiedName;
    public string Description => Item.Command.Description;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged();
            }
        }
    }

    public CommandSelectionViewModel(CommandOwnershipItem item, Action selectionChanged)
    {
        Item = item;
        _selectionChanged = selectionChanged;
    }
}

public sealed record CommandDestinationViewModel(string OwnerId, string Name, string? PackId);
