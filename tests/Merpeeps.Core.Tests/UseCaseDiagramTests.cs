using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.Core.Tests;

public class UseCaseDiagramTests
{
    [Fact]
    public void ForwardReferencesResolveFinalKindsAndBusinessSemantics()
    {
        const string source = """
            usecase-beta
            User --> Login
            note for Login "Needs #quot;session#quot;"
            actor User("Customer")@{ type: hollow, business: true } <<Employee>>
            systemBoundary app["Application"]@{ type: package }
              Login("Sign in")
              Check("Verify")
            end
            Login ..> : include Check
            """;
        var adapter = new UseCaseDiagramAdapter(); var model = adapter.Parse(source); var d = model.UseCase!;
        var user = d.Nodes.Single(n => n.SourceId == "User");
        Assert.Equal(UseCaseNodeKind.Actor, user.Kind); Assert.True(user.Business); Assert.Equal(UseCaseActorKind.Hollow, user.ActorKind);
        Assert.True(d.Boundaries.Single().Package);
        Assert.Equal(UseCaseRelationshipKind.Include, d.Relationships[1].Kind);
        Assert.Equal("Needs \"session\"", d.Nodes.Single(n => n.Kind == UseCaseNodeKind.Note).Label.Text);
        var written = adapter.Write(model); Assert.Equal(written, adapter.Write(adapter.Parse(written)));
    }
    [Fact]
    public void JsonTablesKeepLexicalPropertyOrderAndLastDuplicateValue()
    {
        var value = UseCaseJson.Parse("""{"2":"first","1":"second","2":"updated","list":[{"item":"Book"}],"empty":{}}""");
        var rows = UseCaseJson.Rows(value);
        Assert.Equal(new[] { "2", "1", "list[0].item", "empty" }, rows.Select(r => r.Path));
        Assert.Equal("updated", rows[0].Value);
    }
    [Fact]
    public void ActorMetadataAndUmlEndpointRulesAreStrict()
    {
        Assert.Throws<FormatException>(() => new UseCaseDiagramAdapter().Parse("usecase-beta\nactor A@{ fillColor: red }\n"));
        Assert.Throws<ArgumentException>(() => new UseCaseDiagramAdapter().Parse("usecase-beta\nactor A\nU\nA ..> : include U\n"));
        Assert.Throws<ArgumentException>(() => new UseCaseDiagramAdapter().Parse("usecase-beta\nactor A@{ type: awesome, business: true }\n"));
    }
    [Fact]
    public void TypedNativeLayoutAndSourceReopenPreserveActorsNotesAndTables()
    {
        const string source = "usecase-beta\nactor A(\"Actor\")\nU(\"Use case\")\njson Data@{ \"2\":\"first\", \"1\":true }\nnote for U \"Keep note\"\nA --> U\nU --> Data\n";
        var model = MerpeepsDocument.Load(source); var d = model.UseCase!;
        var layout = UseCaseDiagramLayout.Create(model);
        Assert.Contains(layout.Scene.Labels, l => l.Text == "first");
        Assert.Contains(layout.Scene.Figures, f => f.Id.EndsWith("-head"));
        var actor = d.Nodes.Single(n => n.Kind == UseCaseNodeKind.Actor);
        new EditHistory(model).Apply(UseCaseEditing.UpdateNode(actor with { SourceId = "Renamed", Label = new("New actor") }));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model));
        Assert.Equal(actor.Id, reopened.UseCase!.Nodes.Single(n => n.SourceId == "Renamed").Id);
        Assert.Contains(reopened.UseCase.Nodes, n => n.Kind == UseCaseNodeKind.Note);
    }
    [Fact]
    public void TypedMetadataDoesNotRequireYamlWhitespaceAfterColon()
    {
        var model = new UseCaseDiagramAdapter().Parse("usecase-beta\nactor User@{type:hollow,business:true}\nsystemBoundary App[\"Application\"]@{type:package}\nLogin(\"Sign in\")\nend\nUser --> Login\n");
        Assert.Equal(UseCaseActorKind.Hollow, model.UseCase!.Nodes.Single(n => n.SourceId == "User").ActorKind);
        Assert.True(model.UseCase.Boundaries.Single().Package);
    }
    [Fact]
    public void IncludeLabelsDoNotOverlapTheTargetUseCaseText()
    {
        var model = MerpeepsDocument.Load("usecase-beta\ndirection LR\nA(\"Sign in\")\nB(\"Verify session\")\nA ..> : include B\n");
        var layout = UseCaseDiagramLayout.Create(model);
        var edge = model.UseCase!.Relationships.Single();
        var labels = layout.Scene.Labels.Where(l => l.Owner.Id == edge.Id).ToArray();
        var top = Math.Min(layout.Nodes[edge.From].Y, layout.Nodes[edge.To].Y);
        Assert.All(labels, label => Assert.True(label.Bounds.Y + label.Bounds.Height <= top - 1, "Include label overlaps a use-case body."));
    }
}
