using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ZenDiagramTests
{
    [Fact]
    public void NestedCallsAndReturnsRetainTheirOwnZenGrammar()
    {
        const string source = "zenuml\n@Actor Client\n@Database DB\nClient->Service.method() {\n DB.query() {\n return result\n }\n Service->DB: Async\n}\n";
        var adapter = new ZenDiagramAdapter(); var model = adapter.Parse(source); var d = model.Zen!;
        Assert.Empty(model.Nodes); Assert.Null(model.Sequence);
        var call = d.Statements[0]; Assert.Equal(ZenStatementKind.Call, call.Kind);
        Assert.Equal(call.To, call.Children[0].From); Assert.Equal(ZenStatementKind.Return, call.Children[0].Children[0].Kind);
        Assert.Equal(adapter.Write(model), adapter.Write(adapter.Parse(adapter.Write(model))));
    }
    [Fact]
    public void ConditionalBranchesAndAssignmentRepliesRemainTyped()
    {
        const string source = "zenuml\nSomeType value = A.load()\nif(condition) {\n A->B: yes\n} else {\n A->B: no\n}\n";
        var adapter = new ZenDiagramAdapter(); var d = adapter.Parse(source).Zen!;
        Assert.Equal("SomeType", d.Statements[0].AssignmentType); Assert.Equal("value", d.Statements[0].Assignment);
        Assert.Equal("else", d.Statements[1].Branches[0].Keyword);
    }
    [Fact]
    public void NestedNativeCallsAndSourceIdentitySurviveEditing()
    {
        const string source = "zenuml\nA.load() {\n B.read()\n return result\n}\n";
        var model = MerpeepsDocument.Load(source); var original = model.Zen!.Statements[0];
        var layout = ZenDiagramLayout.Create(model);
        Assert.Contains(layout.Scene.Figures, f => f.Id.EndsWith("-activation"));
        new EditHistory(model).Apply(ZenEditing.UpdateStatement(original with { Text = "fetch" }));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model)).Zen!;
        Assert.Equal(original.Id, reopened.Statements[0].Id);
        Assert.Equal(original.Children[0].Id, reopened.Statements[0].Children[0].Id);
    }
}
