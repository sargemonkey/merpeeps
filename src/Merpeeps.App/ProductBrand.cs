using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Merpeeps.App;

public static class ProductBrand
{
    public const string Name = "Merpie";
    public const string WindowTitle = "Merpie - visual editor for Mermaid";
    private const string AssetRoot = "avares://Merpie.App/Assets/Brand/";

    public static Bitmap AppIcon { get; } = Load("merpie-app-icon-128.png");
    public static Bitmap MascotLight { get; } = Load("merpie-mascot.png");
    public static Bitmap MascotDark { get; } = Load("merpie-mascot-dark.png");
    public static WindowIcon WindowIcon { get; } = LoadWindowIcon();

    private static Bitmap Load(string file)
    {
        using var stream = AssetLoader.Open(new Uri(AssetRoot + file));
        return new Bitmap(stream);
    }

    private static WindowIcon LoadWindowIcon()
    {
        using var stream = AssetLoader.Open(new Uri(AssetRoot + "merpie-app-icon.ico"));
        return new WindowIcon(stream);
    }
}
