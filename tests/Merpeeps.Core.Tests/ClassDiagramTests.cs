using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ClassDiagramTests
{
    private readonly ClassDiagramAdapter _adapter = new();

    [Theory]
    [InlineData("classDiagram")]
    [InlineData("classDiagram-v2")]
    public void EmptyClassDiagramHasNoInventedFlowchartContent(string directive)
    {
        var model = _adapter.Parse(directive + "\n");
        Assert.Equal(directive, model.Kind);
        Assert.NotNull(model.ClassOptions);
        Assert.Empty(model.Nodes);
        Assert.Empty(model.Edges);
        Assert.Equal(directive + "\n", _adapter.Write(model));
    }

    [Fact]
    public void MembersRemainTypedAndHaveIndependentUmlModifiers()
    {
        const string source = """
            classDiagram
            class Account~T~["Customer account"] {
              <<service>>
              +String owner
              -List~T~ entries$
              #calculate(int count) Money*
              ~save()$ bool
            }
            Account : +deposit(Money amount) bool
            """;
        var model = _adapter.Parse(source);
        var node = Assert.Single(model.Nodes);
        Assert.Equal("Account", node.Id);
        Assert.Equal("Customer account", node.Label);
        var definition = Assert.IsType<ClassNodeDefinition>(node.Class);
        Assert.Equal("T", definition.GenericType);
        Assert.Equal(new[] { "service" }, definition.Annotations);
        Assert.Equal(5, definition.Members.Count);
        Assert.Equal(ClassMemberKind.Attribute, definition.Members[0].Kind);
        Assert.True(definition.Members[1].IsStatic);
        Assert.True(definition.Members[2].IsAbstract);
        Assert.Equal(ClassVisibility.Protected, definition.Members[2].Visibility);
        Assert.Equal(ClassVisibility.Package, definition.Members[3].Visibility);
        Assert.True(definition.Members[3].IsStatic);
        var content = ClassDiagramText.Content(node, model.ClassOptions!);
        Assert.Equal("Customer account<T>", content.Header.Last().Text);
        Assert.Equal(2, content.Attributes.Count);
        Assert.Equal(3, content.Operations.Count);
        Assert.True(content.Attributes[1].Underline);
        Assert.True(content.Operations[0].Italic);
        Assert.Equal("-List<T> entries", content.Attributes[1].Text);

        var reopened = _adapter.Parse(_adapter.Write(model)).Nodes.Single();
        Assert.Equal(definition.Members.Select(m => m.Declaration), reopened.Class!.Members.Select(m => m.Declaration));
    }

    [Theory]
    [InlineData("<|--", ClassRelationEnd.Triangle, ClassRelationEnd.None, false)]
    [InlineData("*--", ClassRelationEnd.Composition, ClassRelationEnd.None, false)]
    [InlineData("o--", ClassRelationEnd.Aggregation, ClassRelationEnd.None, false)]
    [InlineData("-->", ClassRelationEnd.None, ClassRelationEnd.Arrow, false)]
    [InlineData("--", ClassRelationEnd.None, ClassRelationEnd.None, false)]
    [InlineData("..>", ClassRelationEnd.None, ClassRelationEnd.Arrow, true)]
    [InlineData("..|>", ClassRelationEnd.None, ClassRelationEnd.Triangle, true)]
    [InlineData("..", ClassRelationEnd.None, ClassRelationEnd.None, true)]
    [InlineData("<|--|>", ClassRelationEnd.Triangle, ClassRelationEnd.Triangle, false)]
    [InlineData("<..", ClassRelationEnd.Arrow, ClassRelationEnd.None, true)]
    [InlineData("--*", ClassRelationEnd.None, ClassRelationEnd.Composition, false)]
    [InlineData("--o", ClassRelationEnd.None, ClassRelationEnd.Aggregation, false)]
    public void RelationshipEndsLineAndMultiplicitiesAreIndependent(
        string syntax, ClassRelationEnd from, ClassRelationEnd to, bool dashed)
    {
        var source = $"classDiagram\n A \"0..1\" {syntax} \"many\" B : owns\n";
        var model = _adapter.Parse(source);
        var edge = Assert.Single(model.Edges);
        Assert.Equal("A", edge.From);
        Assert.Equal("B", edge.To);
        Assert.Equal("owns", edge.Label);
        Assert.Equal(new ClassRelationship(from, to, dashed, "0..1", "many"), edge.ClassRelationship);
        Assert.Equal(edge.ClassRelationship, _adapter.Parse(_adapter.Write(model)).Edges.Single().ClassRelationship);
    }

    [Fact]
    public void SameNamedLollipopsRemainSeparateOccurrences()
    {
        var model = _adapter.Parse("classDiagram\n A --() bar\n B --() bar\n foo ()-- A\n");
        var interfaces = model.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Lollipop).ToArray();
        Assert.Equal(3, interfaces.Length);
        Assert.Equal(3, interfaces.Select(n => n.Id).Distinct().Count());
        Assert.Equal(2, interfaces.Count(n => n.Label == "bar"));
        var reopened = _adapter.Parse(_adapter.Write(model));
        Assert.Equal(interfaces.Select(n => n.Id), reopened.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Lollipop).Select(n => n.Id));
    }

    [Fact]
    public void NamespaceLabelsNestingAndImplicitParentsSurviveCompactMode()
    {
        const string source = """
            ---
            config:
              class:
                hierarchicalNamespaces: false
                hideEmptyMembersBox: true
            ---
            classDiagram
            namespace Company.Backend["Services"] {
              class API
              namespace Data {
                class Store
              }
            }
            """;
        var model = _adapter.Parse(source);
        Assert.False(model.ClassOptions!.HierarchicalNamespaces);
        Assert.True(model.ClassOptions.HideEmptyMembersBox);
        Assert.False(model.Subgraphs.Single(g => g.Id == "Company").IsExplicit);
        Assert.Equal("Services", model.Subgraphs.Single(g => g.Id == "Company.Backend").Title);
        Assert.Equal("Company.Backend.Data", model.Node("Store")!.SubgraphId);
        var reopened = _adapter.Parse(MerpeepsDoc.ReplaceBody(source, _adapter.Write(model)));
        Assert.False(reopened.Subgraphs.Single(g => g.Id == "Company").IsExplicit);
        Assert.Equal(model.Subgraphs.Select(g => (g.Id, g.ParentId, g.IsExplicit, g.Title)),
            reopened.Subgraphs.Select(g => (g.Id, g.ParentId, g.IsExplicit, g.Title)));
    }

    [Fact]
    public void NotesAreTypedAndRetainNewlinesAndTheirTarget()
    {
        const string source = """
            classDiagram
            note "General note"
            note for A "First\nSecond<br/>Third"
            class A
            """;
        var model = _adapter.Parse(source);
        var notes = model.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Note).ToArray();
        Assert.Equal(2, notes.Length);
        Assert.Null(notes[0].Class!.NoteFor);
        Assert.Equal("A", notes[1].Class!.NoteFor);
        Assert.Equal("First\nSecond\nThird", notes[1].Label);
        var reopened = _adapter.Parse(_adapter.Write(model));
        Assert.Equal(notes.Select(n => (n.Id, n.Label, n.Class!.NoteFor)),
            reopened.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Note).Select(n => (n.Id, n.Label, n.Class!.NoteFor)));
    }

    [Fact]
    public void QuotedNamesAndLiteralBackslashesAreNotLost()
    {
        var model = _adapter.Parse("classDiagram\n class `Animal Class!`[\"Title\"]\n");
        var node = Assert.Single(model.Nodes);
        node.Label = "C:\\new & \"quoted\"";
        var reopened = _adapter.Parse(_adapter.Write(model));
        Assert.Equal("Animal Class!", reopened.Nodes.Single().Id);
        Assert.Equal(node.Label, reopened.Nodes.Single().Label);
    }

    [Fact]
    public void OneMemberEditPreservesUnrelatedSourceAndComments()
    {
        const string source = "classDiagram\r\n%% kept\r\nclass A {\r\n  +String name\r\n  %% member comment\r\n  +save() bool\r\n}\r\n";
        var model = _adapter.Parse(source);
        var definition = model.Node("A")!.Class!;
        definition.Members[0] = new(definition.Members[0].Id, ClassMemberKind.Attribute, "String title", ClassVisibility.Public);
        var saved = _adapter.Apply(source, model);
        Assert.Equal(source.Replace("+String name", "+String title"), saved);
    }

    [Fact]
    public void UnchangedClassSourceIsNotRegenerated()
    {
        const string source = "classDiagram\n%% comment\nclass A { int x; foo() }\nA <|-- B\n";
        Assert.Equal(source, _adapter.Apply(source, _adapter.Parse(source)));
    }

    [Fact]
    public void StylesAndInertInteractionsAreNotDiscarded()
    {
        const string source = """
            classDiagram
            class A:::important
            classDef default fill:#ffffff,stroke:#222222
            classDef important color:#112233
            style A fill:#abcdef
            click A href "https://example.invalid" "Reference"
            """;
        var model = _adapter.Parse(source);
        var node = model.Node("A")!;
        Assert.Equal("#abcdef", node.Class!.ResolvedStyle["fill"]);
        Assert.Equal("#112233", node.Class.ResolvedStyle["color"]);
        Assert.Equal("#222222", node.Class.ResolvedStyle["stroke"]);
        var reopened = _adapter.Parse(_adapter.Write(model));
        Assert.Equal(node.Class.Interaction, reopened.Node("A")!.Class!.Interaction);
        Assert.Equal(node.Class.ResolvedStyle, reopened.Node("A")!.Class!.ResolvedStyle);
    }

    [Fact]
    public void OptionsUpdateFrontmatterWithoutDroppingForeignConfiguration()
    {
        const string source = "---\ntitle: Preserved\nconfig:\n  theme: dark\n  future: value\n---\nclassDiagram\nclass A\n";
        var model = _adapter.Parse(source);
        model.ClassOptions!.HideEmptyMembersBox = true;
        model.ClassOptions.HierarchicalNamespaces = false;
        var saved = _adapter.Apply(source, model);
        Assert.Equal(MerpeepsDoc.BodyOf(source), MerpeepsDoc.BodyOf(saved));
        Assert.Equal("dark", MerpeepsDoc.ThemeOf(saved));
        Assert.Contains("future: value", saved);
        var reopened = _adapter.Parse(saved);
        Assert.True(reopened.ClassOptions!.HideEmptyMembersBox);
        Assert.False(reopened.ClassOptions.HierarchicalNamespaces);
    }

    [Fact]
    public void UnknownStructureIsAnErrorNotAFlattenedNode() =>
        Assert.Throws<FormatException>(() => _adapter.Parse("classDiagram\n futureStructure { x }\n"));
}
