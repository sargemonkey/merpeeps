using Avalonia;
using Avalonia.Controls;

namespace Merpeeps.CanvasSpike;

/// <summary>
/// Two scenarios per graph size:
///   navigate — pan/zoom around a large graph (viewport culling active).
///   fit-all  — zoomed out so EVERY node is on screen (worst case, no culling).
/// A warmup pass is discarded so JIT and shader compilation don't skew results.
/// </summary>
public static class Benchmark
{
    public record SizeResult(
        string scenario, int nodes, int edges, int drawnLastFrame,
        double meanMs, double p50Ms, double p95Ms, double maxMs, int frames);

    public static async Task<List<SizeResult>> RunAsync(
        Window window, int[] sizes, int framesPerSize)
    {
        var results = new List<SizeResult>();

        // Global warmup so the first measured size isn't paying JIT costs.
        var warm = new DiagramCanvas(2000);
        window.Content = warm;
        await Task.Delay(250);
        for (var i = 0; i < 40; i++)
        {
            warm.Zoom = 0.5 + 0.3 * Math.Sin(i * 0.1);
            warm.PanX = -(200.0 * i % 900);
            warm.InvalidateVisual();
            await Task.Delay(1);
        }

        foreach (var size in sizes)
        {
            results.Add(await Measure(window, size, framesPerSize, "navigate"));
            results.Add(await Measure(window, size, framesPerSize, "fit-all"));
        }
        return results;
    }

    private static async Task<SizeResult> Measure(
        Window window, int size, int frames, string scenario)
    {
        var canvas = new DiagramCanvas(size);
        window.Content = canvas;
        await Task.Delay(150);

        for (var i = 0; i < 10; i++) { canvas.InvalidateVisual(); await Task.Delay(1); }
        canvas.ResetStats();

        var t = 0.0;
        for (var i = 0; i < frames; i++)
        {
            if (scenario == "navigate")
            {
                t += 0.05;
                canvas.Zoom = 0.6 + 0.4 * Math.Sin(t);
                canvas.PanX = -400 * (1 + Math.Sin(t * 0.7));
                canvas.PanY = -300 * (1 + Math.Cos(t * 0.5));
            }
            else
            {
                var b = canvas.WorldBounds;
                var zx = window.Bounds.Width / Math.Max(b.Width, 1);
                var zy = window.Bounds.Height / Math.Max(b.Height, 1);
                canvas.Zoom = Math.Min(zx, zy) * (0.98 + 0.02 * Math.Sin(i * 0.3));
                canvas.PanX = 1 + Math.Sin(i * 0.2);
                canvas.PanY = 1 + Math.Cos(i * 0.2);
                canvas.DrawLabels = false;
            }
            canvas.InvalidateVisual();
            await Task.Delay(1);
        }

        var f = canvas.Frames.OrderBy(x => x).ToArray();
        if (f.Length == 0)
            return new SizeResult(scenario, size, canvas.EdgeCount, 0, 0, 0, 0, 0, 0);

        return new SizeResult(
            scenario, canvas.NodeCount, canvas.EdgeCount, canvas.DrawnLastFrame,
            Math.Round(f.Average(), 3),
            Math.Round(f[f.Length / 2], 3),
            Math.Round(f[Math.Min(f.Length - 1, (int)(f.Length * 0.95))], 3),
            Math.Round(f[^1], 3),
            f.Length);
    }
}
