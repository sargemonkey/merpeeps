using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.Core.Tests;

public class RailroadDiagramTests
{
    [Theory]
    [InlineData("railroad-ebnf-beta", "identifier = letter (letter | digit | \"_\")* ;\nletter = \"a\" | \"b\" ;\ndigit = \"0\" ;")]
    [InlineData("railroad-abnf-beta", "phone = [ \"+\" ] 2*4DIGIT ;\nrange = %x30-39 ;")]
    [InlineData("railroad-peg-beta", "Identifier <- !Keyword Letter Letter* ;\nKeyword <- \"if\" / \"else\" ;\nLetter <- \"a\" / \"b\" ;")]
    [InlineData("railroad-beta", "rule = sequence(terminal(\"a\"), optional(nonterminal(\"b\")), oneOrMore(terminal(\"c\"))) ;")]
    public void AllNotationsKeepTheirExpressionSemantics(string directive, string body)
    {
        var adapter = new RailroadDiagramAdapter(); var model = adapter.Parse(directive + "\n" + body);
        Assert.Empty(model.Nodes);
        Assert.NotEmpty(model.Railroad!.Rules);
        var written = adapter.Write(model);
        Assert.Equal(written, adapter.Write(adapter.Parse(written)));
    }
    [Fact]
    public void ExceptionsAndLookaheadAreNotReplacedByOrdinaryChoices()
    {
        var ebnf = new RailroadDiagramAdapter().Parse("railroad-ebnf-beta\nrule = token - keyword ;\n");
        Assert.Equal(RailroadExpressionKind.Exception, ebnf.Railroad!.Rules[0].Expression.Kind);
        var peg = new RailroadDiagramAdapter().Parse("railroad-peg-beta\nrule <- &token !keyword . ;\n");
        Assert.Equal(RailroadExpressionKind.AndPredicate, peg.Railroad!.Rules[0].Expression.Children[0].Kind);
        Assert.Equal(RailroadExpressionKind.NotPredicate, peg.Railroad.Rules[0].Expression.Children[1].Kind);
        Assert.Equal(RailroadExpressionKind.AnyCharacter, peg.Railroad.Rules[0].Expression.Children[2].Kind);
    }
    [Fact]
    public void PrimitiveSingletonsNormalizeButEmptyConstructorsAreRejected()
    {
        var model = new RailroadDiagramAdapter().Parse("railroad-beta\nrule = sequence(choice(terminal(\"a\"))) ;\n");
        Assert.Equal(RailroadExpressionKind.Terminal, model.Railroad!.Rules[0].Expression.Kind);
        Assert.Throws<ArgumentException>(() => new RailroadDiagramAdapter().Parse("railroad-beta\nrule = choice() ;\n"));
        Assert.Throws<ArgumentException>(() => new RailroadDiagramAdapter().Parse("railroad-abnf-beta\nbad_name = \"a\" ;\n"));
    }
    [Fact]
    public void RealRailroadPathsContainCurvedChoicesAndBackwardRepetition()
    {
        var model = MerpeepsDocument.Load("railroad-ebnf-beta\nrule = (\"a\" | \"b\")+ [\"c\"] ;\n");
        var layout = RailroadDiagramLayout.Create(model);
        Assert.Empty(model.Nodes);
        Assert.Contains(layout.Scene.Figures, f => f.Id.EndsWith("-loop") && f.Path.Any(p => p.Kind == DrawingPathKind.Cubic));
        Assert.Contains(layout.Scene.Figures, f => f.Id.EndsWith("-skip"));
        Assert.Contains(layout.Scene.Figures, f => f.Kind == DrawingFigureKind.Path && f.Path.Any(p => p.Kind == DrawingPathKind.Cubic));
    }
    [Fact]
    public void RuleAndExpressionIdentitiesSurviveSaveReopenAfterRename()
    {
        const string source = "railroad-ebnf-beta\nrule = (\"a\" | \"b\")+ [\"c\"] ;\nreference = rule ;\n";
        var model = MerpeepsDocument.Load(source); var d = model.Railroad!;
        var expressions = RailroadDiagramDocument.Walk(d.Rules[0].Expression).Select(e => e.Id).ToArray();
        var history = new EditHistory(model); history.Apply(RailroadEditing.RenameRule(d.Rules[0].Id, "renamed"));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model)).Railroad!;
        Assert.Equal(expressions, RailroadDiagramDocument.Walk(reopened.Rules[0].Expression).Select(e => e.Id));
        Assert.Equal("renamed", reopened.Rules[1].Expression.Text);
        history.Undo(); Assert.Equal("rule", model.Railroad!.Rules[0].Name);
    }
    [Fact]
    public void BinaryWrappingIsAtomicAndPreservesTheExistingExpressionIdentity()
    {
        const string source = "railroad-ebnf-beta\nrule = \"a\" ;\n";
        var model = MerpeepsDocument.Load(source); var root = model.Railroad!.Rules[0].Expression;
        var history = new EditHistory(model);
        history.Apply(RailroadEditing.WrapWithSibling(root.Id, RailroadExpressionKind.Choice, "choice", new("second", RailroadExpressionKind.Terminal, "b", [])));
        Assert.Equal(root.Id, model.Railroad!.Rules[0].Expression.Children[0].Id);
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model));
        Assert.Equal("choice", reopened.Railroad!.Rules[0].Expression.Id);
        Assert.Equal(root.Id, reopened.Railroad.Rules[0].Expression.Children[0].Id);
        history.Undo(); Assert.Equal(root.Id, model.Railroad!.Rules[0].Expression.Id);
    }
}
