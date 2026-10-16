using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.ViewModels;

public sealed class ImportExportViewModel : ViewModelBase
{
    private readonly CommandOwnershipManager _ownershipManager;
    private readonly Func<string, bool, Task<CommandExecutionResult>> _importPack;
    private readonly Func<string, CommandPackPreview> _previewPack;
    private readonly Action _close;
    private readonly AddonPackageService _addonPackageService;
    private readonly Action _runtimeRefresh;
    private readonly ObservableCollection<CommandSourceViewModel> _sources = [];
    private CommandDestinationViewModel _selectedDestination;
    private string _importPath = string.Empty;
    private string _importStatus = string.Empty;
    private string _packPreviewId = string.Empty;
    private string _packPreviewNamespace = string.Empty;
    private string _packPreviewVersion = string.Empty;
    private string _packPreviewCommandCountText = string.Empty;
    private string _packPreviewMessage = "Choose a command-pack JSON file to preview its contents.";
    private bool _hasPackPreview;
    private int _importConflictPolicyIndex;
    private string _movePreview = "Select commands to move.";
    private string _moveStatus = string.Empty;
    private string _newPackId = "new-pack";
    private string _newPackNamespace = "new";
    private string _exportPackId = "exported-pack";
    private string _exportNamespace = "exported";
    private string _exportPreview = "Select commands to preview the pack export.";
    private string _exportStatus = string.Empty;
    private string _addonPath = string.Empty;
    private string _addonPreviewName = string.Empty;
    private string _addonPreviewId = string.Empty;
    private string _addonPreviewVersion = string.Empty;
    private string _addonPreviewMessage = "Choose an add-on ZIP to preview its contents.";
    private bool _hasAddonPreview;
    private string _addonStatus = string.Empty;
    private int _addonConflictPolicyIndex;
    private AddonImportPreview? _addonImportPreview;
    private bool _isWorking;
    private CommandPackExportPlan? _exportPlan;
    private string _addonId = "my-addon";
    private string _addonName = "My Add-on";
    private string _addonVersion = "1.0.0";
    private string _createAddonStatus = string.Empty;

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
    public string NewPackId
    {
        get => _newPackId;
        set
        {
            if (SetProperty(ref _newPackId, value))
            {
                RefreshMovePreview();
            }
        }
    }
    public string NewPackNamespace
    {
        get => _newPackNamespace;
        set
        {
            if (SetProperty(ref _newPackNamespace, value))
            {
                RefreshMovePreview();
            }
        }
    }
    public string ImportPath
    {
        get => _importPath;
        set
        {
            if (SetProperty(ref _importPath, value))
            {
                RefreshPackPreview();
            }
        }
    }
    public string ImportStatus
    {
        get => _importStatus;
        private set => SetProperty(ref _importStatus, value);
    }
    public string PackPreviewId { get => _packPreviewId; private set => SetProperty(ref _packPreviewId, value); }
    public string PackPreviewNamespace { get => _packPreviewNamespace; private set => SetProperty(ref _packPreviewNamespace, value); }
    public string PackPreviewVersion { get => _packPreviewVersion; private set => SetProperty(ref _packPreviewVersion, value); }
    public string PackPreviewCommandCountText { get => _packPreviewCommandCountText; private set => SetProperty(ref _packPreviewCommandCountText, value); }
    public string PackPreviewMessage { get => _packPreviewMessage; private set => SetProperty(ref _packPreviewMessage, value); }
    public bool HasPackPreview { get => _hasPackPreview; private set => SetProperty(ref _hasPackPreview, value); }
    public ObservableCollection<AddonPreviewItemViewModel> PackPreviewCommands { get; } = [];
    public int ImportConflictPolicyIndex
    {
        get => _importConflictPolicyIndex;
        set
        {
            if (SetProperty(ref _importConflictPolicyIndex, value))
            {
                RefreshPackCommandCounts();
            }
        }
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
    public string AddonPreviewName { get => _addonPreviewName; private set => SetProperty(ref _addonPreviewName, value); }
    public string AddonPreviewId { get => _addonPreviewId; private set => SetProperty(ref _addonPreviewId, value); }
    public string AddonPreviewVersion { get => _addonPreviewVersion; private set => SetProperty(ref _addonPreviewVersion, value); }
    public string AddonPreviewMessage { get => _addonPreviewMessage; private set => SetProperty(ref _addonPreviewMessage, value); }
    public bool HasAddonPreview { get => _hasAddonPreview; private set => SetProperty(ref _hasAddonPreview, value); }
    public ObservableCollection<AddonPreviewItemViewModel> AddonPreviewPacks { get; } = [];
    public ObservableCollection<AddonPreviewItemViewModel> AddonPreviewSnippets { get; } = [];
    public ObservableCollection<AddonPreviewItemViewModel> AddonPreviewExtensions { get; } = [];
    public string AddonStatus
    {
        get => _addonStatus;
        private set => SetProperty(ref _addonStatus, value);
    }
    public int AddonConflictPolicyIndex
    {
        get => _addonConflictPolicyIndex;
        set
        {
            if (SetProperty(ref _addonConflictPolicyIndex, value))
            {
                RefreshAddonCommandCounts();
            }
        }
    }
    public IRelayCommand MoveCommand { get; }
    public ObservableCollection<AddonPackGroupViewModel> AddonPackGroups { get; } = [];
    public ObservableCollection<AddonComponentSelectionViewModel> AddonSnippets { get; } = [];
    public ObservableCollection<AddonComponentSelectionViewModel> AddonExtensions { get; } = [];
    public string AddonId { get => _addonId; set => SetProperty(ref _addonId, value); }
    public string AddonName { get => _addonName; set => SetProperty(ref _addonName, value); }
    public string AddonVersion { get => _addonVersion; set => SetProperty(ref _addonVersion, value); }
    public string CreateAddonStatus { get => _createAddonStatus; private set => SetProperty(ref _createAddonStatus, value); }
    public IRelayCommand RefreshAddonComponentsCommand { get; }
    public int SelectedCommandCount => _sources.SelectMany(source => source.Commands).Count(command => command.IsSelected);

    public event Action? CloseRequested;

    public ImportExportViewModel(
        CommandOwnershipManager ownershipManager,
        Func<string, bool, Task<CommandExecutionResult>> importPack,
        Func<string, CommandPackPreview> previewPack,
        Action close,
        AddonPackageService addonPackageService,
        Action runtimeRefresh)
    {
        _ownershipManager = ownershipManager;
        _importPack = importPack;
        _previewPack = previewPack;
        _close = close;
        _addonPackageService = addonPackageService;
        _runtimeRefresh = runtimeRefresh;
        ImportCommand = new AsyncRelayCommand(ImportPackFromPathAsync, () => !IsWorking);
        ImportAddonCommand = new AsyncRelayCommand(ImportAddonAsync, () => !IsWorking && _addonImportPreview is not null);
        MoveCommand = new RelayCommand(MoveSelectedCommands);
        RefreshAddonComponentsCommand = new RelayCommand(RefreshAddonComponents);
        Destinations = new[] { new CommandDestinationViewModel(CommandOwnershipManager.UserOwnerId, "User commands", null) }
            .Concat(ownershipManager.EnabledPacks.Select(pack => new CommandDestinationViewModel(pack.Id, $"Pack: {pack.Id}", pack.Id)))
            .Append(new CommandDestinationViewModel("new-pack", "Create new pack…", null, true))
            .ToArray();
        _selectedDestination = Destinations[0];
        RefreshAddonComponents();
        RefreshInventory();
    }

    public void RefreshAddonComponents()
    {
        AddonPackGroups.Clear();
        foreach (var group in _addonPackageService.GetAvailablePacks()
            .GroupBy(pack => pack.Group ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var packs = group.Select(pack => new AddonComponentSelectionViewModel(pack.Id, pack.Id)).ToArray();
            AddonPackGroups.Add(new AddonPackGroupViewModel(
                string.IsNullOrEmpty(group.Key) ? "Ungrouped" : group.Key,
                packs));
        }

        AddonSnippets.Clear();
        foreach (var snippet in _addonPackageService.GetAvailableSnippets())
        {
            AddonSnippets.Add(new AddonComponentSelectionViewModel(snippet.Metadata.Name, snippet.Metadata.Name));
        }

        AddonExtensions.Clear();
        foreach (var extension in _addonPackageService.GetAvailableExtensions())
        {
            AddonExtensions.Add(new AddonComponentSelectionViewModel(extension.Id, extension.Id));
        }

        OnPropertyChanged(nameof(AddonPackGroups));
        OnPropertyChanged(nameof(AddonSnippets));
        OnPropertyChanged(nameof(AddonExtensions));
    }

    public void CreateAddon(string path)
    {
        try
        {
            _addonPackageService.CreatePackage(
                path,
                AddonId,
                AddonName,
                AddonVersion,
                AddonPackGroups.SelectMany(group => group.Packs).Where(item => item.IsSelected).Select(item => item.Id),
                AddonSnippets.Where(item => item.IsSelected).Select(item => item.Id),
                AddonExtensions.Where(item => item.IsSelected).Select(item => item.Id));
            CreateAddonStatus = $"Created add-on package '{Path.GetFileName(path)}'.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            CreateAddonStatus = $"Could not create add-on: {exception.Message}";
        }
    }

    private void RefreshAddonPreview()
    {
        _addonImportPreview = null;
        HasAddonPreview = false;
        AddonPreviewPacks.Clear();
        AddonPreviewSnippets.Clear();
        AddonPreviewExtensions.Clear();
        if (string.IsNullOrWhiteSpace(AddonPath))
        {
            AddonPreviewMessage = "Choose an add-on ZIP to preview its contents.";
        }
        else
        {
            try
            {
                _addonImportPreview = _addonPackageService.Preview(AddonPath.Trim());
                AddonPreviewName = _addonImportPreview.Manifest.Name;
                AddonPreviewId = _addonImportPreview.Manifest.Id;
                AddonPreviewVersion = _addonImportPreview.Manifest.Version;
                foreach (var item in _addonImportPreview.Items)
                {
                    var viewModel = new AddonPreviewItemViewModel(item, AddonConflictPolicyIndex == 1);
                    switch (item.Category)
                    {
                        case "Command pack":
                            AddonPreviewPacks.Add(viewModel);
                            break;
                        case "Snippet":
                            AddonPreviewSnippets.Add(viewModel);
                            break;
                        case "Extension":
                            AddonPreviewExtensions.Add(viewModel);
                            break;
                    }
                }

                HasAddonPreview = true;
                AddonPreviewMessage = string.Empty;
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or ArgumentException or JsonException)
            {
                AddonPreviewMessage = $"Could not read add-on: {exception.Message}";
            }
        }

        ImportAddonCommand.NotifyCanExecuteChanged();
    }

    private void RefreshPackPreview()
    {
        HasPackPreview = false;
        PackPreviewId = string.Empty;
        PackPreviewNamespace = string.Empty;
        PackPreviewVersion = string.Empty;
        PackPreviewCommands.Clear();

        if (string.IsNullOrWhiteSpace(ImportPath))
        {
            PackPreviewMessage = "Choose a command-pack JSON file to preview its contents.";
            return;
        }

        try
        {
            var preview = _previewPack(ImportPath.Trim());
            PackPreviewId = preview.Id;
            PackPreviewNamespace = string.IsNullOrWhiteSpace(preview.Namespace) ? "Global" : preview.Namespace;
            PackPreviewVersion = string.IsNullOrWhiteSpace(preview.Version) ? "Not specified" : preview.Version;

            foreach (var previewCommand in preview.Commands)
            {
                var commandItem = new AddonPreviewItemViewModel(new AddonPreviewItem(
                    "Command",
                    previewCommand.Command.QualifiedName,
                    previewCommand.ExistingOwner is not null,
                    previewCommand.ExistingOwner,
                    Version: null,
                    ImportCommandCount: previewCommand.ExistingOwner is null ? 1 : 0,
                    TotalCommandCount: 1));
                commandItem.SetImportPolicy(ImportConflictPolicyIndex == 1, replaceOwner: $"Pack: {preview.Id}");
                PackPreviewCommands.Add(commandItem);
            }

            HasPackPreview = true;
            PackPreviewMessage = string.Empty;
            RefreshPackCommandCounts();
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or ArgumentException or JsonException)
        {
            PackPreviewMessage = $"Could not read command pack: {exception.Message}";
        }
    }

    private void RefreshPackCommandCounts()
    {
        if (!HasPackPreview)
        {
            PackPreviewCommandCountText = string.Empty;
            return;
        }

        var overwriteSamePack = ImportConflictPolicyIndex == 1;
        foreach (var command in PackPreviewCommands)
        {
            command.SetImportPolicy(overwriteSamePack, replaceOwner: $"Pack: {PackPreviewId}");
        }

        var importCount = PackPreviewCommands.Sum(command => command.ImportCommandCount ?? 0);
        PackPreviewCommandCountText = $"{importCount} / {PackPreviewCommands.Count} commands to import";
    }

    private void RefreshAddonCommandCounts()
    {
        var overwriteExisting = AddonConflictPolicyIndex == 1;
        foreach (var item in AddonPreviewPacks.Concat(AddonPreviewExtensions))
        {
            item.SetImportPolicy(overwriteExisting);
        }
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
            var policy = AddonConflictPolicyIndex == 1
                ? ImportConflictPolicy.OverwriteExisting
                : ImportConflictPolicy.SkipExisting;
            var result = _addonPackageService.Install(AddonPath.Trim(), _addonImportPreview, policy, _runtimeRefresh);
            var skippedMessage = result.SkippedItems.Count == 0
                ? string.Empty
                : $" Skipped existing: {string.Join(", ", result.SkippedItems)}.";
            AddonStatus = $"Installed add-on '{_addonImportPreview.Manifest.Name}'.{skippedMessage}";
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
        source = new CommandSourceViewModel(
            ownerId,
            name,
            commands,
            isExpanded: false);
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

        try
        {
            var preview = SelectedDestination.IsNewPack
                ? _ownershipManager.PreviewMoveToNewPack(keys, NewPackId, NewPackNamespace)
                : _ownershipManager.PreviewMove(keys, SelectedDestination.PackId);
            CanMove = preview.CanCommit;
            MovePreview = preview.Conflicts.Count > 0
                ? $"Conflicts prevent this move:{Environment.NewLine}{string.Join(Environment.NewLine, preview.Conflicts)}"
                : preview.CommandsToMove.Count == 0
                    ? "The selected commands already belong to this destination."
                    : SelectedDestination.IsNewPack
                        ? $"Create pack '{NewPackId}' and move {preview.CommandsToMove.Count} command(s) to it?"
                        : $"Move {preview.CommandsToMove.Count} command(s) to {SelectedDestination.Name}?";
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException)
        {
            CanMove = false;
            MovePreview = exception.Message;
        }
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
            var result = await _importPack(ImportPath.Trim(), ImportConflictPolicyIndex == 1);
            ImportStatus = result.Message;
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
        var keys = GetSelectedCommandKeys();
        var result = SelectedDestination.IsNewPack
            ? _ownershipManager.MoveCommandsToNewPack(keys, NewPackId, NewPackNamespace)
            : _ownershipManager.MoveCommands(keys, SelectedDestination.PackId);
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
    private bool _isExpanded;

    public string OwnerId { get; }
    public string Name { get; }
    public IReadOnlyList<CommandSelectionViewModel> Commands { get; }
    public int CommandCount => Commands.Count;
    public int SelectedCommandCount => Commands.Count(command => command.IsSelected);
    public string SelectionCountText => $"{SelectedCommandCount}/{CommandCount}";
    public IRelayCommand ToggleAllCommand { get; }
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
    public string ToggleAllText => Commands.Any(command => !command.IsSelected) ? "Select all" : "Clear selection";

    public CommandSourceViewModel(
        string ownerId,
        string name,
        IReadOnlyList<CommandSelectionViewModel> commands,
        bool isExpanded = false)
    {
        OwnerId = ownerId;
        Name = name;
        Commands = commands;
        _isExpanded = isExpanded;
        ToggleAllCommand = new RelayCommand(ToggleAll);
    }

    public void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(ToggleAllText));
        OnPropertyChanged(nameof(SelectedCommandCount));
        OnPropertyChanged(nameof(SelectionCountText));
    }

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

public sealed class AddonComponentSelectionViewModel : ViewModelBase
{
    private bool _isSelected;

    public string Id { get; }
    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public AddonComponentSelectionViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }
}

public sealed class AddonPackGroupViewModel : ViewModelBase
{
    public string Name { get; }
    public IReadOnlyList<AddonComponentSelectionViewModel> Packs { get; }
    public string ToggleSelectionText => Packs.Any(pack => !pack.IsSelected) ? "Select all" : "Clear";
    public IRelayCommand ToggleSelectionCommand { get; }

