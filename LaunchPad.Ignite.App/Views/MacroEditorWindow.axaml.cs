using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.App.Views;

public partial class MacroEditorWindow : Window
{
    public MacroEditorWindow(string qualifiedName, MacroDefinition? macro)
    {
        InitializeComponent();
        EscapeToClose.Attach(this);
        var (name, macroNamespace) = ParseQualifiedName(qualifiedName);
        NameBox.Text = macro?.Metadata.Name ?? name;
        NamespaceBox.Text = macro?.Metadata.Namespace ?? macroNamespace ?? string.Empty;
        ArgumentsBox.Text = string.Join(Environment.NewLine,
            (macro?.Metadata.Arguments ?? []).Select(FormatArgument));
        OnErrorBox.SelectedIndex = macro?.Metadata.OnError == MacroErrorBehavior.Continue ? 1 : 0;
        ScriptBox.Text = macro?.Script ?? string.Empty;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!MacroArgumentDefinitionParser.TryParse(
            ArgumentsBox.Text ?? string.Empty,
            out var arguments,
            out var error))
        {
            ErrorText.Text = error;
            return;
        }

        var behavior = OnErrorBox.SelectedIndex == 1
            ? MacroErrorBehavior.Continue
            : MacroErrorBehavior.Break;
        Close(new MacroEditorResult(arguments, behavior, ScriptBox.Text ?? string.Empty));
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
