using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Xunit;

namespace Merpeeps.App.Tests;

public class MerpieBrandTests
{
    [AvaloniaFact]
    public void DesktopIdentityAndArtworkLoadFromTheRenamedAssembly()
    {
        using var f = new Fixture();
        Assert.Equal("Merpie.App", typeof(MainWindow).Assembly.GetName().Name);
        Assert.Equal("Merpie", typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal(ProductBrand.WindowTitle, f.Window.Title);
        Assert.Same(ProductBrand.WindowIcon, f.Window.Icon);
        Assert.Equal("Merpie", f.Window.FindControl<TextBlock>("ProductName")!.Text);
        var icon = f.Window.FindControl<Image>("ProductIcon")!;
        Assert.Same(ProductBrand.AppIcon, icon.Source);
        Assert.Equal("Merpie app icon", AutomationProperties.GetName(icon));
        Assert.Equal(128, ProductBrand.AppIcon.PixelSize.Width);
        Assert.Equal(1024, ProductBrand.MascotLight.PixelSize.Width);
        Assert.Equal(1024, ProductBrand.MascotDark.PixelSize.Width);
        foreach (var file in new[] { "merpie-app-icon.ico", "merpie-mascot.png", "merpie-mascot-dark.png" })
        {
            var uri = new Uri("avares://Merpie.App/Assets/Brand/" + file);
            Assert.True(AssetLoader.Exists(uri));
            using var stream = AssetLoader.Open(uri);
            Assert.True(stream.Length > 0);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 900)]
    [InlineData(true, 900)]
    [InlineData(false, 1560)]
    [InlineData(true, 1560)]
    public void GalleryUsesApprovedMascotAndKeepsCreationControlsReadable(bool dark, int width)
    {
        using var f = new Fixture(dark, width);
        var gallery = f.Window.FindControl<Border>("NewDiagramHost")!;
        var mascot = gallery.GetVisualDescendants().OfType<MerpieMascot>().Single();
        Assert.True(mascot.IsEffectivelyVisible);
        Assert.Same(dark ? ProductBrand.MascotDark : ProductBrand.MascotLight, mascot.Source);
        Assert.Equal("Merpie sea-magpie mascot", AutomationProperties.GetName(mascot));
        Assert.Equal(33, gallery.GetVisualDescendants().OfType<DiagramTypePreview>().Count());
        var heading = gallery.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Create a diagram");
        var textPosition = heading.TranslatePoint(default, f.Window)!.Value;
        var imagePosition = mascot.TranslatePoint(default, f.Window)!.Value;
        Assert.True(textPosition.X + heading.Bounds.Width < imagePosition.X);
        Assert.InRange(imagePosition.X + mascot.Bounds.Width, 0, width);
        if (Environment.GetEnvironmentVariable("MERPIE_BRAND_CAPTURES") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(path);
            using var image = f.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered brand frame.");
            image.Save(Path.Combine(path, $"merpie-gallery-{width}-{(dark ? "dark" : "light")}.png"));
        }
    }

    [AvaloniaFact]
    public void RepeatedThemeSwitchesChooseTheRightMascotWithoutEditingTheDocument()
    {
        using var f = new Fixture();
        const string source = "---\nx-merpeeps:\n  spec: 1\n  layout:\n    nodes:\n      A: {x: 20, y: 30, w: 160, h: 60}\n---\nflowchart LR\nA[Existing document]\n";
        f.Window.LoadSourceForTest(source);
        Settle();
        var before = f.Window.CurrentSource;
        f.Window.FindControl<MenuItem>("MenuNew")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
        var gallery = f.Window.FindControl<Border>("NewDiagramHost")!;
        var mascot = gallery.GetVisualDescendants().OfType<MerpieMascot>().Single();
        for (var i = 0; i < 4; i++)
        {
            f.Window.FindControl<MenuItem>(i % 2 == 0 ? "MenuThemeDark" : "MenuThemeLight")!
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Settle();
            Assert.Same(i % 2 == 0 ? ProductBrand.MascotDark : ProductBrand.MascotLight, mascot.Source);
            Assert.Equal(before, f.Window.CurrentSource);
            Assert.False(f.Window.CanvasForTest.History!.CanUndo);
        }
        var reopened = MerpeepsDocument.Load(before);
        Assert.Equal(20, reopened.Nodes[0].X);
        Assert.Contains("x-merpeeps:", before);
        Assert.DoesNotContain("x-merpie:", before);
    }

    [AvaloniaFact]
    public void AboutUsesTheLockedMascotAndExplainsCompatibleTechnicalIds()
    {
        using var f = new Fixture(dark: true);
        f.Window.FindControl<MenuItem>("MenuAbout")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Settle();
        var about = Assert.Single(f.Window.OwnedWindows);
        Assert.Equal("About Merpie", about.Title);
        Assert.Same(ProductBrand.WindowIcon, about.Icon);
        Assert.Same(ProductBrand.MascotDark, about.GetVisualDescendants().OfType<MerpieMascot>().Single().Source);
        var text = about.GetVisualDescendants().OfType<SelectableTextBlock>().Single().Text!;
        Assert.Contains("x-merpeeps", text);
        Assert.Contains("merpeeps CLI/MCP", text);
        Assert.Contains("Not affiliated", text);
        var close = about.GetVisualDescendants().OfType<Button>().Single();
        Assert.Equal("Close About Merpie", AutomationProperties.GetName(close));
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.Empty(f.Window.OwnedWindows);
    }

    [AvaloniaFact]
    public void RasterMastersHaveRealAlphaAndTheIconKeepsItsIntentionalBackplate()
    {
        Assert.Equal(0u, Pixel(ProductBrand.MascotLight, 0, 0) >> 24);
        Assert.Equal(0u, Pixel(ProductBrand.MascotDark, 0, 0) >> 24);
        Assert.Equal(0u, Pixel(ProductBrand.AppIcon, 0, 0) >> 24);
        Assert.Equal(255u, Pixel(ProductBrand.AppIcon, 64, 64) >> 24);
        Assert.NotEqual(ProductBrand.MascotLight, ProductBrand.MascotDark);
    }

    private static uint Pixel(Bitmap bitmap, int x, int y)
    {
        var pixel = new uint[1];
        unsafe
        {
            fixed (uint* address = pixel)
                bitmap.CopyPixels(new Avalonia.PixelRect(x, y, 1, 1), (nint)address, sizeof(uint), sizeof(uint));
        }
        return pixel[0];
    }

    private static void Settle() => Dispatcher.UIThread.RunJobs();
    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; }
        public Fixture(bool dark = false, int width = 1560)
        {
            Window = new(new StartupOptions(null, dark ? ThemeVariant.Dark : ThemeVariant.Light)) { Width = width, Height = 920 };
            Window.Show(); Settle();
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
