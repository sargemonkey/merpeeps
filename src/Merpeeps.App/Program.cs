using Avalonia;
using System;

namespace Merpeeps.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // A native webview can take the process down in ways a try/catch around
        // the call site never sees. Without this the editor simply vanished,
        // leaving nothing to diagnose; now it leaves a file naming the cause.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception, "unhandled");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            WriteCrashLog(e.Exception, "unobserved task");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void WriteCrashLog(Exception? ex, string kind)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "merpeeps-crash.txt");
            File.AppendAllText(path,
                $"[{DateTime.Now:u}] {kind}: {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}\n\n");
        }
        catch
        {
            // Nothing useful to do if even logging fails.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
