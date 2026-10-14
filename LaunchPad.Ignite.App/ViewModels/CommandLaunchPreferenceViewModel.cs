using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.App.ViewModels;

public sealed class CommandLaunchPreferenceViewModel : ObservableObject
{
    private readonly string _settingName;
    private readonly Action<string, string> _save;
    private string _selectedLaunchMode;

    public string CommandName { get; }
    public IReadOnlyList<string> LaunchModes { get; } = [
        nameof(CommandLaunchMode.Normal),
        nameof(CommandLaunchMode.Elevated),
        nameof(CommandLaunchMode.DifferentUser)
    ];

    public string SelectedLaunchMode
    {
        get => _selectedLaunchMode;
        set
        {
            if (SetProperty(ref _selectedLaunchMode, value))
            {
                _save(_settingName, value);
            }
        }
    }

    public CommandLaunchPreferenceViewModel(
        string commandName,
        string settingName,
        string selectedLaunchMode,
        Action<string, string> save)
    {
        CommandName = commandName;
        _settingName = settingName;
        _selectedLaunchMode = selectedLaunchMode;
        _save = save;
    }
}