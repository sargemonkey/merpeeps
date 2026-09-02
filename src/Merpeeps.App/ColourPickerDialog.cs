using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Merpeeps.App;

/// <summary>
/// A modal colour picker: a hue/saturation wheel, a brightness slider, a hex
/// field and a grid of presets.
/// </summary>
/// <remarks>
/// <para>The eight-swatch palette in the property pane is a curated set that
/// keeps diagrams looking coherent, and it stays the fast path. But a fixed
/// palette is a wall: the moment someone needs their organisation's brand
/// colour the editor's answer was "you cannot have it" — while the format has
/// always stored an arbitrary hex string, and hand-editing the source worked
/// fine. The limitation was the UI's alone.</para>
///
/// <para><b>Why this is hand-built rather than Avalonia's ColorPicker.</b> That
/// package was tried first and abandoned after failing twice: adding its styles
/// to the application hung startup — and every headless test with it — and
/// loading them per-window crashed the app outright, because the package's XAML
/// is precompiled and cannot be resolved as a StyleInclude from another
/// assembly. It ships no theme class either.</para>
/// </remarks>
public static class ColourPickerDialog
{
    /// <summary>
    /// Ask for a colour. Returns a <c>#RRGGBB</c> string, or null if cancelled.
    /// </summary>
    /// <param name="owner">The window to centre on and block.</param>
    /// <param name="title">What is being coloured.</param>
    /// <param name="initial">The current colour as a hex string, if any.</param>
    public static async Task<string?> PickAsync(Window owner, string title, string? initial)
    {
        var start = ParseHex(initial) ?? Color.FromRgb(0x6C, 0x8E, 0xBF);
        var (h, s, v) = ColourMath.RgbToHsv(start.R, start.G, start.B);
        var colour = start;

        var wheel = new ColourWheel { Width = 220, Height = 220 };
        wheel.SetHueSaturation(h, s);

        var brightness = new Slider
        {
            Minimum = 0, Maximum = 100, Value = v * 100,
            Width = 220,
        };

        var preview = new Border
        {
            Height = 44, Width = 84,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#999")),
            Background = new SolidColorBrush(colour),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var hex = new TextBox
        {
            Text = Hex(colour),
            Width = 124,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            PlaceholderText = "#RRGGBB",
            VerticalAlignment = VerticalAlignment.Center,
        };

        // One direction at a time. Without this the wheel and the hex box drive
        // each other: updating the text from the wheel raises TextChanged, which
        // parses it and moves the wheel back, so a drag stutters and snaps.
        var updating = false;

        void Show(Color c)
        {
            colour = c;
            preview.Background = new SolidColorBrush(c);
        }

        void FromWheel()
        {
            if (updating) return;
            updating = true;
            try
            {
                var (r, g, b) = ColourMath.HsvToRgb(
                    wheel.Hue, wheel.Saturation, brightness.Value / 100);
                Show(Color.FromRgb(r, g, b));
                hex.Text = Hex(colour);
            }
            finally { updating = false; }
        }

        void FromHex()
        {
            if (updating) return;
            if (ParseHex(hex.Text) is not { } parsed) return;   // mid-typing is not an error
            updating = true;
            try
            {
                Show(parsed);
                var (hh, ss, vv) = ColourMath.RgbToHsv(parsed.R, parsed.G, parsed.B);

                // Grey has no hue, so leave the wheel's angle where it was.
                // Snapping to red every time someone types a grey would move the
                // marker off the axis they were working along.
                wheel.SetHueSaturation(ss < 1e-6 ? wheel.Hue : hh, ss);
                brightness.Value = vv * 100;
            }
            finally { updating = false; }
        }

        wheel.Picked += (_, _) => FromWheel();
        brightness.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) FromWheel();
        };
        hex.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) FromHex();
        };

        // A broader spread than the eight in the property pane, so most "I need
        // a different blue" cases are one click rather than aiming at a wheel.
        var swatches = new WrapPanel { MaxWidth = 260 };
        foreach (var preset in Presets)
        {
            var btn = new Button
            {
                Width = 20, Height = 20,
                Margin = new Thickness(0, 0, 2, 2),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.Parse(preset)),
                BorderBrush = new SolidColorBrush(Color.Parse("#999")),
                BorderThickness = new Thickness(1),
                [ToolTip.TipProperty] = preset,
            };
            btn.Click += (_, _) => hex.Text = preset;
            swatches.Children.Add(btn);
        }

        string? picked = null;
        var ok = new Button { Content = "Use this colour", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    wheel,
                    Labelled("Brightness", brightness),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children = { preview, hex },
                    },
                    new TextBlock { Text = "Presets", Opacity = 0.7, FontSize = 11 },
                    swatches,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 6, 0, 0),
                        Children = { cancel, ok },
                    },
                },
            },
        };

        ok.Click += (_, _) => { picked = Hex(colour); window.Close(); };
        cancel.Click += (_, _) => window.Close();

        await window.ShowDialog(owner);
        return picked;
    }

    private static Control Labelled(string label, Control control) => new StackPanel
    {
        Spacing = 2,
        Children =
        {
            new TextBlock { Text = label, Opacity = 0.7, FontSize = 11 },
            control,
        },
    };

    /// <summary>
    /// Six-digit hex, upper case, no alpha.
    /// </summary>
    /// <remarks>
    /// Alpha is dropped deliberately. This is written into a mermaid file and
    /// read by renderers we do not control, and an eight-digit hex is not
    /// universally understood — a colour that silently failed to parse elsewhere
    /// would be worse than one the picker never offered.
    /// </remarks>
    internal static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// Parse a hex colour, or null if it is not one yet.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception because this runs on every keystroke: "#AB"
    /// is a half-typed colour, not a mistake to complain about.
    /// </remarks>
    internal static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim();
        if (!s.StartsWith('#')) s = "#" + s;

        // Avalonia accepts named colours and 8-digit forms; restrict to the
        // shapes that round-trip into a mermaid file unchanged.
        if (s.Length is not (4 or 7)) return null;
        return Color.TryParse(s, out var c) ? c : null;
    }

    /// <summary>A broad, flat palette — the common hues at several lightnesses.</summary>
    private static readonly string[] Presets =
    [
        "#FFFFFF", "#F5F5F5", "#E0E0E0", "#BDBDBD", "#757575", "#424242", "#1A1A1A", "#000000",
        "#FFCDD2", "#EF9A9A", "#E57373", "#F44336", "#D32F2F", "#B71C1C", "#FF8A80", "#FF5252",
        "#F8BBD0", "#CE93D8", "#BA68C8", "#9C27B0", "#7B1FA2", "#4A148C", "#B39DDB", "#7E57C2",
        "#BBDEFB", "#90CAF9", "#64B5F6", "#2196F3", "#1976D2", "#0D47A1", "#80DEEA", "#00ACC1",
        "#C8E6C9", "#A5D6A7", "#81C784", "#4CAF50", "#388E3C", "#1B5E20", "#DCEDC8", "#AED581",
        "#FFF9C4", "#FFF59D", "#FFEE58", "#FFC107", "#FF9800", "#E65100", "#FFCCBC", "#FF7043",
    ];
}
