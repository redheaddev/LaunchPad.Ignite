using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LaunchPad.Ignite.App.ViewModels;
using System.Linq;

namespace LaunchPad.Ignite.App.Views;

public partial class HelpWindow : Window
{
    public HelpWindow(HelpViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        EscapeToClose.Attach(this);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
        => Close();

    private void ExpandAll_Click(object? sender, RoutedEventArgs e)
        => SetSectionsExpanded(true);

    private void CollapseAll_Click(object? sender, RoutedEventArgs e)
        => SetSectionsExpanded(false);

    private void SetSectionsExpanded(bool isExpanded)
    {
        foreach (var expander in this.GetVisualDescendants().OfType<Expander>())
        {
            expander.IsExpanded = isExpanded;
        }
    }
}
