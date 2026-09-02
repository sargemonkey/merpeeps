using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Merpeeps.CanvasSpike;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var win = new Window { Width = 1280, Height = 800, Title = "Merpeeps canvas spike" };
            desktop.MainWindow = win;
            win.Opened += async (_, _) =>
            {
                var sizes = new[] { 500, 1000, 5000, 10000, 25000 };
                var results = await Benchmark.RunAsync(win, sizes, framesPerSize: 60);
                Console.WriteLine("MERPEEPS_BENCH_JSON " + JsonSerializer.Serialize(results));
                desktop.Shutdown();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
