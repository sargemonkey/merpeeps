using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Merpeeps.App;

public sealed class MerpieMascot : Image
{
    public MerpieMascot()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "Merpie sea-magpie mascot");
        ActualThemeVariantChanged += (_, _) => UpdateSource();
        UpdateSource();
    }

    private void UpdateSource() => Source =
        ActualThemeVariant == ThemeVariant.Dark ? ProductBrand.MascotDark : ProductBrand.MascotLight;
}
