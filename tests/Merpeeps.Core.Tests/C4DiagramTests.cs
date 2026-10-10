using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.Core.Tests;

public class C4DiagramTests
{
    [Theory]
    [InlineData("C4Context", C4DiagramKind.Context)]
    [InlineData("C4Container", C4DiagramKind.Container)]
    [InlineData("C4Component", C4DiagramKind.Component)]
    [InlineData("C4Dynamic", C4DiagramKind.Dynamic)]
    [InlineData("C4Deployment", C4DiagramKind.Deployment)]
    public void DialectsStartWithTheirOwnEmptyModel(string directive, C4DiagramKind kind)
    {
        var model = new C4DiagramAdapter().Parse(directive + "\n");
        Assert.Equal(kind, model.C4!.Kind);
        Assert.Empty(model.Nodes);
        Assert.Empty(model.C4.Elements);
    }

    [Fact]
    public void TypedContainmentFieldsAndInertExtrasRoundtrip()
    {
        const string source = """
            C4Container
            title Banking
            Person(customer, Customer, "Uses the bank", $tags="v1")
            Container_Boundary(bank, "Bank") {
              Container(app, "Application", "C#, HTTP", "Calls isAuthenticated()")
              ContainerDb_Ext(db, "Accounts", "SQL", "Stored accounts")
            }
            BiRel(customer, app, "Uses", "HTTPS")
            Rel_Back(db, app, "Reads", "JDBC")
            UpdateElementStyle(customer, $fontColor="red", $bgColor="grey")
            UpdateRelStyle(customer, app, $offsetY="-10", $textColor="blue")
            UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
            """;
        var adapter = new C4DiagramAdapter(); var model = adapter.Parse(source);
        var d = model.C4!;
        Assert.Equal("C#, HTTP", d.Elements.Single(e => e.Alias == "app").Technology);
        Assert.Equal(d.Elements.Single(e => e.Alias == "bank").Id, d.Elements.Single(e => e.Alias == "app").ParentId);
        Assert.True(d.Elements.Single(e => e.Alias == "db").External);
        Assert.Equal(C4ElementShape.Database, d.Elements.Single(e => e.Alias == "db").Shape);
        Assert.Contains(d.Elements.Single(e => e.Alias == "customer").ExtraArguments, a => a.Name == "tags" && a.Value == "v1");
        Assert.Equal(C4RelationshipKind.Back, d.Relationships[1].Kind);
        Assert.Equal("-10", d.Relationships[0].Style["offsetY"]!.GetValue<string>());
        var written = adapter.Write(model);
        Assert.Equal(written, adapter.Write(adapter.Parse(written)));
    }

    [Fact]
    public void DuplicateEndpointPairsAreNotInventedAsParallelRelations()
    {
        const string source = "C4Context\nSystem(a,\"A\")\nSystem(b,\"B\")\nRel(a,b,\"First\")\nBiRel(a,b,\"Second\")\n";
        var error = Assert.Throws<DataDiagramNotEditableException>(() => new C4DiagramAdapter().Parse(source));
        Assert.Contains(error.Diagnostics, d => d.Code == "c4-duplicate-pair");
    }

    [Fact]
    public void MeasuredContainmentPaintAndAliasEditsUseOneCanonicalModel()
    {
        var model = new C4DiagramAdapter().Parse("C4Container\nContainer_Boundary(g,\"Group\") {\nContainer(a,\"Application\",\"C#\",\"A long description with several words that wrap to the next line\")\nContainerDb(b,\"Data\",\"SQL\")\n}\nRel(a,b,\"Reads\",\"JDBC\")\nUpdateElementStyle(a,$bgColor=\"red\",$fontColor=\"white\")\nUpdateRelStyle(a,b,$offsetX=\"1.5\",$offsetY=\"-2.9\")\n");
        var layout = C4DiagramLayout.Create(model, (text, font) => new(text.Length * font.Size * .55, font.Size * 1.4));
        var d = model.C4!;
        var group = d.Elements[0]; var app = d.Elements[1]; var store = d.Elements[2];
        var outer = layout.ElementBounds[group.Id]; var inner = layout.ElementBounds[app.Id];
        Assert.True(inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width);
        Assert.Contains(layout.Scene.Labels, l => l.Owner.Id == app.Id && l.Text == "Application" && l.Font.Weight == 700);
        Assert.Equal(new ClassColor(255, 0, 0), layout.Scene.Figures.Single(f => f.Id == app.Id).Fill);
        Assert.Contains(layout.Scene.Figures, f => f.Id == store.Id + "-top");
        var relPaint = C4Appearance.Relationship(d.Relationships.Single());
        Assert.Equal(1, relPaint.OffsetX); Assert.Equal(-2, relPaint.OffsetY);
        new EditHistory(model).Apply(C4DiagramEditing.UpdateElement(app with { Alias = "newAlias" }));
        Assert.Equal(app.Id, model.C4!.Relationships.Single().From);
        Assert.Contains("Rel(newAlias", C4DiagramAdapter.Write(model.C4));
    }

    [Fact]
    public void DynamicIndexIsSourceDataWhileVisibleOrdinalFollowsRelationOrder()
    {
        var model = new C4DiagramAdapter().Parse("C4Dynamic\nSystem(a,\"A\")\nSystem(b,\"B\")\nSystem(c,\"C\")\nRelIndex(99,a,b,\"First\")\nRelIndex(77,b,c,\"Second\")\n");
        var first = model.C4!.Relationships[0];
        var history = new EditHistory(model);
        history.Apply(C4DiagramEditing.MoveRelationship(first.Id, 1));
        Assert.Equal("99", model.C4!.Relationships[1].IndexArgument);
        var labels = C4DiagramLayout.Create(model).Scene.Labels.Where(l => l.Owner.Id == first.Id).ToArray();
        Assert.Contains(labels, l => l.Text == "2.");
        history.Undo();
        Assert.Equal(first.Id, model.C4!.Relationships[0].Id);
    }

    [Fact]
    public void NativeSourceSaveKeepsElementAndRelationshipIdentities()
    {
        const string source = "C4Context\nSystem(a,\"A\")\nSystem(b,\"B\")\nRel(a,b,\"Calls\")\n";
        var model = MerpeepsDocument.Load(source);
        var element = model.C4!.Elements[0]; var relationship = model.C4.Relationships[0];
        var history = new EditHistory(model);
        history.Apply(C4DiagramEditing.UpdateElement(element with { Alias = "renamed", Label = "Updated" }));
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(element.Id, reopened.C4!.Elements[0].Id);
        Assert.Equal(relationship.Id, reopened.C4.Relationships[0].Id);
        Assert.Equal(element.Id, reopened.C4.Relationships[0].From);
        history.Undo(); Assert.Equal("a", model.C4!.Elements[0].Alias);
    }
}
