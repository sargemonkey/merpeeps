using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

public readonly record struct DrawingOwner(string Role, string Id, string? DetailId = null);
public enum DrawingFigureKind { Rectangle, Ellipse, Polyline, Polygon, Path }
public enum DrawingPathKind { Move, Line, Cubic, Close }
public sealed record DrawingPathPart(DrawingPathKind Kind, DiagramPoint End, DiagramPoint Control1 = default, DiagramPoint Control2 = default);
public sealed record DrawingFont(string Family = "Segoe UI", double Size = 14, int Weight = 400, bool Italic = false);
public readonly record struct DrawingTextSize(double Width, double Height);
public delegate DrawingTextSize DrawingTextMeasure(string text, DrawingFont font);
public sealed record DrawingLinearGradient(ClassColor Start, ClassColor End, double Angle = 0);
public sealed record DrawingFigure(
    string Id, DrawingOwner Owner, DrawingFigureKind Kind, DiagramRect Bounds,
    IReadOnlyList<DiagramPoint> Points, IReadOnlyList<DrawingPathPart> Path,
    ClassColor? Fill, ClassColor? Stroke, double StrokeWidth = 1, bool Dashed = false,
    int Layer = 0, DiagramRect? Clip = null, DrawingLinearGradient? Gradient = null, IReadOnlyList<double>? DashPattern = null);
public sealed record DrawingLabel(
    string Id, DrawingOwner Owner, string Text, DrawingFont Font, ClassColor Color,
    DiagramRect Bounds, string Alignment = "left", double Rotation = 0,
    ClassColor? Outline = null, double OutlineWidth = 0);
public enum DrawingHitKind { Rectangle, Circle, Polygon, Polyline, Sector, CircleIntersection }
public sealed record DrawingCircle(DiagramPoint Center, double Radius);
public sealed record DrawingRegion(
    DrawingOwner Owner, DrawingHitKind Kind, DiagramRect Bounds,
    IReadOnlyList<DiagramPoint> Points, double StartAngle = 0, double SweepAngle = 0, double InnerRadius = 0, DiagramRect? Clip = null,
    IReadOnlyList<DrawingCircle>? Circles = null);
public sealed record DrawingScene(
    double Width, double Height, IReadOnlyList<DrawingFigure> Figures,
    IReadOnlyList<DrawingLabel> Labels, IReadOnlyList<DrawingRegion> Regions,
    bool UsesApproximateTextMetrics);

public static class DrawingHitTest
{
    public static DrawingOwner? Hit(DrawingScene scene, DiagramPoint point, double tolerance = 5)
    {
        foreach (var region in scene.Regions.Reverse())
        {
            var r = region.Bounds;
            if (region.Clip is { } clip && (point.X < clip.X || point.Y < clip.Y || point.X > clip.X + clip.Width || point.Y > clip.Y + clip.Height)) continue;
            if (point.X < r.X - tolerance || point.Y < r.Y - tolerance || point.X > r.X + r.Width + tolerance || point.Y > r.Y + r.Height + tolerance) continue;
            if (region.Kind == DrawingHitKind.Rectangle) return region.Owner;
            if (region.Kind == DrawingHitKind.CircleIntersection && region.Circles is { Count: > 0 } circles &&
                circles.All(c => Math.Pow(point.X - c.Center.X, 2) + Math.Pow(point.Y - c.Center.Y, 2) <= c.Radius * c.Radius))
                return region.Owner;
            var dx = point.X - r.X - r.Width / 2;
            var dy = point.Y - r.Y - r.Height / 2;
            if (region.Kind == DrawingHitKind.Circle && dx * dx + dy * dy <= Math.Pow(r.Width / 2 + tolerance, 2)) return region.Owner;
            if (region.Kind == DrawingHitKind.Sector)
            {
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < region.InnerRadius || distance > r.Width / 2) continue;
                var angle = (Math.Atan2(dy, dx) - region.StartAngle) % (2 * Math.PI);
                if (angle < 0) angle += 2 * Math.PI;
                if (angle <= region.SweepAngle + 1e-9) return region.Owner;
            }
            if (region.Kind == DrawingHitKind.Polyline)
                for (var i = 1; i < region.Points.Count; i++)
                    if (Distance(point, region.Points[i - 1], region.Points[i]) <= tolerance) return region.Owner;
            if (region.Kind == DrawingHitKind.Polygon)
            {
                var inside = false;
                for (int i = 0, j = region.Points.Count - 1; i < region.Points.Count; j = i++)
                {
                    var a = region.Points[i]; var b = region.Points[j];
                    if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
                if (inside) return region.Owner;
            }
        }
        return null;
    }
    private static double Distance(DiagramPoint p, DiagramPoint a, DiagramPoint b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return Math.Sqrt(Math.Pow(p.X - a.X - t * dx, 2) + Math.Pow(p.Y - a.Y - t * dy, 2));
    }
}
