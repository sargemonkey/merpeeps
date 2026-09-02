using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The shape palette is data, and data with typos fails quietly: a misspelled
/// icon name draws no icon and a bad colour draws no colour, with no error
/// anywhere. These check the entries actually resolve.
/// </summary>
public class NodePaletteTests
{
    public static TheoryData<string, PaletteItem> AllItems()
    {
        var data = new TheoryData<string, PaletteItem>();
        foreach (var c in NodePalette.All)
            foreach (var i in c.Items) data.Add(c.Name, i);
        return data;
    }

    [Fact]
    public void EveryCollectionHasAUniqueNameAndSomeItems()
    {
        var names = NodePalette.All.Select(c => c.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(NodePalette.All, c => Assert.NotEmpty(c.Items));
    }

    [Theory, MemberData(nameof(AllItems))]
    public void EveryItemIsUsable(string collection, PaletteItem item)
    {
        Assert.False(string.IsNullOrWhiteSpace(item.Name), $"{collection}: unnamed item");
        Assert.False(string.IsNullOrWhiteSpace(item.Label),
            $"{collection}/{item.Name}: no default label");
        Assert.True(Enum.IsDefined(item.Shape),
            $"{collection}/{item.Name}: unknown shape");
    }

    /// <summary>
    /// A misspelled icon is invisible rather than an error, so the palette must
    /// only name icons the vocabulary actually recognises.
    /// </summary>
    [Theory, MemberData(nameof(AllItems))]
    public void EveryIconExists(string collection, PaletteItem item)
    {
        if (item.Icon is null) return;
        Assert.True(IconVocabulary.IsKnown(item.Icon),
            $"{collection}/{item.Name}: '{item.Icon}' is not a known icon. " +
            $"Available: {string.Join(", ", IconVocabulary.Names.Where(n => n.Length > 0))}");
    }

    [Theory, MemberData(nameof(AllItems))]
    public void EveryColourParses(string collection, PaletteItem item)
    {
        foreach (var (which, value) in new[] { ("fill", item.Fill), ("stroke", item.Stroke) })
        {
            if (value is null) continue;
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$"),
                $"{collection}/{item.Name}: {which} '{value}' is not a 6-digit hex colour");
        }
    }

    /// <summary>
    /// A stencil is meant to save configuration, so a styled entry should set
    /// both fill and outline — one without the other looks half-finished.
    /// </summary>
    [Theory, MemberData(nameof(AllItems))]
    public void StylingIsAllOrNothing(string collection, PaletteItem item)
    {
        Assert.True(item.Fill is null == item.Stroke is null,
            $"{collection}/{item.Name}: has a fill or an outline but not both");
    }

    [Theory, MemberData(nameof(AllItems))]
    public void EveryItemExplainsItself(string collection, PaletteItem item) =>
        Assert.False(string.IsNullOrWhiteSpace(item.Hint),
            $"{collection}/{item.Name}: no hint, so the palette cannot explain it");

    [Fact]
    public void ItemNamesAreUniqueWithinACollection()
    {
        foreach (var c in NodePalette.All)
        {
            var names = c.Items.Select(i => i.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }

    /// <summary>
    /// Only the shape reaches the diagram body; icons and colours live in the
    /// Merpeeps block. If a palette entry ever changed the body beyond its shape,
    /// the file would stop rendering the same in stock mermaid.
    /// </summary>
    [Theory, MemberData(nameof(AllItems))]
    public void OnlyTheShapeAffectsTheDiagramBody(string collection, PaletteItem item)
    {
        const string src = "flowchart TD\n    seed[Seed]\n";
        var model = MerpeepsDocument.Load(src);

        model.Nodes.Add(new DiagramNode
        {
            Id = "palette",
            Label = item.Label,
            Shape = item.Shape,
            Icon = item.Icon,
            Fill = item.Fill,
            Stroke = item.Stroke,
            X = 0, Y = 200, HasExplicitPosition = true,
        });

        var body = MerpeepsDoc.BodyOf(MerpeepsDocument.Save(src, model));

        var where = $"{collection}/{item.Name}";
        if (item.Icon is not null)
            Assert.False(body.Contains(item.Icon), $"{where}: icon leaked into the body");
        if (item.Fill is not null)
            Assert.False(body.Contains(item.Fill), $"{where}: fill leaked into the body");
        if (item.Stroke is not null)
            Assert.False(body.Contains(item.Stroke), $"{where}: outline leaked into the body");
        Assert.True(body.Contains(item.Label), $"{where}: label missing from the body");
    }
}
