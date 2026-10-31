using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Snippets;

namespace LaunchPad.Ignite.App.Views;

public partial class SnippetEditorWindow : Window
{
    private TextBox NameBoxControl => this.FindControl<TextBox>(nameof(NameBox))
        ?? throw new InvalidOperationException("The NameBox control was not found.");

    private ComboBox TypeBoxControl => this.FindControl<ComboBox>(nameof(TypeBox))
        ?? throw new InvalidOperationException("The TypeBox control was not found.");

    private TextBox ArgumentsBoxControl => this.FindControl<TextBox>(nameof(ArgumentsBox))
        ?? throw new InvalidOperationException("The ArgumentsBox control was not found.");

    private StackPanel SecretsPanelControl => this.FindControl<StackPanel>(nameof(SecretsPanel))
        ?? throw new InvalidOperationException("The SecretsPanel control was not found.");

    private TextBox ScriptBoxControl => this.FindControl<TextBox>(nameof(ScriptBox))
        ?? throw new InvalidOperationException("The ScriptBox control was not found.");

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    public SnippetEditorWindow(
        string name,
        SnippetDefinition? snippet,
        IReadOnlyList<string> availableSecrets)
    {
        InitializeComponent();
        EscapeToClose.Attach(this);
        NameBoxControl.Text = name;
        TypeBoxControl.SelectedIndex = string.Equals(snippet?.Metadata.Type, "shell", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ArgumentsBoxControl.Text = string.Join(",", snippet?.Metadata.Arguments ?? []);
        ScriptBoxControl.Text = snippet?.Script ?? string.Empty;
        var allowedSecrets = snippet?.Metadata.AllowedSecrets ?? [];
        foreach (var secret in availableSecrets)
        {
            SecretsPanelControl.Children.Add(new CheckBox
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
        var selectedType = (TypeBoxControl.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "powershell";
        var arguments = (ArgumentsBoxControl.Text ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var secrets = SecretsPanelControl.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag?.ToString() ?? string.Empty)
            .ToArray();
        Close(new SnippetEditorResult(selectedType, arguments, secrets, ScriptBoxControl.Text ?? string.Empty));
    }
}
