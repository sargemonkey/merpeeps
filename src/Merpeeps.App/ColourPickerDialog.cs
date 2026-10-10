using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.App;

public static class ColourPickerDialog
{
    /// <summary>Null means Cancel; reset returns defaultValue, which may be an empty string.</summary>
    public static async Task<string?> PickAsync(Window owner, string title, string? initial,
        bool allowAlpha = false, string? defaultValue = null)
    {
        var picker = new ColourPickerWindow(title, initial, allowAlpha, defaultValue);
        await picker.ShowDialog(owner);
        return picker.Result;
    }

    internal static string Hex(Color c) => c.A == 255
        ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

    internal static Color? ParseHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hex = value.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        return hex.Length is 4 or 5 or 7 or 9 && SequenceColors.TryParse(hex, out var c)
            ? Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue) : null;
    }

    internal static Color? ParseColour(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return Colors.Transparent;
        return SequenceColors.TryParse(value, out var c) ? Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue) : ParseHex(value);
    }

    internal static string Source(Color c) => new ClassColor(c.R, c.G, c.B, c.A).Css;
}

internal sealed class ColourPickerWindow : Window
{
    private readonly bool _allowAlpha;
    private readonly string? _initial;
    private readonly ColourWheel _wheel = new() { Width = 200, Height = 200, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Slider _brightness = new() { Minimum = 0, Maximum = 100 };
    private readonly Slider _opacity = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly ColourSample _sample = new() { Width = 60, Height = 36 };
    private readonly TextBox _hex = new() { FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace") };
    private readonly TextBox[] _channels = new TextBox[4];
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _use = new() { Content = "Use color", IsDefault = true };
    private Color _colour;
    private bool _updating;
    private bool _changed;

    public string? Result { get; private set; }

    public ColourPickerWindow(string title, string? initial, bool allowAlpha, string? defaultValue)
    {
        Title = title;
        Width = 420;
        Height = allowAlpha ? 700 : 644;
        MinWidth = 360; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _allowAlpha = allowAlpha;
        _initial = initial;
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = allowAlpha ? "16.7 million RGB colors, plus 256 opacity levels." : "16.7 million RGB colors. This format uses opaque colors.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(_wheel);
        body.Children.Add(Labelled("Brightness", _brightness));
        if (allowAlpha) body.Children.Add(Labelled("Opacity", _opacity));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        row.Children.Add(_sample);
        Grid.SetColumn(_hex, 1); row.Children.Add(_hex);
        _hex.PlaceholderText = allowAlpha ? "#RRGGBB or #RRGGBBAA" : "#RRGGBB";
        AutomationProperties.SetName(_hex, "Hex color");
        body.Children.Add(row);
        var channels = new Grid { ColumnDefinitions = new ColumnDefinitions(allowAlpha ? "*,*,*,*" : "*,*,*"), ColumnSpacing = 8 };
        var names = new[] { "Red", "Green", "Blue", "Alpha" };
        for (var i = 0; i < 4; i++)
        {
            var field = new TextBox();
            _channels[i] = field;
            if (i < 3 || allowAlpha)
            {
                var labelled = Labelled(names[i] + " (0-255)", field);
                AutomationProperties.SetName(field, names[i]);
                Grid.SetColumn(labelled, i); channels.Children.Add(labelled);
            }
            field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) FromChannels(); };
        }
        body.Children.Add(channels);
        body.Children.Add(_error);
        AutomationProperties.SetName(_error, "Color error");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        var presets = new WrapPanel();
        foreach (var color in new[] { "#FFFFFF", "#000000", "#64748B", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#14B8A6", "#0EA5E9", "#3B82F6", "#8B5CF6", "#EC4899" })
        {
            var sample = new ColourSample { Width = 22, Height = 22, Colour = ColourPickerDialog.ParseHex(color) };
            var button = new Button { Content = sample, Padding = new Thickness(3), Margin = new Thickness(0, 0, 4, 4) };
            AutomationProperties.SetName(button, color); ToolTip.SetTip(button, color);
            button.Click += (_, _) => { _changed = true; SetColour(sample.Colour!.Value); };
            presets.Children.Add(button);
        }
        body.Children.Add(new Expander { Header = "Presets", Content = presets, HorizontalAlignment = HorizontalAlignment.Stretch });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18) };
        if (defaultValue is not null)
        {
            var reset = new Button { Content = "Use default" };
            AutomationProperties.SetName(reset, "Use default");
            reset.Click += (_, _) => { Result = defaultValue; Close(); };
            footer.Children.Add(reset);
        }
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AutomationProperties.SetName(cancel, "Cancel color");
        AutomationProperties.SetName(_use, "Use color");
        cancel.Click += (_, _) => Close();
        _use.Click += (_, _) =>
        {
            if (!_use.IsEnabled || !Validate(_colour)) return;
            Result = !_changed && ColourPickerDialog.ParseColour(_initial) is not null ? _initial : ColourPickerDialog.Source(_colour);
            Close();
        };
        footer.Children.Add(cancel); footer.Children.Add(_use);
        var layout = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = layout;

        _wheel.Picked += (_, _) => FromWheel();
        _brightness.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) FromWheel(); };
        _opacity.PropertyChanged += (_, e) =>
        {
            if (_updating) return;
            if (e.Property == RangeBase.ValueProperty)
            {
                _changed = true;
                SetColour(Color.FromArgb((byte)Math.Round(_opacity.Value), _colour.R, _colour.G, _colour.B));
            }
        };
        _hex.PropertyChanged += (_, e) =>
        {
            if (_updating || e.Property != TextBox.TextProperty) return;
            _changed = true;
            if (ColourPickerDialog.ParseColour(_hex.Text) is not { } color) { Error("Enter a valid hex, RGB, HSL, or named color."); return; }
            SetColour(color, updateHex: false);
        };
        SetColour(ColourPickerDialog.ParseColour(initial) ?? Color.FromRgb(108, 142, 191));
        if (!string.IsNullOrWhiteSpace(initial) && ColourPickerDialog.ParseColour(initial) is null)
        {
            _hex.Text = initial;
            _changed = false;
        }
    }

    private void FromWheel()
    {
        if (_updating) return;
        _changed = true;
        var (r, g, b) = ColourMath.HsvToRgb(_wheel.Hue, _wheel.Saturation, _brightness.Value / 100);
        SetColour(Color.FromArgb(_colour.A, r, g, b));
    }
    private void FromChannels()
    {
        if (_updating) return;
        _changed = true;
        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
            if (!byte.TryParse(_channels[i].Text, NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]))
            { Error("Each channel must be a whole number from 0 to 255."); return; }
        SetColour(Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2]));
    }
    private void SetColour(Color color, bool updateHex = true)
    {
        _updating = true;
        try
        {
            _colour = color; _sample.Colour = color;
            var (h, s, v) = ColourMath.RgbToHsv(color.R, color.G, color.B);
            _wheel.SetHueSaturation(s < 1e-6 ? _wheel.Hue : h, s);
            _brightness.Value = v * 100; _opacity.Value = color.A;
            if (updateHex) _hex.Text = ColourPickerDialog.Hex(color);
            var channels = new[] { color.R, color.G, color.B, color.A };
            for (var i = 0; i < 4; i++) _channels[i].Text = channels[i].ToString(CultureInfo.InvariantCulture);
            Validate(color);
        }
        finally { _updating = false; }
    }
    private bool Validate(Color color)
    {
        if (!_allowAlpha && color.A != 255) { Error("This field supports opaque RGB colors only. Use a six-digit hex color."); return false; }
        _error.Text = ""; _error.IsVisible = false; _use.IsEnabled = true; return true;
    }
    private void Error(string message) { _error.Text = message; _error.IsVisible = true; _use.IsEnabled = false; }
    private static Control Labelled(string name, Control field)
    {
        var label = new TextBlock { Text = name };
        AutomationProperties.SetName(field, name);
        AutomationProperties.SetLabeledBy(field, label);
        return new StackPanel { Spacing = 4, Children = { label, field } };
    }
}
