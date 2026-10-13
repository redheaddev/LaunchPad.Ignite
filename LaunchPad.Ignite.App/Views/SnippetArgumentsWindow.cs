using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LaunchPad.Ignite.App.Views;

public sealed class SnippetArgumentsWindow : Window
{
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.OrdinalIgnoreCase);

    public SnippetArgumentsWindow(string snippetName, IReadOnlyList<string> arguments)
    {
        Title = $"Run snippet: {snippetName}";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        foreach (var argument in arguments)
        {
            content.Children.Add(new TextBlock { Text = argument });
            var field = new TextBox { Watermark = argument };
            _fields.Add(argument, field);
            content.Children.Add(field);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        cancel.Click += (_, _) => Close(null);
        var run = new Button { Content = "Run", MinWidth = 80, IsDefault = true };
        run.Click += (_, _) => Close((IReadOnlyDictionary<string, string>)GetValues());
        buttons.Children.Add(cancel);
        buttons.Children.Add(run);
        content.Children.Add(buttons);
        Content = content;
    }

    private Dictionary<string, string> GetValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, field) in _fields)
        {
            values.Add(name, field.Text ?? string.Empty);
        }

        return values;
    }
}
