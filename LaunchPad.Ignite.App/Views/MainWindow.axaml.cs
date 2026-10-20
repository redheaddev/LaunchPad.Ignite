using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using System.Collections.Generic;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Schema;
using SharpHook;
using SharpHook.Data;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.Views;

public partial class MainWindow : Window
{
    private readonly SimpleGlobalHook _globalHook = new();
    private WindowsRegisteredHotkey? _windowsActivationHotkey;
    private readonly TextBox _commandBox;
    private readonly Border _commandInputBorder;
    private readonly ItemsControl _completionList;
    private readonly TextBlock _commandTextOverlay;
    private readonly TextBlock _usageTextBlock;
    private readonly DispatcherTimer _windowStateSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private ScrollViewer? _commandScrollViewer;
    private MainViewModel? _shortcutViewModel;
    private Task? _hotKeyTask;
    private bool _altPressed;
    private bool _controlPressed;
    private bool _shiftPressed;
    private bool _metaPressed;
    private bool _activationKeyPressed;
    private KeyCode _activationKey;
    private bool _activationPending;
    private bool _secretDialogIsOpen;
    private bool _commandBoxHasFocus;
    private bool _commandBoxFocusPending;
    private readonly bool _startMinimized;
    private int? _pathSelectionAnchor;
    private int? _pathSelectionCaret;
    private int _pathSelectionStart = -1;
    private int _pathSelectionEnd = -1;
    private int? _pendingCaretIndex;
    private LauncherWindowMode? _appliedWindowMode;
    private bool _windowStateInitialized;
    private IPointer? _resizePointer;
    private Point _resizeStartPoint;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private bool _resizeWidth;
    private bool _resizeHeight;
    private readonly Cursor _horizontalResizeCursor;
    private readonly Cursor _verticalResizeCursor;
    private readonly Cursor _diagonalResizeCursor;
    private readonly Cursor _pointerCursor;

    private const double ResizeGripSize = 24;
    private const double MainDefaultWidth = 760;
    private const double MainDefaultHeight = 360;
    private const double MiniDefaultWidth = 640;
    private const double MiniDefaultHeight = 92;

