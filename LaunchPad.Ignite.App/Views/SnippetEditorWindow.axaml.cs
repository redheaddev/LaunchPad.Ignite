using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.App.Views;

public partial class SnippetEditorWindow : Window
{
    public SnippetEditorWindow(
        string name,
        SnippetDefinition? snippet,
        IReadOnlyList<string> availableSecrets)
    {
        InitializeComponent();
        EscapeToClose.Attach(this);
        NameBox.Text = name;
        TypeBox.SelectedIndex = string.Equals(snippet?.Metadata.Type, "shell", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ArgumentsBox.Text = string.Join(",", snippet?.Metadata.Arguments ?? []);
        ScriptBox.Text = snippet?.Script ?? string.Empty;
        var allowedSecrets = snippet?.Metadata.AllowedSecrets ?? [];
        foreach (var secret in availableSecrets)
        {
            SecretsPanel.Children.Add(new CheckBox
            {
                Content = new TextBlock { Text = secret },
                Tag = secret,
                IsChecked = allowedSecrets.Contains(secret, StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var selectedType = (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "powershell";
        var arguments = (ArgumentsBox.Text ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var secrets = SecretsPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag?.ToString() ?? string.Empty)
            .ToArray();
        Close(new SnippetEditorResult(selectedType, arguments, secrets, ScriptBox.Text ?? string.Empty));
    }
}