    public AddonPackGroupViewModel(string name, IReadOnlyList<AddonComponentSelectionViewModel> packs)
    {
        Name = name;
        Packs = packs;
        ToggleSelectionCommand = new RelayCommand(ToggleSelection);
        foreach (var pack in Packs)
        {
            pack.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ToggleSelectionText));
        }
    }

    private void ToggleSelection()
    {
        var selectAll = Packs.Any(pack => !pack.IsSelected);
        foreach (var pack in Packs)
        {
            pack.IsSelected = selectAll;
        }

        OnPropertyChanged(nameof(ToggleSelectionText));
    }
}

public sealed class AddonPreviewItemViewModel : ViewModelBase
{
    private int? _importCommandCount;
    private bool _overwriteExisting;
    public string Category { get; }
    public string Name { get; }
    public bool AlreadyExists { get; }
    public string Status => AlreadyExists ? "Already exists" : "New";
    public string StatusDetail => AlreadyExists
        ? $"Already exists{(string.IsNullOrWhiteSpace(ExistingOwner) ? string.Empty : $" · {ExistingOwner}")}"
        : "New";
    public string? ExistingOwner { get; }
    public string Version { get; }
    public int? ImportCommandCount { get => _importCommandCount; private set => SetProperty(ref _importCommandCount, value); }
    public int? TotalCommandCount { get; }
    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "Version not specified" : $"Version {Version}";
    public string CommandCountText => ImportCommandCount is null || TotalCommandCount is null
        ? string.Empty
        : $"{ImportCommandCount} / {TotalCommandCount} commands to import";
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);
    public bool HasCommandCountText => !string.IsNullOrWhiteSpace(CommandCountText);
    public IReadOnlyList<AddonPreviewItemViewModel> Commands { get; }
    public bool HasCommands => Commands.Count > 0;

    public AddonPreviewItemViewModel(AddonPreviewItem item, bool overwriteExisting = false)
    {
        Category = item.Category;
        Name = item.Name;
        AlreadyExists = item.AlreadyExists;
        ExistingOwner = item.ExistingOwner;
        Version = item.Version ?? string.Empty;
        _importCommandCount = item.ImportCommandCount;
        TotalCommandCount = item.TotalCommandCount;
        Commands = (item.Commands ?? []).Select(command => new AddonPreviewItemViewModel(command, overwriteExisting)).ToArray();
        _overwriteExisting = overwriteExisting;
        SetImportPolicy(overwriteExisting);
    }

    public void SetImportPolicy(bool overwriteExisting, bool overwriteComponent = false, string? replaceOwner = null)
    {
        _overwriteExisting = overwriteExisting;
        if (TotalCommandCount is not null)
        {
            if (string.Equals(Category, "Command", StringComparison.OrdinalIgnoreCase))
            {
                var canOverwrite = overwriteComponent
                    || (!string.IsNullOrWhiteSpace(replaceOwner)
                        && string.Equals(ExistingOwner, replaceOwner, StringComparison.OrdinalIgnoreCase));
                ImportCommandCount = AlreadyExists
                    ? overwriteExisting && canOverwrite ? TotalCommandCount : 0
                    : TotalCommandCount;
            }
            else if (AlreadyExists && !overwriteExisting)
            {
                ImportCommandCount = 0;
            }
            else if (Commands.Count > 0)
            {
                ImportCommandCount = AlreadyExists && !overwriteExisting
                    ? 0
                    : Commands.Sum(command => command.ImportCommandCount ?? 0);
            }
            else
            {
                ImportCommandCount = TotalCommandCount;
            }

            OnPropertyChanged(nameof(CommandCountText));
            OnPropertyChanged(nameof(HasCommandCountText));
        }

        foreach (var command in Commands)
        {
            command.SetImportPolicy(
                overwriteExisting,
                replaceOwner: string.Equals(Category, "Extension", StringComparison.OrdinalIgnoreCase)
                    ? $"Extension: {Name}"
                    : $"Pack: {Name}");
        }

        if (TotalCommandCount is not null && Commands.Count > 0)
        {
            ImportCommandCount = AlreadyExists && !overwriteExisting
                ? 0
                : Commands.Sum(command => command.ImportCommandCount ?? 0);
            OnPropertyChanged(nameof(CommandCountText));
            OnPropertyChanged(nameof(HasCommandCountText));
        }
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

public sealed record CommandDestinationViewModel(string OwnerId, string Name, string? PackId, bool IsNewPack = false);