    public MainWindow(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        InitializeComponent();
        _horizontalResizeCursor = new Cursor(StandardCursorType.SizeWestEast);
        _verticalResizeCursor = new Cursor(StandardCursorType.SizeNorthSouth);
        _diagonalResizeCursor = new Cursor(StandardCursorType.BottomRightCorner);
        _pointerCursor = new Cursor(StandardCursorType.Arrow);
        Title = $"LaunchPad.Ignite {typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
        _commandBox = this.FindControl<TextBox>("CommandBox")
            ?? throw new InvalidOperationException("Command input control was not found.");
        _commandInputBorder = this.FindControl<Border>("CommandInputBorder")
            ?? throw new InvalidOperationException("Command input border was not found.");
        _completionList = this.FindControl<ItemsControl>("CompletionList")
            ?? throw new InvalidOperationException("Completion list control was not found.");
        _commandTextOverlay = this.FindControl<TextBlock>("CommandTextOverlay")
            ?? throw new InvalidOperationException("Command text overlay was not found.");
        _usageTextBlock = this.FindControl<TextBlock>("UsageTextBlock")
            ?? throw new InvalidOperationException("Usage text control was not found.");
        ColorThemeService.ThemeChanged += UpdateUsageText;
        ColorThemeService.ThemeChanged += UpdateCommandInputBorder;
        DataContextChanged += (_, _) =>
        {
            if (_shortcutViewModel is not null)
            {
                _shortcutViewModel.CommandEditorRequested -= OpenCommandEditor;
                _shortcutViewModel.NavigationRequested -= MainWindow_NavigationRequested;
            }

            _shortcutViewModel = DataContext as MainViewModel;
            if (_shortcutViewModel is not null)
            {
                _shortcutViewModel.CommandEditorRequested += OpenCommandEditor;
                _shortcutViewModel.NavigationRequested += MainWindow_NavigationRequested;
            }

            if (DataContext is INotifyPropertyChanged viewModel)
            {
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.IsMiniMode)
                        && DataContext is MainViewModel currentViewModel)
                    {
                        ApplyWindowMode(currentViewModel);
                    }

                    if (args.PropertyName is nameof(MainViewModel.UsageText) or nameof(MainViewModel.UsageCommandText))
                    {
                        UpdateUsageText();
                    }

                };
            }

            if (_shortcutViewModel is not null)
            {
                ApplyWindowMode(_shortcutViewModel);
            }

            UpdateUsageText();
        };
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LaunchPad.Ignite.App/Assets/rocket.ico")));
        _globalHook.KeyPressed += GlobalHook_KeyPressed;
        _globalHook.KeyReleased += GlobalHook_KeyReleased;
        Deactivated += MainWindow_Deactivated;
        Activated += MainWindow_Activated;
        AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, MainWindow_PointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerMovedEvent, MainWindow_PointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, MainWindow_PointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        PositionChanged += (_, _) =>
        {
            if (_windowStateInitialized)
            {
                ScheduleWindowStateSave();
            }
        };
        _windowStateSaveTimer.Tick += (_, _) =>
        {
            _windowStateSaveTimer.Stop();
            SaveCurrentWindowState();
        };
        Opened += MainWindow_Opened;
        Closing += (_, _) => FlushWindowState();
        Closed += MainWindow_Closed;
    }

    private void CommandBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        _commandBoxHasFocus = true;
        UpdateCommandInputBorder();
    }

    private void CommandBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        _commandBoxHasFocus = false;
        UpdateCommandInputBorder();
    }

    private void UpdateCommandInputBorder()
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        _commandInputBorder.BorderBrush = resources[
            _commandBoxHasFocus ? "ThemeCommandFocus" : "ThemeBorder"] as IBrush;
        _commandInputBorder.BorderThickness = _commandBoxHasFocus ? new Thickness(2) : new Thickness(1);
    }

    private void UpdateUsageText()
    {
        _usageTextBlock.Inlines.Clear();
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (string.IsNullOrEmpty(viewModel.UsageText))
        {
            _usageTextBlock.Inlines.Add(new Avalonia.Controls.Documents.Run("Usage: —"));
            return;
        }

        if (string.IsNullOrEmpty(viewModel.UsageCommandText))
        {
            _usageTextBlock.Inlines.Add(new Avalonia.Controls.Documents.Run(viewModel.UsageText));
            return;
        }

        var commandText = viewModel.UsageCommandText;
        var prefix = "Usage: ";
        _usageTextBlock.Inlines.Add(new Avalonia.Controls.Documents.Run(prefix));
        var position = 0;
        var firstArgument = commandText.IndexOf('<');
        var commandAndActionEnd = firstArgument < 0
            ? commandText.Length
            : firstArgument > 0 && commandText[firstArgument - 1] == '['
                ? firstArgument - 1
                : firstArgument;
        if (commandAndActionEnd > 0)
        {
            _usageTextBlock.Inlines.Add(new Avalonia.Controls.Documents.Run(commandText[..commandAndActionEnd])
            {
                Foreground = Application.Current?.Resources["ThemeRequiredArgument"] as IBrush
            });
            position = commandAndActionEnd;
        }

        while (position < commandText.Length)
        {
            var opening = commandText.IndexOf('<', position);
            if (opening < 0)
            {
                _usageTextBlock.Inlines.Add(new Avalonia.Controls.Documents.Run(commandText[position..]));
                break;
            }

            var optional = opening > 0 && commandText[opening - 1] == '[';
            var placeholderStart = optional ? opening - 1 : opening;
            if (placeholderStart > position)
            {
                _usageTextBlock.Inlines.Add(new Avalonia.Controls.Documents.Run(commandText[position..placeholderStart]));
            }

            var closing = commandText.IndexOf('>', opening + 1);
            if (closing < 0)
            {
                _usageTextBlock.Inlines.Add(new Avalonia.Controls.Documents.Run(commandText[opening..]));
                break;
            }

            var placeholderEnd = optional && closing + 1 < commandText.Length && commandText[closing + 1] == ']'
                ? closing + 2
                : closing + 1;
            var run = new Avalonia.Controls.Documents.Run(commandText[opening..(closing + 1)])
            {
                Foreground = optional
                    ? Application.Current?.Resources["ThemeOptionalArgument"] as IBrush
                    : Application.Current?.Resources["ThemeRequiredArgument"] as IBrush
            };
            if (optional)
            {
                run.Text = commandText[placeholderStart..placeholderEnd];
            }

            _usageTextBlock.Inlines.Add(run);
            position = placeholderEnd;
        }
    }

    private async void OpenCommandEditor(string? commandName)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _secretDialogIsOpen = true;
        try
        {
            var settingsViewModel = viewModel.CreateSettingsViewModel();
            settingsViewModel.OpenCommandEditor(commandName);
            await new SettingsWindow(settingsViewModel).ShowDialog(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
            RefreshWindowsActivationHotkey();
        }
    }

    internal async Task<IReadOnlyDictionary<string, string>?> ShowSnippetArgumentsAsync(SnippetArgumentsWindow dialog)
    {
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<IReadOnlyDictionary<string, string>?>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    internal void SetPendingCaretIndex(int caretIndex)
        => _pendingCaretIndex = DataContext is MainViewModel viewModel
            ? viewModel.GetCommandInputCaretIndex(caretIndex)
            : caretIndex;

    private void UpdateCommandTextOverlayOffset()
    {
        if (_commandScrollViewer is not null)
        {
            _commandTextOverlay.RenderTransform = new TranslateTransform(-_commandScrollViewer.Offset.X, 0);
        }
    }

    private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_resizePointer is not null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is not Visual source
            || source is TextBox
            || source is ScrollBar
            || source.GetVisualAncestors().Any(ancestor => ancestor is TextBox or Button or ScrollBar or ScrollViewer))
        {
            return;
        }

        var point = e.GetPosition(this);
        _resizeWidth = point.X >= Bounds.Width - ResizeGripSize;
        _resizeHeight = _appliedWindowMode == LauncherWindowMode.Main
            && point.Y >= Bounds.Height - ResizeGripSize;
        if (_resizeWidth || _resizeHeight)
        {
            BeginResize(e, _resizeWidth, _resizeHeight);
            return;
        }

        BeginMoveDrag(e);
    }

    private async void CompletionItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || sender is not Control control
            || control.DataContext is null
            || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var index = viewModel.CompletionItems.ToList()
            .FindIndex(item => ReferenceEquals(item, control.DataContext));
        if (index < 0)
        {
            return;
        }

        e.Handled = true;
        viewModel.SetCommandInputCaretIndex(_commandBox.CaretIndex);
        await viewModel.AcceptCompletionAtIndexAsync(index);
        RequestCommandBoxFocus();
        var acceptedText = viewModel.CommandInputText;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_commandBox.IsFocused)
            {
                return;
            }

            if (!string.Equals(_commandBox.Text, acceptedText, StringComparison.Ordinal))
            {
                _commandBox.Text = acceptedText;
            }

            var caretIndex = acceptedText.Length;
            _commandBox.CaretIndex = caretIndex;
            _commandBox.SelectionStart = caretIndex;
            _commandBox.SelectionEnd = caretIndex;
        }, DispatcherPriority.Background);
    }

    private void MainResizeGrip_PointerPressed(object? sender, PointerPressedEventArgs e)
        => BeginResize(e, resizeWidth: true, resizeHeight: true);

    private void MiniResizeGrip_PointerPressed(object? sender, PointerPressedEventArgs e)
        => BeginResize(e, resizeWidth: true, resizeHeight: false);

    private void BeginResize(PointerPressedEventArgs e, bool resizeWidth, bool resizeHeight)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _resizeWidth = resizeWidth;
        _resizeHeight = resizeHeight;
        _resizePointer = e.Pointer;
        _resizeStartPoint = e.GetPosition(this);
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void MainWindow_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizePointer is null || !ReferenceEquals(_resizePointer, e.Pointer))
        {
            UpdateResizeCursor(e);
            return;
        }

        var point = e.GetPosition(this);
        if (_resizeWidth)
        {
            Width = Math.Max(MinWidth, _resizeStartWidth + point.X - _resizeStartPoint.X);
        }

        if (_resizeHeight)
        {
            Height = Math.Max(MinHeight, _resizeStartHeight + point.Y - _resizeStartPoint.Y);
        }

        ScheduleWindowStateSave();
        e.Handled = true;
    }

    private void UpdateResizeCursor(PointerEventArgs e)
    {
        if (e.Source is Visual source
            && (source is ScrollBar || source.GetVisualAncestors().Any(ancestor => ancestor is ScrollBar)))
        {
            Cursor = _pointerCursor;
            return;
        }

        var point = e.GetPosition(this);
        var resizeWidth = point.X >= Bounds.Width - ResizeGripSize;
        var resizeHeight = _appliedWindowMode == LauncherWindowMode.Main
            && point.Y >= Bounds.Height - ResizeGripSize;
        Cursor = resizeWidth && resizeHeight
            ? _diagonalResizeCursor
            : resizeWidth
                ? _horizontalResizeCursor
                : resizeHeight
                    ? _verticalResizeCursor
                    : null;
    }

    private void MainWindow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizePointer is null || !ReferenceEquals(_resizePointer, e.Pointer))
        {
            return;
        }

        _resizePointer = null;
        _resizeWidth = false;
        _resizeHeight = false;
        e.Pointer.Capture(null);
        UpdateResizeCursor(e);
        e.Handled = true;
    }

    private void ApplyWindowMode(MainViewModel viewModel)
    {
        var mode = viewModel.CurrentWindowMode;
        if (_appliedWindowMode is { } previousMode && previousMode != mode)
        {
            _windowStateSaveTimer.Stop();
            SaveWindowState(viewModel, previousMode);
        }

        _appliedWindowMode = mode;
        MinWidth = mode == LauncherWindowMode.Mini ? 460 : 640;
        MinHeight = mode == LauncherWindowMode.Mini ? MiniDefaultHeight : 300;

        var defaultWidth = mode == LauncherWindowMode.Mini ? MiniDefaultWidth : MainDefaultWidth;
        var defaultHeight = mode == LauncherWindowMode.Mini ? MiniDefaultHeight : MainDefaultHeight;
        if (viewModel.TryGetWindowSize(mode, out var size))
        {
            Width = Math.Max(MinWidth, size.Width);
            Height = mode == LauncherWindowMode.Mini
                ? defaultHeight
                : Math.Max(MinHeight, size.Height);
        }
        else
        {
            Width = defaultWidth;
            Height = defaultHeight;
        }

        if (IsLoaded && viewModel.TryGetWindowPosition(mode, out var position))
        {
            Position = ClampToVisibleScreen(new PixelPoint(position.X, position.Y));
        }
    }

    private void RestoreWindowState(MainViewModel viewModel)
    {
        if (viewModel.TryGetWindowPosition(viewModel.CurrentWindowMode, out var position))
        {
            Position = ClampToVisibleScreen(new PixelPoint(position.X, position.Y));
        }

        _windowStateInitialized = true;
    }

    private PixelPoint ClampToVisibleScreen(PixelPoint position)
    {
        var screens = Screens.All;
        if (screens.Count == 0)
        {
            return position;
        }

        var screen = screens.FirstOrDefault(candidate =>
        {
            var area = candidate.WorkingArea;
            return position.X >= area.X && position.X < area.Right
                && position.Y >= area.Y && position.Y < area.Bottom;
        }) ?? Screens.Primary ?? screens[0];
        var workingArea = screen.WorkingArea;
        var windowWidth = (int)Math.Ceiling(Width * screen.Scaling);
        var windowHeight = (int)Math.Ceiling(Height * screen.Scaling);
        var maxX = Math.Max(workingArea.X, workingArea.Right - Math.Min(windowWidth, workingArea.Width));
        var maxY = Math.Max(workingArea.Y, workingArea.Bottom - Math.Min(windowHeight, workingArea.Height));
        return new PixelPoint(
            Math.Clamp(position.X, workingArea.X, maxX),
            Math.Clamp(position.Y, workingArea.Y, maxY));
    }

    private void ScheduleWindowStateSave()
    {
        if (_appliedWindowMode is null)
        {
            return;
        }

        _windowStateSaveTimer.Stop();
        _windowStateSaveTimer.Start();
    }

    private void SaveCurrentWindowState()
    {
        if (_windowStateInitialized
            && _appliedWindowMode is { } mode
            && DataContext is MainViewModel viewModel)
        {
            SaveWindowState(viewModel, mode);
        }
    }

    private void SaveWindowState(MainViewModel viewModel, LauncherWindowMode mode)
    {
        viewModel.SaveWindowPosition(mode, new LauncherWindowPosition(Position.X, Position.Y));
        var width = (int)Math.Round(Width);
        var height = mode == LauncherWindowMode.Mini
            ? (int)MiniDefaultHeight
            : (int)Math.Round(Height);
        viewModel.SaveWindowSize(mode, new LauncherWindowSize(width, height));
    }

    private void FlushWindowState()
    {
        _windowStateSaveTimer.Stop();
        SaveCurrentWindowState();
    }

    internal async Task<bool> ConfirmCommandPackReplacementAsync(string id)
    {
        var dialog = new Window
        {
            Title = "Replace command pack?",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"A command pack named '{id}' is already installed. Replace it?",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CreateDialogButton("Cancel", false),
                            CreateDialogButton("Replace", true)
                        }
                    }
                }
            }
        };

        EscapeToClose.Attach(dialog);
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    internal async Task<bool> ConfirmVariableOverwriteAsync(string name)
    {
        var dialog = new Window
        {
            Title = "Overwrite variable?",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"A variable named '{name}' already exists. Overwrite it?",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CreateDialogButton("Cancel", false),
                            CreateDialogButton("Overwrite", true)
                        }
                    }
                }
            }
        };

        EscapeToClose.Attach(dialog);
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    internal async Task<SnippetEditorResult?> ShowSnippetEditorAsync(SnippetEditorWindow dialog)
    {
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<SnippetEditorResult?>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    internal async Task<MacroEditorResult?> ShowMacroEditorAsync(MacroEditorWindow dialog)
    {
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<MacroEditorResult?>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    internal async Task<bool> ConfirmMacroRemovalAsync(string qualifiedName)
    {
        var dialog = new Window
        {
            Title = "Remove macro?",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Remove macro '{qualifiedName}'? This cannot be undone.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CreateDialogButton("Cancel", false),
                            CreateDialogButton("Remove", true)
                        }
                    }
                }
            }
        };

        EscapeToClose.Attach(dialog);
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    internal async Task<bool> ConfirmSnippetRemovalAsync(string name)
    {
        var dialog = new Window
        {
            Title = "Remove snippet?",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Remove snippet '{name}'? This cannot be undone.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CreateDialogButton("Cancel", false),
                            CreateDialogButton("Remove", true)
                        }
                    }
                }
            }
        };

        EscapeToClose.Attach(dialog);
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    internal async Task<bool> ConfirmProfileRestoreAsync(string fileName)
    {
        var dialog = new Window
        {
            Title = "Restore profile?",
            Width = 460,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Restore '{fileName}'? Current profile files will first be saved to a backup, then overwritten.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CreateDialogButton("Cancel", false),
                            CreateDialogButton("Back up and restore", true)
                        }
                    }
                }
            }
        };

        EscapeToClose.Attach(dialog);
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    private async Task<bool> ConfirmClearHistoryAsync()
    {
        var dialog = new Window
        {
            Title = "Clear command history?",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Delete all command history? This cannot be undone.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CreateDialogButton("Cancel", false),
                            CreateDialogButton("Delete all", true)
                        }
                    }
                }
            }
        };

        EscapeToClose.Attach(dialog);
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    internal async Task<string?> PickCommandPackFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import command pack",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Command pack manifest")
                {
                    Patterns = ["*.json"]
                }
            ]
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    internal async Task<bool> ConfirmAddonReplacementAsync(AddonImportPreview preview)
    {
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"The add-on '{preview.Manifest.Name}' contains content that already exists. Replace these items?",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = string.Join(Environment.NewLine, preview.Conflicts),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        var dialog = new Window
        {
            Title = "Replace add-on content?",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    content,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CreateDialogButton("Cancel", false),
                            CreateDialogButton("Replace", true)
                        }
                    }
                }
            }
        };

        EscapeToClose.Attach(dialog);
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    private async void MainWindow_NavigationRequested(LauncherNavigationTarget target)
        => await OpenNavigationAsync(target);

    private Task OpenNavigationAsync(LauncherNavigationTarget target)
        => target switch
        {
            LauncherNavigationTarget.Settings => OpenSettingsAsync(),
            LauncherNavigationTarget.ImportExport => OpenImportExportAsync(),
            LauncherNavigationTarget.Help => OpenHelpAsync(),
            _ => Task.CompletedTask
        };

    private async void ImportExportButton_Click(object? sender, RoutedEventArgs e)
        => await OpenImportExportAsync();

    private async Task OpenImportExportAsync()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _secretDialogIsOpen = true;
        try
        {
            var dialogViewModel = viewModel.CreateImportExportViewModel(() => { });
            await new ImportExportWindow(dialogViewModel).ShowDialog(this);
            _commandBox.Text = viewModel.CommandInputText;
            _commandBox.CaretIndex = viewModel.CommandInputText.Length;
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
        => await OpenSettingsAsync();

    private async Task OpenSettingsAsync()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _secretDialogIsOpen = true;
        try
        {
            await new SettingsWindow(viewModel.CreateSettingsViewModel()).ShowDialog(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
            RefreshWindowsActivationHotkey();
        }
    }

    private async void HelpButton_Click(object? sender, RoutedEventArgs e)
        => await OpenHelpAsync();

    private async void SecretLockButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.ToggleSecretAccessAsync();
        }
    }

    private async Task OpenHelpAsync()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _secretDialogIsOpen = true;
        try
        {
            await new HelpWindow(viewModel.CreateHelpViewModel()).ShowDialog(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    private static Button CreateDialogButton(string label, bool result)
    {
        var button = new Button { Content = label, MinWidth = 80 };
        button.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(button) is Window window)
            {
                window.Close(result);
            }
        };

        return button;
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            RestoreWindowState(viewModel);
        }

        if (_startMinimized)
        {
            Hide();
        }

        _shortcutViewModel = DataContext as MainViewModel;
        _commandScrollViewer = _commandBox.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (_commandScrollViewer is not null)
        {
            _commandScrollViewer.ScrollChanged += (_, _) => UpdateCommandTextOverlayOffset();
            UpdateCommandTextOverlayOffset();
        }

        RefreshWindowsActivationHotkey();

        if (!_startMinimized)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Activate();
                RequestCommandBoxFocus();
            }, DispatcherPriority.Input);
        }

        if (!OperatingSystem.IsWindows() || _hotKeyTask is not null)
        {
            return;
        }

        _hotKeyTask = _globalHook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true);
        _ = _hotKeyTask.ContinueWith(
            task => Trace.TraceError($"Global hotkey listener failed: {task.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void GlobalHook_KeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        switch (e.Data.KeyCode)
        {
            case KeyCode.VcLeftAlt:
            case KeyCode.VcRightAlt:
                _altPressed = true;
                return;
            case KeyCode.VcLeftControl:
            case KeyCode.VcRightControl:
                _controlPressed = true;
                return;
            case KeyCode.VcLeftShift:
            case KeyCode.VcRightShift:
                _shiftPressed = true;
                return;
            case KeyCode.VcLeftMeta:
            case KeyCode.VcRightMeta:
                _metaPressed = true;
                return;
        }

        if (_secretDialogIsOpen)
        {
            return;
        }

        if (!_activationKeyPressed
            && _shortcutViewModel is { } mainViewModel
            && GlobalActivationHotkey.TryParse(mainViewModel.GlobalActivationHotkeyValue, out var activationHotkey)
            && activationHotkey.Matches(_controlPressed, _altPressed, _shiftPressed, _metaPressed, e.Data.KeyCode))
        {
            if (_windowsActivationHotkey is not null)
            {
                return;
            }

            _activationKeyPressed = true;
            _activationKey = e.Data.KeyCode;
            _activationPending = true;
            e.SuppressEvent = true;
            return;
        }

        if (!_controlPressed && !_altPressed && !_shiftPressed && !_metaPressed)
        {
            return;
        }

        if (!TryGetShortcutKey(e.Data.KeyCode, out var key))
        {
            return;
        }

        var chord = new KeyboardShortcutChord(_controlPressed, _altPressed, _shiftPressed, _metaPressed, key);
        if (_shortcutViewModel is { } viewModel
            && viewModel.TryGetKeyboardShortcutExpansion(chord, out var expansion, out var executeImmediately))
        {
            e.SuppressEvent = true;
            if (executeImmediately)
            {
                // Submit the expanded command immediately
                Dispatcher.UIThread.Post(() => viewModel.ExecuteShortcutAsync(expansion));
            }
            else
            {
                // Expand the chord into the command input and allow the user to submit it
                Dispatcher.UIThread.Post(() =>
                {
                    viewModel.CommandText = expansion;
                    ShowAndActivate();
                    _commandBox.CaretIndex = viewModel.GetCommandInputCaretIndex(expansion.Length);
                });
            }
        }
    }

    private static bool TryGetShortcutKey(KeyCode keyCode, out string key)
    {
        var keyCodeName = keyCode.ToString();
        const string numpadPrefix = "VcNumPad";
        if (keyCodeName.StartsWith(numpadPrefix, StringComparison.Ordinal)
            && keyCodeName.Length == numpadPrefix.Length + 1
            && char.IsAsciiDigit(keyCodeName[^1]))
        {
            key = $"num{keyCodeName[^1]}";
            return true;
        }

        if (keyCodeName.Length == 3
            && keyCodeName.StartsWith("Vc", StringComparison.Ordinal)
            && char.IsAsciiLetterOrDigit(keyCodeName[2]))
        {
            key = keyCodeName[2].ToString().ToLowerInvariant();
            return true;
        }

        key = string.Empty;
        return false;
    }

    private void GlobalHook_KeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        switch (e.Data.KeyCode)
        {
            case KeyCode.VcLeftAlt:
            case KeyCode.VcRightAlt:
                _altPressed = false;
                break;
            case KeyCode.VcLeftControl:
            case KeyCode.VcRightControl:
                _controlPressed = false;
                break;
            case KeyCode.VcLeftShift:
            case KeyCode.VcRightShift:
                _shiftPressed = false;
                break;
            case KeyCode.VcLeftMeta:
            case KeyCode.VcRightMeta:
                _metaPressed = false;
                break;
        }

        if (e.Data.KeyCode == _activationKey)
        {
            e.SuppressEvent = _activationKeyPressed;
            _activationKeyPressed = false;
        }

        if (_activationPending && !_altPressed && !_controlPressed && !_shiftPressed && !_metaPressed)
        {
            _activationPending = false;
            Dispatcher.UIThread.Post(ActivateFromHotkey);
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_secretDialogIsOpen || DataContext is MainViewModel { KeepWindowOpen: true })
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            HideMainWindow();
        }
        else if (OperatingSystem.IsMacOS())
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        RequestCommandBoxFocus();
    }

    private void RefreshWindowsActivationHotkey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _windowsActivationHotkey?.Dispose();
        _windowsActivationHotkey = null;
        if (_shortcutViewModel is not { } viewModel)
        {
            return;
        }

        _windowsActivationHotkey = WindowsRegisteredHotkey.TryCreate(
            viewModel.GlobalActivationHotkeyValue,
            () => Dispatcher.UIThread.Post(() =>
            {
                if (!_secretDialogIsOpen)
                {
                    ActivateFromHotkey();
                }
            }),
            out var error);

        if (_windowsActivationHotkey is null && error is not null)
        {
            Trace.TraceWarning($"{error} Using the keyboard-hook activation fallback.");
        }
    }

    private void RequestCommandBoxFocus()
    {
        if (_commandBoxFocusPending || _secretDialogIsOpen)
        {
            return;
        }

        _commandBoxFocusPending = true;
        _ = FocusCommandBoxWhenActiveAsync();
    }

    private async Task FocusCommandBoxWhenActiveAsync()
    {
        try
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (!IsVisible || _secretDialogIsOpen)
                {
                    return;
                }

                if (IsActive)
                {
                    _commandBox.Focus();
                    if (_commandBox.IsFocused)
                    {
                        return;
                    }
                }

                await Task.Delay(50);
            }
        }
        finally
        {
            _commandBoxFocusPending = false;
        }
    }

    internal async Task<string?> ShowSecretEntryDialogAsync(SecretEntryWindow dialog)
    {
        _secretDialogIsOpen = true;
        try
        {
            return await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            _secretDialogIsOpen = false;
        }
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (LauncherNavigationShortcutParser.TryParse(e.Key, e.KeyModifiers, out var target))
        {
            viewModel.RequestNavigation(target);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.M && e.KeyModifiers == KeyModifiers.Control)
        {
            viewModel.ToggleWindowMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            await viewModel.HandleInputKeyAsync(CommandInputKey.Escape);
            if (!viewModel.KeepWindowOpen)
            {
                HideMainWindow();
            }
            e.Handled = true;
            return;
        }

        if (!_commandBox.IsFocused)
        {
            ResetPathSelection();
            return;
        }

        viewModel.SetCommandInputCaretIndex(_commandBox.CaretIndex);

        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
        {
            if (viewModel.UndoCommandInput(out var caretIndex))
            {
                var restoredText = viewModel.CommandInputText;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_commandBox.IsFocused)
                    {
                        return;
                    }

                    if (!string.Equals(_commandBox.Text, restoredText, StringComparison.Ordinal))
                    {
                        _commandBox.Text = restoredText;
                    }

                    var restoredCaret = Math.Clamp(caretIndex, 0, restoredText.Length);
                    _commandBox.CaretIndex = restoredCaret;
                    _commandBox.SelectionStart = restoredCaret;
                    _commandBox.SelectionEnd = restoredCaret;
                }, DispatcherPriority.Background);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Shift)
        {
            viewModel.EndCommandInputSession();
        }

        if (e.Key == Key.Enter
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            await viewModel.HandleInputKeyAsync(CommandInputKey.Enter, launchMode: CommandLaunchMode.Elevated);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter
            && e.KeyModifiers == KeyModifiers.Shift
            && !viewModel.IsCommandMetadataFilterMode
            && DirectFilePathSelection.TryGetPath(
                _commandBox.Text ?? string.Empty,
                _commandBox.SelectionStart,
                _commandBox.SelectionEnd,
                _commandBox.CaretIndex,
                out var selectedPath)
            && viewModel.OpenDirectPathInTerminal(selectedPath))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Alt)
        {
            await viewModel.HandleInputKeyAsync(CommandInputKey.Enter, launchMode: CommandLaunchMode.DifferentUser);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await viewModel.HandleInputKeyAsync(CommandInputKey.Enter, newWindow: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.End
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            ResetPathSelection();
            var end = _commandBox.Text?.Length ?? 0;
            _commandBox.SelectionStart = end;
            _commandBox.SelectionEnd = end;
            _commandBox.CaretIndex = end;
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && !viewModel.IsCommandMetadataFilterMode
            && e.Key is Key.Left or Key.Right
            && TrySelectPathLevel(e.Key == Key.Right))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right
            && e.KeyModifiers == KeyModifiers.None
            && !viewModel.IsCommandMetadataFilterMode
            && PathLevelSelection.IsPathSelection(
                _commandBox.Text ?? string.Empty,
                _commandBox.SelectionStart,
                _commandBox.SelectionEnd)
            && TrySelectPathLevel(
                moveRight: true,
                adjustUntrackedSelection: _commandBox.SelectionStart != _commandBox.SelectionEnd))
        {
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key is Key.Left or Key.Right)
        {
            ResetPathSelection();
        }

        CommandInputKey? inputKey = null;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (e.KeyModifiers == KeyModifiers.Alt
                && e.Key is Key.Up or Key.Down or Key.Home or Key.End)
            {
                if (e.Key is Key.Up or Key.Down)
                {
                    viewModel.CycleHistory(older: e.Key == Key.Up);
                }
                else
                {
                    viewModel.SelectHistoryBoundary(newest: e.Key == Key.Home);
                }

                e.Handled = true;
                return;
            }

        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.Delete)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    if (await ConfirmClearHistoryAsync())
                    {
                        viewModel.ClearHistory();
                    }
                }
                else
                {
                    viewModel.DeleteSelectedHistoryEntry();
                }

                e.Handled = true;
                return;
            }

            inputKey = e.Key switch
            {
                Key.A when viewModel.IsChecklistActive => CommandInputKey.SelectAll,
                Key.D when viewModel.IsChecklistActive => CommandInputKey.DeselectAll,
                Key.Back => CommandInputKey.Clear,
                _ => null
            };
        }
        else
        {
            inputKey = e.Key switch
            {
                Key.Up when e.KeyModifiers == KeyModifiers.Shift
                    && (viewModel.HasCompletionOptions || viewModel.IsChecklistActive) => CommandInputKey.First,
                Key.Down when e.KeyModifiers == KeyModifiers.Shift
                    && (viewModel.HasCompletionOptions || viewModel.IsChecklistActive) => CommandInputKey.Last,
                Key.Tab when viewModel.CanComplete || viewModel.CanExpandTypedShortcut => CommandInputKey.Tab,
                Key.Right when (viewModel.CanComplete || viewModel.IsChecklistActive)
                    && _commandBox.SelectionStart == _commandBox.SelectionEnd
                    && _commandBox.CaretIndex == (_commandBox.Text?.Length ?? 0) => CommandInputKey.Right,
                Key.Enter => CommandInputKey.Enter,
                Key.Space when viewModel.IsChecklistActive || viewModel.IsCommandMetadataFilterInputActive => CommandInputKey.Space,
                Key.Up when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.Up,
                Key.Down when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.Down,
                Key.PageUp when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.PageUp,
                Key.PageDown when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.PageDown,
                _ => null
            };
        }

        if (inputKey is not null)
        {
            var previousText = viewModel.CommandInputText;
            await viewModel.HandleInputKeyAsync(inputKey.Value);
            var pendingCaretIndex = _pendingCaretIndex;
            _pendingCaretIndex = null;
            e.Handled = true;
            Dispatcher.UIThread.Post(() =>
            {
                for (var index = 0; index < viewModel.CompletionItems.Count; index++)
                {
                    if (viewModel.CompletionItems[index].IsHighlighted)
                    {
                        _completionList.ContainerFromIndex(index)?.BringIntoView();
                        break;
                    }
                }
            }, DispatcherPriority.Background);

            if (!string.Equals(previousText, viewModel.CommandInputText, StringComparison.Ordinal))
            {
                var updatedText = viewModel.CommandInputText;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_commandBox.IsFocused)
                    {
                        if (!string.Equals(_commandBox.Text, updatedText, StringComparison.Ordinal))
                        {
                            _commandBox.Text = updatedText;
                        }

                        var caretIndex = pendingCaretIndex is { } requestedCaretIndex
                            ? Math.Clamp(requestedCaretIndex, 0, updatedText.Length)
                            : updatedText.Length;
                        _commandBox.CaretIndex = caretIndex;
                        _commandBox.SelectionStart = caretIndex;
                        _commandBox.SelectionEnd = caretIndex;
                    }
                }, DispatcherPriority.Background);
            }
        }
    }

    private bool TrySelectPathLevel(bool moveRight, bool adjustUntrackedSelection = false)
    {
        var text = _commandBox.Text ?? string.Empty;
        var selectionStart = Math.Min(_commandBox.SelectionStart, _commandBox.SelectionEnd);
        var selectionEnd = Math.Max(_commandBox.SelectionStart, _commandBox.SelectionEnd);
        var hasTrackedSelection = _pathSelectionAnchor.HasValue
            && _pathSelectionCaret.HasValue
            && selectionStart == _pathSelectionStart
            && selectionEnd == _pathSelectionEnd;
        if (!hasTrackedSelection && selectionStart < selectionEnd)
        {
            if (adjustUntrackedSelection
                && PathLevelSelection.TryAdjustSelection(
                    text,
                    _commandBox.CaretIndex,
                    selectionStart,
                    selectionEnd,
                    moveRight,
                    out var adjustedSelectionStart,
                    out var adjustedSelectionEnd,
                    out var adjustedSelectionCaret))
            {
                var selectionAnchor = moveRight ? selectionEnd : selectionStart;
                SetPathSelection(
                    selectionAnchor,
                    adjustedSelectionCaret,
                    adjustedSelectionStart,
                    adjustedSelectionEnd,
                    text);
                return true;
            }

            var edge = moveRight ? selectionEnd : selectionStart;
            SetPathSelection(edge, edge, edge, edge, text);
            return true;
        }

        var anchor = hasTrackedSelection
            ? _pathSelectionAnchor!.Value
            : selectionStart == selectionEnd
                ? _commandBox.CaretIndex
                : moveRight ? selectionEnd : selectionStart;
        var caret = hasTrackedSelection
            ? _pathSelectionCaret!.Value
            : selectionStart == selectionEnd
                ? _commandBox.CaretIndex
                : moveRight ? selectionStart : selectionEnd;

        if (!PathLevelSelection.TryGetBoundary(text, caret, moveRight, out var nextCaret))
        {
            if (hasTrackedSelection)
            {
                SetPathSelection(caret, caret, caret, caret, text);
                return true;
            }

            _pathSelectionAnchor = null;
            _pathSelectionCaret = null;
            _pathSelectionStart = -1;
            _pathSelectionEnd = -1;
            return false;
        }

        var adjustedStart = Math.Min(anchor, nextCaret);
        var adjustedEnd = Math.Max(anchor, nextCaret);
        SetPathSelection(anchor, nextCaret, adjustedStart, adjustedEnd, text);
        return true;
    }

    private void SetPathSelection(int anchor, int caret, int start, int end, string text)
    {
        _pathSelectionAnchor = anchor;
        _pathSelectionCaret = caret;
        _pathSelectionStart = start;
        _pathSelectionEnd = end;
        _commandBox.SelectionStart = start;
        _commandBox.SelectionEnd = end;
        Dispatcher.UIThread.Post(() =>
        {
            if (_commandBox.IsFocused && string.Equals(_commandBox.Text, text, StringComparison.Ordinal))
            {
                _commandBox.SelectionStart = start;
                _commandBox.SelectionEnd = end;
            }
        }, DispatcherPriority.Input);
    }

    private void ResetPathSelection()
    {
        _pathSelectionAnchor = null;
        _pathSelectionCaret = null;
        _pathSelectionStart = -1;
        _pathSelectionEnd = -1;
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            HideMainWindow();
            return;
        }

        ShowAndActivate();
    }

    private void ActivateFromHotkey()
    {
        if (DataContext is MainViewModel { KeepWindowOpen: true })
        {
            ShowAndActivate();
            return;
        }

        ToggleVisibility();
    }

    private void HideMainWindow()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ResetHistoryNavigation();
        }

        Hide();
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        RequestCommandBoxFocus();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ResetHistoryNavigation();
        }

        _globalHook.KeyPressed -= GlobalHook_KeyPressed;
        _globalHook.KeyReleased -= GlobalHook_KeyReleased;
        _windowsActivationHotkey?.Dispose();
        _windowsActivationHotkey = null;
        ColorThemeService.ThemeChanged -= UpdateUsageText;
        ColorThemeService.ThemeChanged -= UpdateCommandInputBorder;
        try
        {
            _globalHook.Dispose();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Unable to stop global hotkey listener: {ex.Message}");
        }
    }
}