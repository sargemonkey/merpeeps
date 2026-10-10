using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Merpeeps.App;

internal sealed class ColourField : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    private readonly ColourSample _sample = new() { Width = 28, Height = 24 };
    private readonly TextBlock _label = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private string _value = "";
    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            _sample.Colour = ColourPickerDialog.ParseColour(value);
            _label.Text = value.Length == 0 ? "Default" : value;
        }
    }
    public ColourField()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Padding = new Thickness(8);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(_sample);
        Grid.SetColumn(_label, 1); grid.Children.Add(_label);
        var more = new TextBlock { Text = "...", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(more, 2); grid.Children.Add(more);
        Content = grid; Value = "";
        ToolTip.SetTip(this, "Choose a color");
    }
}

internal sealed class ColourSample : Control
{
    private Color? _colour;
    public Color? Colour { get => _colour; set { _colour = value; InvalidateVisual(); } }
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        using var clip = context.PushClip(bounds);
        context.FillRectangle(Brushes.White, bounds);
        for (var y = 0; y < bounds.Height; y += 6)
            for (var x = 0; x < bounds.Width; x += 6)
                if ((x / 6 + y / 6) % 2 == 0)
                    context.FillRectangle(Brushes.LightGray, new Rect(x, y, 6, 6));
        if (_colour is { } color) context.FillRectangle(new SolidColorBrush(color), bounds);
        else context.DrawLine(new Pen(Brushes.DimGray, 1), bounds.BottomLeft, bounds.TopRight);
        context.DrawRectangle(null, new Pen(Brushes.Gray, 1), bounds.Deflate(.5));
    }
}
