using CommunityToolkit.Mvvm.ComponentModel;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _commandText = string.Empty;
}
