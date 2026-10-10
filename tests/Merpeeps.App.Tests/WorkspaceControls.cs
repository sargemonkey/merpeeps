using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Merpeeps.App.Tests;

internal static class WorkspaceControls
{
    public static void OpenCreationForm(MainWindow window)
    {
        window.FindControl<MenuItem>("MenuClassAdd")!.RaiseClick();
        Dispatcher.UIThread.RunJobs();
    }
    public static T Find<T>(MainWindow window, string name) where T : Control
    {
        var properties = window.FindControl<Border>("ClassInspectorHost")!;
        var selected = properties.GetVisualDescendants().OfType<T>()
            .SingleOrDefault(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
        if (selected is not null) return selected;
        var more = properties.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .FirstOrDefault(c => AutomationProperties.GetName(c) == "More properties");
        if (more is not null) more.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        foreach (var section in properties.GetVisualDescendants().OfType<Expander>().ToArray())
            section.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        selected = properties.GetVisualDescendants().OfType<T>()
            .SingleOrDefault(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
        if (selected is not null) return selected;
        return window.FindControl<Border>("FamilyPaletteHost")!.GetVisualDescendants().OfType<T>()
            .Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
    }

    public static void Click(MainWindow window, string name)
    {
        var panel = window.FindControl<Border>("ClassInspectorHost")!;
        var button = panel.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == name);
        if (button is null && name.StartsWith("Apply ", StringComparison.Ordinal))
            CommitProperties(window);
        else if (button is null && name == "Cancel edit")
        {
            var input = panel.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(c => c.IsEffectivelyVisible);
            (input ?? panel.Child!).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        }
        else
            (button ?? Find<Button>(window, name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    public static void CommitProperties(MainWindow window)
    {
        var input = window.FindControl<Border>("ClassInspectorHost")!.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(c => c.IsEffectivelyVisible);
        if (input is not null)
        {
            input.Focus();
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
        }
        Dispatcher.UIThread.RunJobs();
    }
}
