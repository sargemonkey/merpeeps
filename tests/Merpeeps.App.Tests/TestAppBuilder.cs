using Avalonia;
using Avalonia.Headless;

// Runs the whole suite on a headless Avalonia application: real controls, real
// XAML, real event handlers — no display, no window manager, no screenshots.
// That is what makes these runnable in CI rather than only on a desktop.
[assembly: AvaloniaTestApplication(typeof(Merpeeps.App.Tests.TestAppBuilder))]

namespace Merpeeps.App.Tests;

public static class TestAppBuilder
{
    public static Avalonia.AppBuilder BuildAvaloniaApp() =>
        Avalonia.AppBuilder.Configure<Merpeeps.App.App>()
            // UseHeadlessDrawing:false swaps the no-op renderer for real Skia, so
            // CaptureRenderedFrame returns the pixels a person would actually see.
            // Without it a test can only assert what the code *intends* to draw,
            // which is precisely the gap that let "icons show with styling off"
            // survive a green suite.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            // UseHeadlessDrawing:false leaves no renderer registered, so Skia has
            // to be supplied explicitly.
            .UseSkia()
            // Pinned to Light so assertions never depend on the machine's
            // system theme. A CI runner and a developer's dark desktop would
            // otherwise produce different colours from the same test.
            .AfterSetup(_ => Avalonia.Application.Current!.RequestedThemeVariant =
                Avalonia.Styling.ThemeVariant.Light);
}
