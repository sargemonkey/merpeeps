using Merpeeps.Core.Export;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class DiagramCapabilityTests
{
    [Theory]
    [InlineData("C4Unknown\n    Person(a, \"Alice\")\n")]
    [InlineData("ganttUnknown\n    title Schedule\n")]
    [InlineData("futureDiagram\n    title Plan\n")]
    [InlineData("flowchartish TD\n    a --> b\n")]
    [InlineData("graphical LR\n    a --> b\n")]
    [InlineData("stateDiagram-v3\n    a --> b\n")]
    [InlineData("")]
    [InlineData("%% only a comment\n")]
    public void UnsupportedSourceIsNotInventedAsAFlowchart(string source)
    {
        Assert.ThrowsAny<NotSupportedException>(() => MerpeepsDocument.Load(source));
    }

    [Theory]
    [InlineData("flowchartish TD")]
    [InlineData("graphical LR")]
    public void FlowchartHeaderRequiresAnExactKeyword(string body)
    {
        Assert.False(new FlowchartAdapter().CanParse(body));
    }

    [Theory]
    [InlineData("stateDiagram-v3")]
    [InlineData("stateDiagramExtra")]
    public void StateHeaderRequiresAnExactKeyword(string body)
    {
        Assert.False(new StateAdapter().CanParse(body));
    }

    [Theory]
    [InlineData("flowchart LR\n    a --> b\n", "flowchart")]
    [InlineData("graph TD\n    a --> b\n", "graph")]
    [InlineData("flowchart-elk TD\n    a --> b\n", "flowchart-elk")]
    [InlineData("stateDiagram-v2\n    a --> b\n", "stateDiagram-v2")]
    [InlineData("stateDiagram\n    a --> b\n", "stateDiagram")]
    public void SupportedFamiliesStillLoad(string source, string kind)
    {
        var capabilities = MerpeepsDocument.GetCapabilities(source);
        Assert.Equal(kind, capabilities.Kind);
        Assert.True(capabilities.CanReadMetadata);
        Assert.True(capabilities.CanEdit && capabilities.CanLayout && capabilities.CanRenderNatively);
        Assert.Null(capabilities.Diagnostic);
        var model = MerpeepsDocument.Load(source);
        Assert.Equal(kind, model.Kind);
        Assert.Equal(2, model.Nodes.Count);
        Assert.Single(model.Edges);
    }

    [Fact]
    public void UnknownModelCannotBeRenderedAsNativeSvg()
    {
        var model = new DiagramModel { Kind = "futureDiagram" };
        model.Nodes.Add(new DiagramNode { Id = "Alice" });

        Assert.ThrowsAny<NotSupportedException>(() => SvgExporter.ToSvg(model));
        Assert.ThrowsAny<NotSupportedException>(() => DiagramAdapters.ForModel(model));
    }

    [Fact]
    public void RawMetadataOperationsRemainTypeAgnostic()
    {
        const string source = "sequenceDiagram\n    Alice->>Bob: Hello\n";
        var metadata = new Dictionary<string, object?>
        {
            ["spec"] = 1,
            ["future"] = new Dictionary<string, object?> { ["value"] = 0.5 },
        };

        var extended = MerpeepsDoc.Inject(source, metadata);
        Assert.Equal(source, MerpeepsDoc.BodyOf(extended));
        Assert.Equal(MerpeepsDoc.ToJson(metadata), MerpeepsDoc.ToJson(MerpeepsDoc.Parse(extended)));
        Assert.Equal(source, MerpeepsDoc.Serialise(extended, null));
    }

    [Theory]
    [InlineData("railroad")]
    [InlineData("futureDiagram")]
    [InlineData("stateDiagram-v3")]
    [InlineData("flowchartish")]
    [InlineData("graphical")]
    public void UnsupportedCapabilitiesNameTheActualFamily(string kind)
    {
        var source = $"%% comment\n{kind}\n    Alice->>Bob: Hello\n";
        var capabilities = MerpeepsDocument.GetCapabilities(source);

        Assert.Equal(kind, capabilities.Kind);
        Assert.True(capabilities.CanReadMetadata);
        Assert.False(capabilities.CanEdit);
        Assert.False(capabilities.CanLayout);
        Assert.False(capabilities.CanRenderNatively);
        Assert.Contains(kind, capabilities.Diagnostic);
        var error = Assert.Throws<UnsupportedDiagramException>(() => MerpeepsDocument.Load(source));
        Assert.Equal(capabilities, error.Capabilities);
    }

    [Theory]
    [InlineData("flowchart LR;", "flowchart")]
    [InlineData("FLOWCHART LR", "flowchart")]
    [InlineData("graph", "graph")]
    [InlineData("stateDiagram-v2;", "stateDiagram-v2")]
    public void PrefixDirectivesArePreservedRatherThanParsedAsNodes(string header, string kind)
    {
        var source = "---\ntitle: Example\n---\n\n%% prefix\n%%{init:\n" +
                     "{\"theme\": \"forest\"}\n}%%\n" + header + "\n    a --> b\n";

        var model = MerpeepsDocument.Load(source);
        Assert.Equal(kind, model.Kind);
        Assert.Equal(new[] { "a", "b" }, model.Nodes.Select(n => n.Id));
        Assert.Equal(MerpeepsDoc.BodyOf(source),
            MerpeepsDoc.BodyOf(MerpeepsDocument.SavePositions(source, model)));
    }

    [Fact]
    public void CapabilityDetectionDoesNotClaimToValidateSyntax()
    {
        var capabilities = MerpeepsDocument.GetCapabilities("flowchart TD\nnot valid syntax [\n");
        Assert.True(capabilities.CanEdit);
        Assert.Equal("flowchart", capabilities.Kind);
    }

    [Fact]
    public void NativePositionSaveCannotApplyAnInventedModelToUnsupportedSource()
    {
        const string source = "futureDiagram\n    title Plan\n";
        var model = new DiagramModel();
        model.Nodes.Add(new DiagramNode { Id = "invented" });

        Assert.Throws<UnsupportedDiagramException>(() => MerpeepsDocument.SavePositions(source, model));
        Assert.Throws<UnsupportedDiagramException>(() => MerpeepsDocument.Save(source, model));
    }

    [Theory]
    [InlineData("sequenceDiagram", "style")]
    public void UnsupportedBakeDoesNotLoadAnInventedModel(string kind, string diagnostic)
    {
        var source = $"---\nx-merpeeps:\n  spec: 1\n  style:\n    nodes:\n" +
                     $"      Alice: {{icon: 'aws:lambda', fill: '#ff0000'}}\n" +
                     $"  layout:\n    nodes:\n      Alice: {{x: 10.5, y: 20.25}}\n---\n{kind}\n";
        var result = MermaidBake.Bake(source);

        Assert.Equal(kind, result.UnsupportedKind);
        Assert.Contains(diagnostic, result.Diagnostic);
        Assert.Equal($"{kind}\n", result.Output);
        Assert.Equal(new[] { "Alice" }, result.DroppedIcons);
        Assert.Equal(new[] { "Alice" }, result.DroppedPositions);
        Assert.Equal(new[] { "aws" }, IconVocabulary.PacksReferencedByMetadata(MerpeepsDoc.Parse(source)));
    }
}
