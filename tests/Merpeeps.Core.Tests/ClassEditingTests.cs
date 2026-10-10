using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Model;
using System.Xml.Linq;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ClassEditingTests
{
    [Theory]
    [InlineData("classDiagram")]
    [InlineData("classDiagram-v2")]
    public void CreateAuthorSaveReopenRetainsClassesNotFlowcharts(string dialect)
    {
        var draft = MermaidDiagramCatalog.CreateEmpty("class", dialect);
        var history = new EditHistory(draft.Model);
        var customer = ClassEditing.CreateClass("Customer", "Customer account");
        history.Apply(new AddClassElementCommand(customer));
        history.Apply(new AddClassElementCommand(ClassEditing.CreateClass("Order")));
        history.Apply(ClassEditing.AddMember(customer, new("name", ClassMemberKind.Attribute, "String name", ClassVisibility.Public)));
        history.Apply(ClassEditing.AddMember(customer, new("save", ClassMemberKind.Operation, "save() bool", ClassVisibility.Public, isStatic: true)));
        history.Apply(new AddClassRelationshipCommand(new()
        {
            From = "Customer", To = "Order", Label = "places",
            ClassRelationship = new(ClassRelationEnd.None, ClassRelationEnd.Arrow, false, "1", "*"),
        }));
        var saved = MerpeepsDocument.Save(draft.Source, draft.Model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(dialect, reopened.Kind);
        Assert.Equal(2, reopened.Nodes.Count);
        Assert.Equal(new[] { "name", "save" }, reopened.Node("Customer")!.Class!.Members.Select(m => m.Id));
        Assert.Equal("*", reopened.Edges.Single().ClassRelationship!.ToMultiplicity);
        history.Undo();
        Assert.Empty(draft.Model.Edges);
        history.Redo();
        Assert.Single(draft.Model.Edges);
        Assert.Contains("class Customer[\"Customer account\"]", saved);
        Assert.DoesNotContain("flowchart", saved);
    }

    [Fact]
    public void EditingAndReorderingMembersKeepStableIdentities()
    {
        const string source = "classDiagram\nclass A {\n +int first\n +int second\n}\n";
        var model = MerpeepsDocument.Load(source);
        var node = model.Node("A")!;
        var first = node.Class!.Members[0];
        var second = node.Class.Members[1];
        var history = new EditHistory(model);
        history.Apply(ClassEditing.EditMember(node, new(first.Id, ClassMemberKind.Attribute, "String renamed")));
        history.Apply(ClassEditing.MoveMember(node, first.Id, 1));
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved).Node("A")!.Class!;
        Assert.Equal(new[] { second.Id, first.Id }, reopened.Members.Select(m => m.Id));
        Assert.Equal("String renamed", reopened.Members[1].Signature);
        history.Undo();
        history.Undo();
        Assert.Equal(first, node.Class!.Members[0]);
    }

    [Fact]
    public void NoteIdentityAndGeometrySurviveEditAndReorder()
    {
        const string source = "classDiagram\nclass A\nnote for A \"First\"\nnote \"Second\"\n";
        var model = MerpeepsDocument.Load(source);
        var note = model.Nodes.First(n => n.Class?.Kind == ClassNodeKind.Note);
        var id = note.Id;
        note.X = 123;
        note.HasExplicitPosition = true;
        new RenameNodeCommand(note, "Updated\nbody").Do(model);
        model.Nodes.Remove(note);
        model.Nodes.Add(note);
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved).Node(id);
        Assert.NotNull(reopened);
        Assert.Equal("Updated\nbody", reopened.Label);
        Assert.Equal(123, reopened.X);
    }

    [Fact]
    public void InterfaceAndItsRelationshipAreAtomicAndIndependent()
    {
        var draft = MermaidDiagramCatalog.CreateEmpty("class");
        var history = new EditHistory(draft.Model);
        history.Apply(new AddClassElementCommand(ClassEditing.CreateClass("A")));
        var first = new AddClassInterfaceCommand("A", "iface-1", "port");
        var second = new AddClassInterfaceCommand("A", "iface-2", "port");
        history.Apply(first);
        history.Apply(second);
        Assert.Equal(3, draft.Model.Nodes.Count);
        Assert.Throws<ArgumentException>(() => history.Apply(new AddClassInterfaceCommand("missing", "bad", "port")));
        Assert.Equal(3, draft.Model.Nodes.Count);
        var source = MerpeepsDocument.Save(draft.Source, draft.Model);
        var reopened = MerpeepsDocument.Load(source);
        Assert.NotNull(reopened.Node("iface-1"));
        Assert.NotNull(reopened.Node("iface-2"));
        history.Apply(new DeleteClassRelationshipCommand(first.Edge));
        Assert.Null(draft.Model.Node(first.Node.Id));
        history.Undo();
        Assert.Same(first.Node, draft.Model.Node(first.Node.Id));
        history.Apply(new DeleteNodesCommand([draft.Model.Node("A")!]));
        Assert.Empty(draft.Model.Nodes);
        Assert.Empty(draft.Model.Edges);
        history.Undo();
        Assert.Equal(3, draft.Model.Nodes.Count);
    }

    [Fact]
    public void IdentityRenameUpdatesReferencesAndUndoAtomically()
    {
        var model = MerpeepsDocument.Load("classDiagram\nclass A\nA --> B\nnote for A \"Note\"\n");
        var original = model.Node("A")!;
        var command = new RenameClassIdentityCommand(original, "Account");
        var history = new EditHistory(model);
        history.Apply(command);
        Assert.Same(command.Result, model.Node("Account"));
        Assert.Equal("Account", model.Edges[0].From);
        Assert.Equal("Account", model.Nodes.Single(n => n.Class?.Kind == ClassNodeKind.Note).Class!.NoteFor);
        history.Undo();
        Assert.Same(original, model.Node("A"));
        Assert.Equal("A", model.Edges[0].From);
    }

    [Fact]
    public void ClassSvgHasCompartmentsSpecialMarkersAndPlainNotes()
    {
        const string source = """
            classDiagram
            class A {
              <<interface>>
              +String name$
              +save() bool*
            }
            A "1" <|.. "many" B : implements
            B --() port
            note for A "First\nSecond"
            """;
        var model = MerpeepsDocument.Load(source);
        var svg = XDocument.Parse(SvgExporter.ToSvg(model));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Contains(svg.Descendants(ns + "text"), t => t.Value == "+String name" && t.Attribute("text-decoration")?.Value == "underline");
        Assert.Contains(svg.Descendants(ns + "text"), t => t.Value == "+save() bool" && t.Attribute("font-style")?.Value == "italic");
        Assert.Contains(svg.Descendants(ns + "text"), t => t.Value == "many");
        Assert.Contains(svg.Descendants(ns + "polygon"), p => p.Attribute("fill")?.Value != "none");
        Assert.Single(svg.Descendants(ns + "circle"));
        Assert.Contains(svg.Descendants(ns + "text"), t => t.Value == "First");
        Assert.Contains(svg.Descendants(ns + "text"), t => t.Value == "Second");
    }

    [Fact]
    public void SourceStylesResolveOnceWithExplicitOverrides()
    {
        var model = MerpeepsDocument.Load("classDiagram\nclass A:::c\nclassDef c fill:navy,color:white,font-size:12pt,stroke-width:2px\n");
        var node = model.Node("A")!;
        node.Fill = "#112233";
        var style = ClassDiagramStyle.Resolve(node);
        Assert.Equal("112233", style.Fill!.Value.RgbHex);
        Assert.Equal("FFFFFF", style.TextColor!.Value.RgbHex);
        Assert.Equal(16, style.FontSize);
        Assert.Equal(2, style.StrokeWidth);
        Assert.Empty(style.Diagnostics);
        Assert.Equal("000080", ClassDiagramStyle.Resolve(node, false).Fill!.Value.RgbHex);
    }

    [Fact]
    public void NamespaceIdentityAndDeletionPreserveClassesAndUndo()
    {
        const string source = "classDiagram\nnamespace Org {\n namespace Old {\n class A\n }\n}\n";
        var model = MerpeepsDocument.Load(source);
        var history = new EditHistory(model);
        var group = model.Subgraphs.Single(g => g.Id == "Org.Old");
        var rename = new SetClassNamespaceDefinitionCommand(group, "Org.New", "Service layer", "Org");
        history.Apply(rename);
        Assert.Equal("Org.New", model.Node("A")!.SubgraphId);
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal("Service layer", MerpeepsDocument.Load(saved).Subgraphs.Single(g => g.Id == "Org.New").Title);
        history.Apply(new DeleteClassNamespaceCommand(rename.Result!, "Org"));
        Assert.Equal("Org", model.Node("A")!.SubgraphId);
        Assert.DoesNotContain(model.Subgraphs, g => g.Id == "Org.New");
        history.Undo();
        Assert.Equal("Org.New", model.Node("A")!.SubgraphId);
        history.Undo();
        Assert.Same(group, model.Subgraphs.Single(g => g.Id == "Org.Old"));
        Assert.Equal("Org.Old", model.Node("A")!.SubgraphId);
    }
}
