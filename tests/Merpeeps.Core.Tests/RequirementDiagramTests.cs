using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class RequirementDiagramTests
{
    [Fact]
    public void TypedRequirementPropertiesAreNotClassOrFlowchartLabels()
    {
        const string source = """
            requirementDiagram
            direction LR
            functionalRequirement Req {
              id: "R-1"
              text: "**Must** work"
              risk: high
              verifymethod: test
            }
            element Service {
              type: service
              docRef: docs/service
            }
            Service - satisfies -> Req
            """;
        var adapter = new RequirementDiagramAdapter();
        var model = adapter.Parse(source);
        var document = model.Requirements!;
        Assert.Empty(model.Nodes);
        Assert.Equal(RequirementKind.FunctionalRequirement, document.Items[0].Kind);
        Assert.Equal("R-1", document.Items[0].DisplayId);
        Assert.Equal(RequirementRisk.High, document.Items[0].Risk);
        Assert.Equal(RequirementVerification.Test, document.Items[0].Verification);
        Assert.Equal("docs/service", document.Items[1].DocumentReference);
        Assert.Equal(RequirementRelationKind.Satisfies, document.Relations[0].Kind);
        var scene = RequirementDiagramLayout.Create(model);
        Assert.Contains(scene.Labels, l => l.Text == "Must" && l.Font.Weight == 700);
        Assert.Contains(scene.Figures, f => f.Id.Contains("-dash-"));
        var source2 = adapter.Write(model);
        Assert.Equal(source2, adapter.Write(adapter.Parse(source2)));
    }
    [Fact]
    public void RenamingElementKeepsReferenceIdentityAndUndo()
    {
        var model = new RequirementDiagramAdapter().Parse("requirementDiagram\nrequirement A {\nid: 1\n}\nelement B {\ntype: service\n}\nA <- verifies - B\n");
        var old = model.Requirements!.Items[1];
        var history = new EditHistory(model);
        history.Apply(RequirementEditing.Update(old with { Name = "New B" }));
        Assert.Equal(old.Id, model.Requirements!.Relations[0].From);
        Assert.Contains("\"New B\" - verifies -> \"A\"", RequirementDiagramAdapter.Write(model.Requirements));
        history.Undo(); Assert.Equal("B", model.Requirements!.Items[1].Name);
    }
}
