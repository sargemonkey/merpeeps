using System.Text.Json.Nodes;
using System.Xml.Linq;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class SankeyPresentationTests
{
    [Fact]
    public void OutlinedLabelsHaveARealOutlineAndFollowCentralLayerPlacement()
    {
        var model = MerpeepsDocument.Load("sankey\nA,B,2\nB,C,5\nC,D,3\n");
        var history = new EditHistory(model);
        history.Apply(SankeyEditing.SetOption("showValues", JsonValue.Create(false)));
        var legacy = WeightedDiagramLayout.Create(model).Scene;
        history.Apply(SankeyEditing.SetOption("labelStyle", JsonValue.Create("outlined")));
        var outlined = WeightedDiagramLayout.Create(model).Scene;
        var id = model.Sankey!.Nodes.Single(n => n.Label == "A").Id;
        var oldNode = legacy.Figures.Single(f => f.Id == id).Bounds;
        var oldLabel = legacy.Labels.Single(l => l.Owner.Id == id);
        Assert.True(oldLabel.Bounds.X > oldNode.X + oldNode.Width);
        var node = outlined.Figures.Single(f => f.Id == id).Bounds;
        var label = outlined.Labels.Single(l => l.Owner.Id == id);
        Assert.True(label.Bounds.X + label.Bounds.Width <= node.X - 5,
            "An outlined label left of the central layer must end before its node.");

        var xml = XDocument.Parse(SvgExporter.ToSvg(model));
        var text = xml.Descendants().Single(e => e.Name.LocalName == "text" && e.Attribute("data-owner")?.Value == id);
        Assert.NotNull(text.Attribute("stroke"));
        Assert.NotEqual("none", text.Attribute("stroke")!.Value);
        Assert.Equal("stroke fill", text.Attribute("paint-order")?.Value);
        Assert.True(double.Parse(text.Attribute("stroke-width")!.Value, System.Globalization.CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public void RenamePreservesNodeColorThroughLayoutSaveReopenAndUndo()
    {
        const string source = """
            ---
            config:
              sankey:
                nodeColors:
                  Power: "#c63874"
                  Industry: "#336699"
                futureOption: retained
            ---
            sankey
            Power,Industry,12
            """;
        var model = MerpeepsDocument.Load(source);
        var node = model.Sankey!.Nodes.Single(n => n.Label == "Power");
        var before = model.Sankey;
        var history = new EditHistory(model);
        history.Apply(SankeyEditing.RenameNode(node.Id, "Power grid"));
        Assert.Equal("#c63874", model.Sankey!.Configuration["nodeColors"]!["Power grid"]?.GetValue<string>());
        Assert.Null(model.Sankey.Configuration["nodeColors"]!["Power"]);
        Assert.Equal("#c63874", before.Configuration["nodeColors"]!["Power"]!.GetValue<string>());
        Assert.Equal(new ClassColor(198, 56, 116), WeightedDiagramLayout.Create(model).Scene.Figures.Single(f => f.Id == node.Id).Fill);

        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(node.Id, reopened.Sankey!.Nodes.Single(n => n.Label == "Power grid").Id);
        Assert.Equal(new ClassColor(198, 56, 116), WeightedDiagramLayout.Create(reopened).Scene.Figures.Single(f => f.Id == node.Id).Fill);
        Assert.Equal("retained", reopened.Sankey.Configuration["futureOption"]!.GetValue<string>());
        Assert.Equal("#336699", reopened.Sankey.Configuration["nodeColors"]!["Industry"]!.GetValue<string>());

        history.Undo();
        Assert.Same(before, model.Sankey);
        history.Redo();
        Assert.Equal("#c63874", model.Sankey!.Configuration["nodeColors"]!["Power grid"]!.GetValue<string>());
    }

    [Fact]
    public void ConflictingColorDestinationDoesNotMutateTheLiveDocument()
    {
        var model = MerpeepsDocument.Load("sankey\nA,B,1\n");
        model.Sankey!.Configuration["nodeColors"] = new JsonObject { ["A"] = "#112233", ["Future"] = "#445566" };
        var before = model.Sankey;
        var history = new EditHistory(model);
        Assert.Throws<ArgumentException>(() => history.Apply(SankeyEditing.RenameNode(before.Nodes[0].Id, "Future")));
        Assert.Same(before, model.Sankey);
        Assert.Equal("#112233", before.Configuration["nodeColors"]!["A"]!.GetValue<string>());
        Assert.Equal("#445566", before.Configuration["nodeColors"]!["Future"]!.GetValue<string>());
    }

    [Fact]
    public void InvalidLabelStyleIsRejectedBeforeMutationAndLegacyHasNoOutline()
    {
        var model = MerpeepsDocument.Load("sankey\nA,B,1\n");
        var before = model.Sankey;
        var history = new EditHistory(model);
        Assert.Throws<ArgumentException>(() => history.Apply(SankeyEditing.SetOption("labelStyle", JsonValue.Create("typo"))));
        Assert.Same(before, model.Sankey);
        Assert.All(WeightedDiagramLayout.Create(model).Scene.Labels, label => Assert.Null(label.Outline));
        history.Apply(SankeyEditing.SetOption("labelStyle", JsonValue.Create("outlined")));
        var saved = MerpeepsDocument.Save("sankey\nA,B,1\n", model);
        Assert.All(WeightedDiagramLayout.Create(MerpeepsDocument.Load(saved), themeOverride: "dark").Scene.Labels,
            label => { Assert.Equal(new ClassColor(31, 32, 32), label.Outline); Assert.Equal(4, label.OutlineWidth); });
    }
}
