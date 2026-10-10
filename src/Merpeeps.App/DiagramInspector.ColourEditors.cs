using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core.Parsing;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private void ColourInput(Draft draft, string key, string label, string initial = "", string? defaultValue = "", Action<string>? onSelected = null)
    {
        var field = new ColourField { Value = draft.Get(key, initial) };
        DraftField(draft, key, label, field);
        field.Click += async (_, _) =>
        {
            if (_active is { Inline: true, Dirty: true } pending)
            {
                if (!TryCommitInline(pending, restoreFocus: false)) return;
                Dispatcher.UIThread.Post(() =>
                {
                    var replacement = this.GetVisualDescendants().OfType<ColourField>()
                        .FirstOrDefault(c => AutomationProperties.GetName(c) == label);
                    if (replacement is null) { _feedback.Text = "The properties changed. Open the color picker again."; return; }
                    replacement.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                });
                return;
            }
            if (!ReferenceEquals(_active, draft) || TopLevel.GetTopLevel(this) is not Window owner) return;
            var model = _model;
            var selected = await ColourPickerDialog.PickAsync(owner, label, field.Value, allowAlpha: true, defaultValue: defaultValue);
            if (selected is null) return;
            if (!ReferenceEquals(model, _model) || !ReferenceEquals(_active, draft))
            { _feedback.Text = "The selected object changed while choosing a color. Choose the color again for the current object."; return; }
            field.Value = selected;
            if (onSelected is not null) onSelected(selected);
            else Changed(draft, key, selected);
            if (draft.Inline) TryCommitInline(draft);
        };
    }

    private void ColourListInput(Draft draft, string key, string label, string initial)
    {
        var colors = ChartDiagramAdapter.SplitList(draft.Get(key, initial));
        for (var index = 0; index < colors.Count; index++)
        {
            var position = index;
            ColourInput(draft, "$palette:" + index, label + " " + (index + 1), colors[index], null, color =>
            {
                var updated = ChartDiagramAdapter.SplitList(Value(draft, key)).ToArray();
                updated[position] = color;
                draft.Values["$palette:" + position] = color;
                Changed(draft, key, string.Join(", ", updated));
            });
        }
        Button("Add series color", () =>
        {
            Changed(draft, key, string.Join(", ", ChartDiagramAdapter.SplitList(Value(draft, key)).Append("#6C8EBF")));
            RebuildEditor(draft);
        });
        if (colors.Count > 1) Button("Remove last series color", () =>
        {
            Changed(draft, key, string.Join(", ", ChartDiagramAdapter.SplitList(Value(draft, key)).Take(colors.Count - 1)));
            draft.Values.Remove("$palette:" + (colors.Count - 1));
            RebuildEditor(draft);
        });
    }

    private void StyleInput(Draft draft, string key, string initial)
    {
        if (key is "fill" or "stroke" or "color" or "stroke-color" or "textColor" or "lineColor"
            or "bgColor" or "fontColor" or "borderColor") ColourInput(draft, key, key, initial);
        else Input(draft, key, key, initial);
    }

    private void ThemeValueInput(Draft draft, string[] keys, string label)
    {
        var key = draft.Get("themeKey", keys[0]);
        var choices = keys.Select(k => new Choice(k, k)).ToArray();
        var selector = new ComboBox { ItemsSource = choices, SelectedItem = choices.Single(c => c.Id == key) };
        Field("Theme variable", selector);
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is not Choice choice || choice.Id == Value(draft, "themeKey")) return;
            if (!string.IsNullOrEmpty(draft.Get("themeValue", "")))
            {
                _feedback.Text = "Apply or clear this theme value before selecting another variable.";
                selector.SelectedItem = choices.Single(c => c.Id == Value(draft, "themeKey"));
                return;
            }
            draft.Values["themeKey"] = choice.Id;
            RebuildEditor(draft);
        };
        if (key.EndsWith("FontSize", StringComparison.Ordinal)) Input(draft, "themeValue", label);
        else ColourInput(draft, "themeValue", label);
    }
}
