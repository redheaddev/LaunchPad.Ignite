using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
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
    private readonly TextBox _commandBox;
    private readonly ItemsControl _completionList;
    private readonly TextBlock _commandTextOverlay;
    private readonly TextBlock _usageTextBlock;
    private ScrollViewer? _commandScrollViewer;
    private MainViewModel? _shortcutViewModel;
    private Task? _hotKeyTask;
    private bool _altPressed;
    private bool _controlPressed;
    private bool _shiftPressed;
    private bool _metaPressed;
    private bool _activationKeyPressed;
    private bool _secretDialogIsOpen;
    private int? _pathSelectionAnchor;
    private int? _pathSelectionCaret;
    private int _pathSelectionStart = -1;
    private int _pathSelectionEnd = -1;
    private int? _pendingCaretIndex;

    public MainWindow()
    {
        InitializeComponent();
        _commandBox = this.FindControl<TextBox>("CommandBox")
            ?? throw new InvalidOperationException("Command input control was not found.");;
        _completionList = this.FindControl<ItemsControl>("CompletionList")
            ?? throw new InvalidOperationException("Completion list control was not found.");
        _commandTextOverlay = this.FindControl<TextBlock>("CommandTextOverlay")
            ?? throw new InvalidOperationException("Command text overlay was not found.");
        _usageTextBlock = this.FindControl<TextBlock>("UsageTextBlock")
            ?? throw new InvalidOperationException("Usage text control was not found.");
        DataContextChanged += (_, _) =>
        {
            if (DataContext is INotifyPropertyChanged viewModel)
            {
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName is nameof(MainViewModel.UsageText) or nameof(MainViewModel.UsageCommandText))
                    {
                        UpdateUsageText();
                    }
                };
            }

            UpdateUsageText();
        };
        _globalHook.KeyPressed += GlobalHook_KeyPressed;
        _globalHook.KeyReleased += GlobalHook_KeyReleased;
        Deactivated += MainWindow_Deactivated;
        Activated += MainWindow_Activated;
        AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel);
        Opened += MainWindow_Opened;
        Closed += MainWindow_Closed;
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
                Foreground = new SolidColorBrush(Color.Parse("#FF86EFAC"))
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
                    ? new SolidColorBrush(Color.Parse("#FF94A3B8"))
                    : new SolidColorBrush(Color.Parse("#FF86EFAC"))
            };
            if (optional)
            {
                run.Text = commandText[placeholderStart..placeholderEnd];
            }

            _usageTextBlock.Inlines.Add(run);
            position = placeholderEnd;
        }
    }

    internal void SetPendingCaretIndex(int caretIndex) => _pendingCaretIndex = caretIndex;

    private void UpdateCommandTextOverlayOffset()
    {
        if (_commandScrollViewer is not null)
        {
            _commandTextOverlay.RenderTransform = new TranslateTransform(-_commandScrollViewer.Offset.X, 0);
        }
    }

    private void HeaderBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
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

    private async void ImportExportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = await PickCommandPackFileAsync();
        if (path is null)
        {
            return;
        }

        await viewModel.ImportCommandPackAsync(path);
        _commandBox.Text = viewModel.CommandText;
        _commandBox.CaretIndex = viewModel.CommandText.Length;
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
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
        _shortcutViewModel = DataContext as MainViewModel;
        _commandScrollViewer = _commandBox.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (_commandScrollViewer is not null)
        {
            _commandScrollViewer.ScrollChanged += (_, _) => UpdateCommandTextOverlayOffset();
            UpdateCommandTextOverlayOffset();
        }

        Dispatcher.UIThread.Post(() =>
        {
            Activate();
            _commandBox.Focus();
        }, DispatcherPriority.Input);

        if (_hotKeyTask is not null)
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

        if (e.Data.KeyCode == KeyCode.VcSpace
            && _altPressed
            && !_controlPressed
            && !_shiftPressed
            && !_metaPressed
            && !_activationKeyPressed)
        {
            _activationKeyPressed = true;
            e.SuppressEvent = true;
            Dispatcher.UIThread.Post(ToggleVisibility);
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
            && viewModel.TryGetKeyboardShortcutExpansion(chord, out var expansion))
        {
            e.SuppressEvent = true;
            Dispatcher.UIThread.Post(() =>
            {
                viewModel.CommandText = expansion;
                ShowAndActivate();
                _commandBox.CaretIndex = expansion.Length;
            });
        }
    }

    private static bool TryGetShortcutKey(KeyCode keyCode, out string key)
    {
        var keyCodeName = keyCode.ToString();
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

        if (e.Data.KeyCode == KeyCode.VcSpace)
        {
            e.SuppressEvent = _activationKeyPressed;
            _activationKeyPressed = false;
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!_secretDialogIsOpen)
        {
            Hide();
        }
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => _commandBox.Focus(), DispatcherPriority.Input);
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

        if (e.Key == Key.Escape)
        {
            await viewModel.HandleInputKeyAsync(CommandInputKey.Escape);
            Hide();
            e.Handled = true;
            return;
        }

        if (!_commandBox.IsFocused)
        {
            ResetPathSelection();
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
            && e.Key is Key.Left or Key.Right
            && TrySelectPathLevel(e.Key == Key.Right))
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
            inputKey = e.Key switch
            {
                Key.Home when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.First,
                Key.End when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.Last,
                _ => null
            };
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
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
                Key.Tab when viewModel.CanComplete || viewModel.CanExpandTypedShortcut => CommandInputKey.Tab,
                Key.Enter => CommandInputKey.Enter,
                Key.Space when viewModel.IsChecklistActive => CommandInputKey.Space,
                Key.Up when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.Up,
                Key.Down when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.Down,
                Key.PageUp when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.PageUp,
                Key.PageDown when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.PageDown,
                _ => null
            };
        }

        if (inputKey is not null)
        {
            var previousText = viewModel.CommandText;
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

            if (!string.Equals(previousText, viewModel.CommandText, StringComparison.Ordinal))
            {
                var updatedText = viewModel.CommandText;
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

    private bool TrySelectPathLevel(bool moveRight)
    {
        var text = _commandBox.Text ?? string.Empty;
        var selectionStart = _commandBox.SelectionStart;
        var selectionEnd = _commandBox.SelectionEnd;
        var hasTrackedSelection = _pathSelectionAnchor.HasValue
            && _pathSelectionCaret.HasValue
            && selectionStart == _pathSelectionStart
            && selectionEnd == _pathSelectionEnd;
        if (!hasTrackedSelection && selectionStart < selectionEnd)
        {
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
            Hide();
            return;
        }

        ShowAndActivate();
    }

    public void ShowAndActivate()
    {
        Show();
        Activate();
        _commandBox.Focus();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _globalHook.KeyPressed -= GlobalHook_KeyPressed;
        _globalHook.KeyReleased -= GlobalHook_KeyReleased;
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