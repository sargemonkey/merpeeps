using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace Merpeeps.App;

public enum UiGlyph
{
    Add, Layers, Open, Save, Undo, Redo, Select, Pan, Fit, Layout, Settings, Palette, Moon, Info,
    Delete, Link, Up, Down, More, Copy, Back, AlignLeft, AlignHorizontalCenter, AlignRight,
    AlignTop, AlignVerticalCenter, AlignBottom, DistributeHorizontal, DistributeVertical,
}

public sealed class UiIcon : Control
{
    public static readonly StyledProperty<UiGlyph> GlyphProperty =
        AvaloniaProperty.Register<UiIcon, UiGlyph>(nameof(Glyph));
    public UiGlyph Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public UiIcon()
    {
        Width = 18; Height = 18; IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    static UiIcon() => AffectsRender<UiIcon>(GlyphProperty);
    public override void Render(DrawingContext context)
    {
        var ink = new SolidColorBrush(Color.Parse(ActualThemeVariant == ThemeVariant.Dark ? "#B6B0C7" : "#5C5573"));
        var data = Glyph switch
        {
            UiGlyph.Add => "M12 4V20M4 12H20",
            UiGlyph.Layers => "M12 3L22 8L12 13L2 8ZM2 12L12 17L22 12M2 17L12 22L22 17",
            UiGlyph.Open => "M3 8V4H10L13 7H21V19H3ZM3 11H23L19 20H3Z",
            UiGlyph.Save => "M4 3H17L21 7V21H3V3ZM7 3V9H16V3M7 21V14H17V21",
            UiGlyph.Undo => "M8 4L3 9L8 14M3 9H14C23 9 23 21 14 21",
            UiGlyph.Redo => "M16 4L21 9L16 14M21 9H10C1 9 1 21 10 21",
            UiGlyph.Select => "M5 3L20 12L13 14L10 21Z",
            UiGlyph.Pan => "M8 12V6C8 3 12 3 12 6V11V4C12 1 16 1 16 4V11V7C16 4 20 4 20 7V14C20 19 17 22 13 22C10 22 9 20 7 17L4 13C2 10 5 9 7 11Z",
            UiGlyph.Fit => "M3 9V3H9M15 3H21V9M21 15V21H15M9 21H3V15M8 8H16V16H8Z",
            UiGlyph.Layout => "M3 3H9V9H3ZM15 3H21V9H15ZM9 15H15V21H9ZM6 9V12H18V9M12 12V15",
            UiGlyph.Settings => "M4 3V10M4 14V21M12 3V15M12 19V21M20 3V5M20 9V21M1 10H7M9 15H15M17 5H23",
            UiGlyph.Palette => "M12 3C5 3 1 9 3 15C5 22 12 23 14 19C16 15 11 15 16 13C24 13 24 3 12 3ZM7 8H8M13 6H14M18 8H19M6 14H7",
            UiGlyph.Moon => "M20 15C13 18 7 11 9 3C-3 7 2 25 15 21C18 20 20 18 20 15Z",
            UiGlyph.Info => "M12 3C0 3 0 21 12 21C24 21 24 3 12 3ZM12 11V17M12 7V8",
            UiGlyph.Delete => "M3 6H21M9 6V3H15V6M6 6L7 21H17L18 6M10 10V17M14 10V17",
            UiGlyph.Link => "M9 15L15 9M6 13L4 15C0 19 5 24 9 20L13 16M11 8L15 4C19 0 24 5 20 9L17 12",
            UiGlyph.Up => "M12 20V4M5 11L12 4L19 11",
            UiGlyph.Down => "M12 4V20M5 13L12 20L19 13",
            UiGlyph.More => "M4 11H5V12H4ZM11 11H12V12H11ZM18 11H19V12H18Z",
            UiGlyph.Copy => "M8 8H21V21H8ZM16 5V3H3V16H5",
            UiGlyph.Back => "M20 12H4M10 5L3 12L10 19",
            UiGlyph.AlignLeft => "M3 3V21M7 5H20V10H7ZM7 14H15V19H7Z",
            UiGlyph.AlignHorizontalCenter => "M12 2V22M4 5H20V10H4ZM8 14H16V19H8Z",
            UiGlyph.AlignRight => "M21 3V21M4 5H17V10H4ZM9 14H17V19H9Z",
            UiGlyph.AlignTop => "M3 3H21M5 7H10V20H5ZM14 7H19V15H14Z",
            UiGlyph.AlignVerticalCenter => "M2 12H22M5 4H10V20H5ZM14 8H19V16H14Z",
            UiGlyph.AlignBottom => "M3 21H21M5 4H10V17H5ZM14 9H19V17H14Z",
            UiGlyph.DistributeHorizontal => "M2 3V21M22 3V21M5 7H9V17H5ZM15 7H19V17H15ZM11 12H13",
            UiGlyph.DistributeVertical => "M3 2H21M3 22H21M7 5H17V9H7ZM7 15H17V19H7ZM12 11V13",
            _ => throw new ArgumentOutOfRangeException(),
        };
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale));
        context.DrawGeometry(null, new Pen(ink, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), StreamGeometry.Parse(data));
    }
    internal static void Describe(Control control, string name)
    {
        AutomationProperties.SetName(control, name);
        ToolTip.SetTip(control, name);
        control.GotFocus += (_, e) =>
        {
            if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) ToolTip.SetIsOpen(control, true);
        };
        control.LostFocus += (_, _) => ToolTip.SetIsOpen(control, false);
    }
}
