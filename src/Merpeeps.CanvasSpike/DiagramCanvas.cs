using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Merpeeps.CanvasSpike;

/// <summary>
/// Custom-draw diagram surface. Deliberately does the work a real Merpeeps canvas
/// must do every frame: transform, viewport-cull, draw rounded node boxes,
/// stroke edges, and lay out text labels.
/// </summary>
public sealed class DiagramCanvas : Control
{
    private readonly record struct Node(double X, double Y, double W, double H, string Label);
    private readonly record struct Edge(int From, int To);

    private readonly Node[] _nodes;
    private readonly Edge[] _edges;
    private readonly IBrush _fill = new SolidColorBrush(Color.FromRgb(0xDA, 0xE8, 0xFC));
    private readonly IPen _stroke = new Pen(new SolidColorBrush(Color.FromRgb(0x6C, 0x8E, 0xBF)), 1.5);
    private readonly IPen _edgePen = new Pen(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), 1);
    private readonly IBrush _text = Brushes.Black;
    private readonly Typeface _face = new(FontFamily.Default);
    private readonly List<double> _frames = new();

    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public int DrawnLastFrame { get; private set; }
    public bool DrawLabels { get; set; } = true;

    public DiagramCanvas(int nodeCount)
    {
        var rnd = new Random(42);
        var cols = (int)Math.Ceiling(Math.Sqrt(nodeCount));
        _nodes = new Node[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            var (cx, cy) = (i % cols, i / cols);
            _nodes[i] = new Node(cx * 220.0, cy * 120.0, 160, 60, $"Service {i}");
        }
        // ~1.4 edges per node, typical of an architecture graph
        var edges = new List<Edge>(nodeCount * 3 / 2);
        for (var i = 0; i < nodeCount - 1; i++)
        {
            edges.Add(new Edge(i, i + 1));
            if (i % 2 == 0) edges.Add(new Edge(i, rnd.Next(nodeCount)));
        }
        _edges = edges.ToArray();
    }

    public int NodeCount => _nodes.Length;
    public int EdgeCount => _edges.Length;
    public IReadOnlyList<double> Frames => _frames;

    /// <summary>Bounding box of the whole graph in world coordinates.</summary>
    public Rect WorldBounds
    {
        get
        {
            if (_nodes.Length == 0) return new Rect(0, 0, 1, 1);
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var n in _nodes)
            {
                minX = Math.Min(minX, n.X); minY = Math.Min(minY, n.Y);
                maxX = Math.Max(maxX, n.X + n.W); maxY = Math.Max(maxY, n.Y + n.H);
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var sw = Stopwatch.StartNew();

        using var _ = ctx.PushTransform(
            Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(PanX, PanY));

        // Viewport in world space — everything outside is culled, exactly as a
        // real infinite canvas must do.
        var vw = Bounds.Width / Zoom;
        var vh = Bounds.Height / Zoom;
        var ox = -PanX / Zoom;
        var oy = -PanY / Zoom;
        var view = new Rect(ox, oy, vw, vh);

        var drawn = 0;

        foreach (var e in _edges)
        {
            var a = _nodes[e.From];
            var b = _nodes[e.To];
            var p1 = new Point(a.X + a.W / 2, a.Y + a.H);
            var p2 = new Point(b.X + b.W / 2, b.Y);
            if (!view.Intersects(new Rect(p1, p2).Inflate(2))) continue;
            ctx.DrawLine(_edgePen, p1, p2);
        }

        foreach (var n in _nodes)
        {
            var r = new Rect(n.X, n.Y, n.W, n.H);
            if (!view.Intersects(r)) continue;
            ctx.DrawRectangle(_fill, _stroke, r, 6, 6);
            if (DrawLabels && Zoom > 0.35)
            {
                var ft = new FormattedText(n.Label, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _face, 12, _text);
                ctx.DrawText(ft, new Point(n.X + 10, n.Y + 20));
            }
            drawn++;
        }

        DrawnLastFrame = drawn;
        sw.Stop();
        _frames.Add(sw.Elapsed.TotalMilliseconds);
    }

    public void ResetStats() => _frames.Clear();
}
