using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Macros;

namespace LaunchPad.Ignite.App.Views;

public partial class MacroEditorWindow : Window
{
    private TextBox NameBoxControl => this.FindControl<TextBox>(nameof(NameBox))
        ?? throw new InvalidOperationException("The NameBox control was not found.");

    private TextBox NamespaceBoxControl => this.FindControl<TextBox>(nameof(NamespaceBox))
        ?? throw new InvalidOperationException("The NamespaceBox control was not found.");

    private TextBox ArgumentsBoxControl => this.FindControl<TextBox>(nameof(ArgumentsBox))
        ?? throw new InvalidOperationException("The ArgumentsBox control was not found.");

    private ComboBox OnErrorBoxControl => this.FindControl<ComboBox>(nameof(OnErrorBox))
        ?? throw new InvalidOperationException("The OnErrorBox control was not found.");

    private TextBox ScriptBoxControl => this.FindControl<TextBox>(nameof(ScriptBox))
        ?? throw new InvalidOperationException("The ScriptBox control was not found.");

    private TextBlock ErrorTextControl => this.FindControl<TextBlock>(nameof(ErrorText))
        ?? throw new InvalidOperationException("The ErrorText control was not found.");

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    public MacroEditorWindow(string qualifiedName, MacroDefinition? macro)
    {
        InitializeComponent();
        EscapeToClose.Attach(this);
        var (name, macroNamespace) = ParseQualifiedName(qualifiedName);
        NameBoxControl.Text = macro?.Metadata.Name ?? name;
        NamespaceBoxControl.Text = macro?.Metadata.Namespace ?? macroNamespace ?? string.Empty;
        ArgumentsBoxControl.Text = string.Join(Environment.NewLine,
            (macro?.Metadata.Arguments ?? []).Select(FormatArgument));
        OnErrorBoxControl.SelectedIndex = macro?.Metadata.OnError == MacroErrorBehavior.Continue ? 1 : 0;
        ScriptBoxControl.Text = macro?.Script ?? string.Empty;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!MacroArgumentDefinitionParser.TryParse(
            ArgumentsBoxControl.Text ?? string.Empty,
            out var arguments,
            out var error))
        {
            ErrorTextControl.Text = error;
            return;
        }

        var behavior = OnErrorBoxControl.SelectedIndex == 1
            ? MacroErrorBehavior.Continue
            : MacroErrorBehavior.Break;
        Close(new MacroEditorResult(arguments, behavior, ScriptBoxControl.Text ?? string.Empty));
    }

    private static string FormatArgument(MacroArgumentDefinition argument)
    {
        var type = argument.Type switch
        {
            MacroArgumentType.SecretList => "secretList",
            MacroArgumentType.ValueList => "valueList",
            _ => argument.Type.ToString().ToLowerInvariant()
        };
        var source = argument.Type == MacroArgumentType.List ? $" {argument.ListName}" : string.Empty;
        var mode = argument.Type is MacroArgumentType.List or MacroArgumentType.SecretList or MacroArgumentType.ValueList
            ? $" {argument.ListMode.ToString().ToLowerInvariant()}"
            : string.Empty;
        return $"{argument.Name}:{type}{source}{mode}";
    }

    private static (string Name, string? Namespace) ParseQualifiedName(string qualifiedName)
    {
        var separator = qualifiedName.IndexOf(':');
        return separator < 0
            ? (qualifiedName, null)
            : (qualifiedName[(separator + 1)..], qualifiedName[..separator]);
    }
}
