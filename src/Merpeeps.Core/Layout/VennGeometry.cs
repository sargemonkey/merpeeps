using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

public sealed record VennCircle(string SetId, double X, double Y, double Radius);
public sealed record VennArc(VennCircle Circle, double StartAngle, double SweepAngle);
public sealed record VennAreaFit(string AreaId, double RequestedArea, double ActualArea, bool Implied)
{
    public double RelativeError => Math.Abs(ActualArea - RequestedArea) / Math.Max(RequestedArea, 1e-9);
}
public sealed record VennGeometryResult(IReadOnlyList<VennCircle> Circles, IReadOnlyList<VennAreaFit> Areas);
public static class VennGeometry
{
    private sealed record Area(string Id, int[] Sets, double Size, bool Implied);
    public static double PairArea(double firstRadius, double secondRadius, double distance)
    {
        if (firstRadius <= 0 || secondRadius <= 0 || distance >= firstRadius + secondRadius) return 0;
        if (distance <= Math.Abs(firstRadius - secondRadius)) return Math.PI * Math.Pow(Math.Min(firstRadius, secondRadius), 2);
        var a = Math.Acos(Math.Clamp((distance * distance + firstRadius * firstRadius - secondRadius * secondRadius) / (2 * distance * firstRadius), -1, 1));
        var b = Math.Acos(Math.Clamp((distance * distance + secondRadius * secondRadius - firstRadius * firstRadius) / (2 * distance * secondRadius), -1, 1));
        var triangle = Math.Sqrt(Math.Max(0, (-distance + firstRadius + secondRadius) * (distance + firstRadius - secondRadius) * (distance - firstRadius + secondRadius) * (distance + firstRadius + secondRadius))) / 2;
        return firstRadius * firstRadius * a + secondRadius * secondRadius * b - triangle;
    }
    public static IReadOnlyList<VennArc> IntersectionArcs(IReadOnlyList<VennCircle> circles)
    {
        if (circles.Count == 0 || circles.Any(c => c.Radius <= 0)) return [];
        var unique = new List<VennCircle>();
        foreach (var circle in circles)
            if (!unique.Any(c => Math.Abs(c.X - circle.X) < 1e-10 && Math.Abs(c.Y - circle.Y) < 1e-10 && Math.Abs(c.Radius - circle.Radius) < 1e-10)) unique.Add(circle);
        var arcs = new List<VennArc>();
        foreach (var circle in unique)
        {
            var angles = new List<double> { 0, Math.PI * 2 };
            foreach (var other in unique.Where(c => !ReferenceEquals(c, circle)))
            {
                var dx = other.X - circle.X; var dy = other.Y - circle.Y; var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= 1e-12 || distance >= circle.Radius + other.Radius || distance <= Math.Abs(circle.Radius - other.Radius)) continue;
                var mid = Math.Atan2(dy, dx); var delta = Math.Acos(Math.Clamp((circle.Radius * circle.Radius + distance * distance - other.Radius * other.Radius) / (2 * circle.Radius * distance), -1, 1));
                angles.Add(Normalize(mid - delta)); angles.Add(Normalize(mid + delta));
            }
            angles = angles.Order().Distinct().ToList();
            for (var i = 1; i < angles.Count; i++)
            {
                var middle = (angles[i - 1] + angles[i]) / 2;
                var x = circle.X + circle.Radius * Math.Cos(middle); var y = circle.Y + circle.Radius * Math.Sin(middle);
                if (unique.All(c => Math.Pow(x - c.X, 2) + Math.Pow(y - c.Y, 2) <= c.Radius * c.Radius + 1e-10))
                    arcs.Add(new(circle, angles[i - 1], angles[i] - angles[i - 1]));
            }
        }
        return arcs;
    }
    public static double IntersectionArea(IReadOnlyList<VennCircle> circles)
    {
        if (circles.Count == 2)
        {
            var a = circles[0]; var b = circles[1];
            return PairArea(a.Radius, b.Radius, Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)));
        }
        var area = 0d;
        foreach (var arc in IntersectionArcs(circles))
        {
            var c = arc.Circle; var end = arc.StartAngle + arc.SweepAngle;
            area += .5 * (c.Radius * c.Radius * arc.SweepAngle +
                c.Radius * c.X * (Math.Sin(end) - Math.Sin(arc.StartAngle)) +
                c.Radius * c.Y * (Math.Cos(arc.StartAngle) - Math.Cos(end)));
        }
        return Math.Max(0, area);
    }
    public static VennGeometryResult Fit(VennDiagramDocument document)
    {
        VennValidation.Check(document);
        if (document.Sets.Count == 0) return new([], []);
        var scale = Math.Max(1e-12, document.Sets.Max(s => s.EffectiveSize));
        var radii = document.Sets.Select(s => Math.Sqrt(s.EffectiveSize / scale / Math.PI)).ToArray();
        var index = document.Sets.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var areas = document.Overlaps.Select(o => new Area(o.Id, o.SetIds.Select(id => index[id]).ToArray(), o.EffectiveSize / scale, false)).ToList();
        var pairs = new Dictionary<(int, int), double>();
        foreach (var area in areas.Where(a => a.Sets.Length == 2)) pairs[Pair(area.Sets[0], area.Sets[1])] = area.Size;
        foreach (var area in areas.Where(a => a.Sets.Length >= 3).ToArray())
            for (var i = 0; i < area.Sets.Length; i++)
                for (var j = i + 1; j < area.Sets.Length; j++)
                {
                    var pair = Pair(area.Sets[i], area.Sets[j]);
                    if (pairs.ContainsKey(pair)) continue;
                    var size = Math.Min(document.Sets[pair.Item1].EffectiveSize, document.Sets[pair.Item2].EffectiveSize) / scale / 4;
                    pairs[pair] = size;
                    areas.Add(new("implied:" + pair.Item1 + ":" + pair.Item2, [pair.Item1, pair.Item2], size, true));
                }
        for (var i = 0; i < radii.Length; i++)
            for (var j = i + 1; j < radii.Length; j++)
                if (!pairs.ContainsKey((i, j))) { pairs[(i, j)] = 0; areas.Add(new("disjoint:" + i + ":" + j, [i, j], 0, true)); }
        var circles = document.Sets.Select((s, i) => new VennCircle(s.Id, 0, 0, radii[i])).ToArray();
        if (circles.Length == 1) return new(circles, []);
        double Distance(int a, int b)
        {
            var target = pairs[Pair(a, b)]; var low = Math.Abs(radii[a] - radii[b]); var high = radii[a] + radii[b];
            if (target <= 0) return high + .05;
            for (var step = 0; step < 65; step++)
            {
                var middle = (low + high) / 2;
                if (PairArea(radii[a], radii[b], middle) > target) low = middle; else high = middle;
            }
            return (low + high) / 2;
        }
        circles[1] = circles[1] with { X = Distance(0, 1) };
        for (var i = 2; i < circles.Length; i++)
        {
            var radius = Distance(0, i); var baseline = circles[1].X;
            var x = baseline > 1e-9 ? (radius * radius + baseline * baseline - Math.Pow(Distance(1, i), 2)) / (2 * baseline) : radius;
            x = Math.Clamp(x, -radius, radius);
            var y = Math.Sqrt(Math.Max(0, radius * radius - x * x)) * (i % 2 == 0 ? 1 : -1);
            if (i > 3) { var angle = i * 2.399963229728653; x = radius * Math.Cos(angle); y = radius * Math.Sin(angle); }
            circles[i] = circles[i] with { X = x, Y = y };
        }
        if (circles.Length > 2)
        {
            var initial = new double[circles.Length * 2 - 3]; initial[0] = circles[1].X;
            for (var i = 2; i < circles.Length; i++) { initial[i * 2 - 3] = circles[i].X; initial[i * 2 - 2] = circles[i].Y; }
            var best = Optimize(initial, Loss);
            circles = Decode(best);
        }
        var fitted = areas.Select(a => new VennAreaFit(a.Id, a.Size * scale, IntersectionArea(a.Sets.Select(i => circles[i]).ToArray()) * scale, a.Implied)).ToArray();
        var physical = Math.Sqrt(scale);
        return new(circles.Select(c => c with { X = c.X * physical, Y = c.Y * physical, Radius = c.Radius * physical }).ToArray(), fitted);
        VennCircle[] Decode(double[] values)
        {
            var result = circles.ToArray(); result[0] = result[0] with { X = 0, Y = 0 }; result[1] = result[1] with { X = values[0], Y = 0 };
            for (var i = 2; i < result.Length; i++) result[i] = result[i] with { X = values[i * 2 - 3], Y = values[i * 2 - 2] };
            return result;
        }
        double Loss(double[] values)
        {
            var c = Decode(values); var loss = 0d;
            foreach (var area in areas)
            {
                var actual = IntersectionArea(area.Sets.Select(i => c[i]).ToArray()); var difference = actual - area.Size;
                loss += difference * difference;
            }
            return loss;
        }
    }
    private static double[] Optimize(double[] initial, Func<double[], double> loss)
    {
        var n = initial.Length;
        var points = new (double[] P, double Cost)[n + 1];
        points[0] = (initial.ToArray(), loss(initial));
        for (var i = 0; i < n; i++)
        {
            var point = initial.ToArray(); point[i] += .08; points[i + 1] = (point, loss(point));
        }
        for (var step = 0; step < 450 + 50 * n; step++)
        {
            Array.Sort(points, (a, b) => a.Cost.CompareTo(b.Cost));
            if (points[0].Cost < 1e-14) break;
            var center = new double[n];
            for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) center[j] += points[i].P[j] / n;
            double[] At(double factor) => center.Select((value, i) => value + factor * (center[i] - points[n].P[i])).ToArray();
            var reflected = At(1); var score = loss(reflected);
            if (score < points[0].Cost)
            {
                var expanded = At(2); var expandedScore = loss(expanded);
                points[n] = expandedScore < score ? (expanded, expandedScore) : (reflected, score);
            }
            else if (score < points[n - 1].Cost) points[n] = (reflected, score);
            else
            {
                var contracted = At(score < points[n].Cost ? .5 : -.5); var contractedScore = loss(contracted);
                if (contractedScore < Math.Min(score, points[n].Cost)) points[n] = (contracted, contractedScore);
                else
                    for (var i = 1; i <= n; i++)
                    {
                        var shrunk = points[i].P.Select((v, j) => (v + points[0].P[j]) / 2).ToArray();
                        points[i] = (shrunk, loss(shrunk));
                    }
            }
        }
        return points.MinBy(p => p.Cost).P;
    }
    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);
    private static double Normalize(double angle) { angle %= Math.PI * 2; return angle < 0 ? angle + Math.PI * 2 : angle; }
}
