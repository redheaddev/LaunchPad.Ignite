using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.Core.Completion;
using SharpHook;
using SharpHook.Data;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.Views;

public partial class MainWindow : Window
{
    private readonly SimpleGlobalHook _globalHook = new();
    private readonly TextBox _commandBox;
    private Task? _hotKeyTask;
    private bool _altPressed;
    private bool _controlPressed;
    private bool _shiftPressed;
    private bool _metaPressed;
    private bool _activationKeyPressed;

    public MainWindow()
    {
        InitializeComponent();
        _commandBox = this.FindControl<TextBox>("CommandBox")
            ?? throw new InvalidOperationException("Command input control was not found.");
        _globalHook.KeyPressed += GlobalHook_KeyPressed;
        _globalHook.KeyReleased += GlobalHook_KeyReleased;
        Deactivated += MainWindow_Deactivated;
        AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel);
        Opened += MainWindow_Opened;
        Closed += MainWindow_Closed;
    }

    private void HeaderBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
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
        }
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
        Hide();
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.HandleInputKey(CommandInputKey.Escape);
            Hide();
            e.Handled = true;
            return;
        }

        if (!_commandBox.IsFocused)
        {
            return;
        }

        CommandInputKey? inputKey = null;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
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
                Key.Tab when viewModel.CanComplete => CommandInputKey.Tab,
                Key.Enter => CommandInputKey.Enter,
                Key.Space when viewModel.IsChecklistActive => CommandInputKey.Space,
                Key.Up when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.Up,
                Key.Down when viewModel.HasCompletionOptions || viewModel.IsChecklistActive => CommandInputKey.Down,
                _ => null
            };
        }

        if (inputKey is not null)
        {
            var previousText = viewModel.CommandText;
            viewModel.HandleInputKey(inputKey.Value);
            e.Handled = true;

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

                        _commandBox.CaretIndex = updatedText.Length;
                    }
                }, DispatcherPriority.Background);
            }
        }
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