using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class GitDiagramTests
{
    [Fact]
    public void BranchCreationSwitchesAndMergeBuildsTwoParentDiagramCommit()
    {
        const string source = "gitGraph LR:\ncommit id:\"A\"\nbranch dev\ncommit id:\"B\"\ncheckout main\ncommit id:\"C\"\nmerge dev id:\"M\" tag:\"release\"\n";
        var model = new GitGraphAdapter().Parse(source);
        var state = model.GitDiagram!.Replay();
        Assert.Equal(4, state.Commits.Count);
        Assert.Equal("dev", state.Commits[1].Branch);
        Assert.Equal(2, state.Commits[^1].Parents.Count);
        Assert.Equal("release", state.Commits[^1].Tags[0]);
        Assert.Empty(model.Nodes);
        var scene = GitDiagramLayout.Create(model);
        Assert.Contains(scene.Figures, f => f.Id.EndsWith("-merge"));
        var source2 = GitGraphAdapter.Write(model.GitDiagram);
        Assert.Equal(source2, new GitGraphAdapter().Write(new GitGraphAdapter().Parse(source2)));
    }
    [Fact]
    public void CherryPickAndReorderValidationHappenBeforeLiveMutation()
    {
        var model = new GitGraphAdapter().Parse("gitGraph\ncommit id:\"A\"\nbranch dev\ncommit id:\"B\"\ncheckout main\ncherry-pick id:\"B\"\n");
        var state = model.GitDiagram!.Replay();
        Assert.NotNull(state.Commits[^1].CherrySource);
        var history = new EditHistory(model);
        var before = model.GitDiagram;
        Assert.Throws<ArgumentException>(() => history.Apply(GitDiagramEditing.Move(before.Operations[^1].Id, 0)));
        Assert.Same(before, model.GitDiagram);
        history.Apply(GitDiagramEditing.SetDirection(GitDiagramDirection.TB));
        Assert.Equal(GitDiagramDirection.TB, model.GitDiagram!.Direction);
        history.Undo(); Assert.Equal(GitDiagramDirection.LR, model.GitDiagram!.Direction);
    }
    [Fact]
    public void DuplicateExplicitCommitIdsAreNotSilentlyOverwritten()
    {
        Assert.Throws<DataDiagramNotEditableException>(() => new GitGraphAdapter().Parse("gitGraph\ncommit id:\"A\"\ncommit id:\"A\"\n"));
    }
    [Fact]
    public void RequirementStyleValidationIsFailBeforeMutation()
    {
        var model = new RequirementDiagramAdapter().Parse("requirementDiagram\nrequirement A {\n id: 1\n}\n");
        var before = model.Requirements;
        var item = before!.Items[0] with { Classes = new[] { "missing" } };
        Assert.Throws<DataDiagramNotEditableException>(() => new EditHistory(model).Apply(RequirementEditing.Update(item)));
        Assert.Same(before, model.Requirements);
    }
    [Fact]
    public void GitThemeOptionsChangeMeasuredLabelsAndValidateBeforeCommit()
    {
        var model = new GitGraphAdapter().Parse("gitGraph\ncommit id:\"A\" tag:\"release\" type:HIGHLIGHT\n");
        var history = new EditHistory(model);
        history.Apply(GitDiagramEditing.SetThemeVariable("commitLabelFontSize", System.Text.Json.Nodes.JsonValue.Create("20px")));
        history.Apply(GitDiagramEditing.SetThemeVariable("tagLabelColor", System.Text.Json.Nodes.JsonValue.Create("#ff0000")));
        history.Apply(GitDiagramEditing.SetThemeVariable("gitInv0", System.Text.Json.Nodes.JsonValue.Create("#00ff00")));
        var scene = GitDiagramLayout.Create(model);
        Assert.Contains(scene.Labels, l => l.Text == "A" && l.Font.Size == 20);
        Assert.Contains(scene.Labels, l => l.Text == "release" && l.Color == new ClassColor(255, 0, 0));
        Assert.Contains(scene.Figures, f => f.Id.EndsWith("-highlight") && f.Fill == new ClassColor(0, 255, 0));
        var before = model.GitDiagram;
        Assert.Throws<ArgumentException>(() => history.Apply(GitDiagramEditing.SetThemeVariable("tagLabelColor", System.Text.Json.Nodes.JsonValue.Create("not-a-color"))));
        Assert.Same(before, model.GitDiagram);
    }
    [Fact]
    public void ExplicitPacketReflowUpdatesFollowingAbsoluteStartsAtomically()
    {
        var model = new PacketDiagramAdapter().Parse("packet\n0-7: \"A\"\n8-15: \"B\"\n");
        var history = new EditHistory(model);
        history.Apply(PacketEditing.UpdateAndReflow(model.Packet!.Fields[0] with { Bits = 16 }));
        Assert.Equal(16, model.Packet!.Fields[1].ExplicitStart);
        history.Undo(); Assert.Equal(8, model.Packet!.Fields[1].ExplicitStart);
    }
}
