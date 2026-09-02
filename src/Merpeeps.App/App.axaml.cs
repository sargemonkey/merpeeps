using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Merpeeps.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Honour a file passed on the command line, so the editor can be
            // wired up as a file association or launched from a shell.
            var options = StartupOptions.Parse(desktop.Args);
            desktop.MainWindow = new MainWindow(options);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
