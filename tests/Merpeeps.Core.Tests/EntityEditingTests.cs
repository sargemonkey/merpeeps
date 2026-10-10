using System.Xml.Linq;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class EntityEditingTests
{
    [Fact]
    public void EntityAuthoringRetainsStableAttributesAndCardinalitiesAcrossSaveAndUndo()
    {
        var draft = MermaidDiagramCatalog.CreateEmpty("er");
        var history = new EditHistory(draft.Model);
        var customer = EntityEditing.CreateEntity("CUSTOMER", "Customer account");
        history.Apply(new AddEntityCommand(customer));
        history.Apply(new AddEntityCommand(EntityEditing.CreateEntity("ORDER")));
        history.Apply(EntityEditing.AddAttribute(customer, new("id", "int", "*id")));
        history.Apply(EntityEditing.AddAttribute(customer, new("name", "string?", "name", EntityKey.Unique, "Display name")));
        history.Apply(new AddEntityRelationshipCommand(new()
        {
            From = "CUSTOMER",
            To = "ORDER",
            Label = "",
            EntityRelationship = new(EntityCardinality.ExactlyOne, EntityCardinality.ZeroOrMore, false),
        }));
        var saved = MerpeepsDocument.Save(draft.Source, draft.Model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal("erDiagram", reopened.Kind);
        Assert.Equal(new[] { "id", "name" }, reopened.Node("CUSTOMER")!.Entity!.Attributes.Select(a => a.Id));
        Assert.Equal(EntityKey.Primary, reopened.Node("CUSTOMER")!.Entity!.Attributes[0].EffectiveKeys);
        history.Apply(EntityEditing.EditAttribute(customer, customer.Entity!.Attributes[0].WithKeys(EntityKey.None)));
        var noKey = MerpeepsDocument.Load(MerpeepsDocument.Save(saved, draft.Model));
        Assert.Equal(EntityKey.None, noKey.Node("CUSTOMER")!.Entity!.Attributes[0].EffectiveKeys);
        history.Undo();
        Assert.Equal(EntityKey.Primary, customer.Entity.Attributes[0].EffectiveKeys);
        history.Undo();
        Assert.Empty(draft.Model.Edges);
    }

    [Fact]
    public void GroupRenameAndRemovalRepairEndpointsWithoutDroppingEntities()
    {
        var model = MerpeepsDocument.Load("erDiagram\nsubgraph G [Group]\nA\nsubgraph H\nB\nend\nend\nG ||--o{ H : contains\n");
        var history = new EditHistory(model);
        var group = model.Subgraphs.Single(g => g.Id == "G");
        var rename = new SetEntityGroupCommand(group, "Renamed", "New title", null, Direction.LR);
        history.Apply(rename);
        Assert.Equal("Renamed", model.Node("A")!.SubgraphId);
        Assert.Equal("Renamed", model.Subgraphs.Single(g => g.Id == "H").ParentId);
        Assert.Equal("Renamed", model.Edges.Single().From);
        var saved = MerpeepsDocument.Save("erDiagram\n", model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal("Renamed", reopened.Edges.Single().From);
        history.Apply(new DeleteEntityGroupCommand(rename.Result!));
        Assert.Equal(2, model.Nodes.Count);
        Assert.Single(model.Subgraphs);
        Assert.Null(model.Subgraphs[0].ParentId);
        Assert.Empty(model.Edges);
        history.Undo();
        Assert.Single(model.Edges);
        history.Undo();
        Assert.Same(group, model.Subgraphs.Single(g => g.Id == "G"));
    }

    [Fact]
    public void ScopedDirectionsAffectLayoutAndExplicitGeometryIsPreserved()
    {
        const string source = "erDiagram\ndirection TB\nsubgraph G\ndirection LR\nA ||--|| B : links\nend\n";
        var model = MerpeepsDocument.Load(source);
        Assert.True(model.Node("B")!.X > model.Node("A")!.X);
        Assert.Equal(model.Node("A")!.Y, model.Node("B")!.Y);
        model.Node("A")!.X = 1000;
        model.Node("A")!.Y = 321;
        model.Node("A")!.HasExplicitPosition = true;
        var saved = MerpeepsDocument.SavePositions(source, model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(1000, reopened.Node("A")!.X);
        Assert.Equal(321, reopened.Node("A")!.Y);
    }

    [Fact]
    public void BadRelationshipsAndIdentityChangesFailBeforeMutation()
    {
        var model = MerpeepsDocument.Load("erDiagram\nA ||--|| B : links\n");
        var history = new EditHistory(model);
        var before = MerpeepsDocument.Save("erDiagram\n", model);
        Assert.Throws<ArgumentException>(() => history.Apply(new AddEntityRelationshipCommand(new()
        {
            From = "A",
            To = "missing",
            EntityRelationship = new(),
        })));
        Assert.Throws<ArgumentException>(() => history.Apply(new SetEntityRelationshipCommand(model.Edges[0], new(), "bad\"role")));
        Assert.Throws<ArgumentException>(() => history.Apply(new RenameEntityIdentityCommand(model.Node("A")!, "B")));
        Assert.Equal(before, MerpeepsDocument.Save("erDiagram\n", model));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void AttributeEditsPatchOnlyTheirSourceAndKeepStableIds()
    {
        const string source = "erDiagram\r\n%% keep comment\r\nA {\r\n string name\r\n int age\r\n}\r\n";
        var model = MerpeepsDocument.Load(source);
        var node = model.Nodes.Single();
        var member = node.Entity!.Attributes[0];
        var history = new EditHistory(model);
        history.Apply(EntityEditing.EditAttribute(node, new(member.Id, "string?", "displayName")));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(MerpeepsDoc.BodyOf(source).Replace("string name", "string? displayName"), MerpeepsDoc.BodyOf(saved));
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(member.Id, reopened.Nodes.Single().Entity!.Attributes[0].Id);
        history.Apply(EntityEditing.AddAttribute(node, new("new", "string", "email")));
        var added = MerpeepsDocument.Save(saved, model);
        Assert.DoesNotContain("\r\r\n", added);
        Assert.Equal(3, MerpeepsDocument.Load(added).Nodes.Single().Entity!.Attributes.Count);
        Assert.Contains("%% keep comment\r\n", added);
    }

    [Fact]
    public void SelfRelationsAndGroupEndpointsAreVisibleInNativeSvg()
    {
        const string source = """
            erDiagram
            subgraph G [Customer domain]
              A {
                int id PK
                string? email UK "Primary email"
              }
            end
            subgraph Empty
            end
            G ||--o{ A : contains
            A |o..|{ A : parent
            """;
        var model = MerpeepsDocument.Load(source);
        var xml = XDocument.Parse(SvgExporter.ToSvg(model));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(2, xml.Descendants(ns + "g").Count(g => g.Attribute("data-er-subgraph") is not null));
        Assert.Equal(2, xml.Descendants(ns + "g").Count(g => g.Attribute("data-er-relationship") is not null));
        Assert.Single(xml.Descendants(ns + "g"), g => g.Attribute("data-entity") is not null);
        Assert.Contains(xml.Descendants(ns + "text"), t => t.Value == "PK");
        Assert.Contains(xml.Descendants(ns + "text"), t => t.Value == "Primary email");
        Assert.True(xml.Descendants(ns + "circle").Count() >= 2);
    }

    [Fact]
    public void UnsupportedEntityIconsAreNotSilentlyDroppedByNativeSvg()
    {
        var model = MerpeepsDocument.Load("erDiagram\nA\n");
        model.Nodes[0].Icon = "database";
        Assert.Contains(EntityDiagramStyle.Resolve(model.Nodes[0]).Diagnostics, d => d.Contains("icons", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => SvgExporter.ToSvg(model));
        Assert.Equal("database", MerpeepsDocument.Load(MerpeepsDocument.Save("erDiagram\nA\n", model)).Nodes[0].Icon);
    }
}
