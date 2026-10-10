using System.Xml.Linq;

namespace Merpeeps.Core.Export;

public static class SvgBackground
{
    /// <summary>Remove only a generated full-canvas rect; document fills and label backplates remain.</summary>
    public static string MakeTransparent(string source)
    {
        var document = XDocument.Parse(source);
        var root = document.Root ?? throw new ArgumentException("Missing SVG root.");
        XNamespace ns = "http://www.w3.org/2000/svg";
        foreach (var rect in root.Elements(ns + "rect").Where(r => (string?)r.Attribute("data-canvas-background") == "true").ToArray())
            rect.Remove();
        root.SetAttributeValue("data-canvas-background", "transparent");
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + document + "\n";
    }
}
