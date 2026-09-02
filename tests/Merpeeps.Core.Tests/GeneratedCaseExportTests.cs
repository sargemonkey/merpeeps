using System.Text.Json;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Emits editor-generated documents for the oracle to validate against real
/// mermaid.js. Our own parser accepting the output proves nothing; the stock
/// renderer must accept it too.
/// </summary>
public class GeneratedCaseExportTests
{
    private const string Source = """
        flowchart TD
            a[Alpha] --> b{Decision}
            b -->|yes| c([Ship])
            b -.->|no| d[(Store)]
            classDef keepme fill:#eee
        """;

    /// <summary>
    /// A second diagram family, to prove the adapter pattern produces output
    /// stock mermaid accepts — not just output our own parser accepts.
    /// The trailing note is unmodelled syntax that must survive verbatim.
    /// </summary>
    private const string StateSource = """
        stateDiagram-v2
            [*] --> Still
            Still --> [*]
            Still --> Moving : go
            Moving --> Still : halt
            Moving --> Crash : hit wall
            Crash --> [*]
            note right of Crash : unrecoverable
        """;

    [Fact]
    public void ExportCasesForTheOracle()
    {
        var cases = new Dictionary<string, string>();

        // 1. untouched save
        var m0 = MerpeepsDocument.Load(Source);
        cases["untouched"] = MerpeepsDocument.Save(Source, m0);

        // 2. node added and connected
        var m1 = MerpeepsDocument.Load(Source);
        var h1 = new EditHistory(m1);
        h1.Apply(new AddNodeCommand(new DiagramNode
            { Id = "e", Label = "Audit Log", Shape = NodeShape.Cylinder }));
        h1.Apply(new AddEdgeCommand(new DiagramEdge
            { From = "c", To = "e", Label = "records" }));
        cases["added-node-and-edge"] = MerpeepsDocument.Save(Source, m1);

        // 3. node deleted (removes incident edges)
        var m2 = MerpeepsDocument.Load(Source);
        var h2 = new EditHistory(m2);
        h2.Apply(new DeleteNodesCommand(new[] { m2.Node("b")! }));
        cases["deleted-node"] = MerpeepsDocument.Save(Source, m2);

        // 4. renamed with characters that must be quoted
        var m3 = MerpeepsDocument.Load(Source);
        var h3 = new EditHistory(m3);
        h3.Apply(new RenameNodeCommand(m3.Node("a")!, "Alpha [v2] (beta)"));
        cases["awkward-label"] = MerpeepsDocument.Save(Source, m3);

        // 5. every shape, to prove each delimiter pair is emitted correctly
        var m4 = MerpeepsDocument.Load("flowchart LR\n    s0[Start]");
        var h4 = new EditHistory(m4);
        var i = 0;
        foreach (var shape in Enum.GetValues<NodeShape>())
        {
            i++;
            var node = new DiagramNode
                { Id = $"s{i}", Label = shape.ToString(), Shape = shape };
            h4.Apply(new AddNodeCommand(node));
            h4.Apply(new AddEdgeCommand(new DiagramEdge { From = "s0", To = node.Id }));
        }
        cases["all-shapes"] = MerpeepsDocument.Save("flowchart LR\n    s0[Start]", m4);

        // 6. every edge kind
        var m5 = MerpeepsDocument.Load("flowchart LR\n    k0[Hub]");
        var h5 = new EditHistory(m5);
        var j = 0;
        foreach (var kind in Enum.GetValues<EdgeKind>())
        {
            j++;
            h5.Apply(new AddNodeCommand(new DiagramNode
                { Id = $"k{j}", Label = kind.ToString() }));
            h5.Apply(new AddEdgeCommand(new DiagramEdge
                { From = "k0", To = $"k{j}", Kind = kind, Label = kind.ToString() }));
        }
        cases["all-edge-kinds"] = MerpeepsDocument.Save("flowchart LR\n    k0[Hub]", m5);

        // 7. heavy styling and icons — the metadata that mermaid cannot express.
        // Stock mermaid must render this as a perfectly ordinary diagram.
        var m6 = MerpeepsDocument.Load(Source);
        var h6 = new EditHistory(m6);
        h6.Apply(new StyleNodesCommand(m6.Nodes, new StyleNodesCommand.Style(
            Fill: "#FFE6CC", Stroke: "#D79B00", TextColor: "#663300",
            StrokeWidth: 3, Dashed: true, Icon: "gateway")));
        cases["styled-and-iconed"] = MerpeepsDocument.SavePositions(Source, m6);

        // 8. state diagram, saved without structural change
        var s0 = MerpeepsDocument.Load(StateSource);
        cases["state-untouched"] = MerpeepsDocument.Save(StateSource, s0);

        // 9. state added and wired into the existing machine
        var s1 = MerpeepsDocument.Load(StateSource);
        var hs1 = new EditHistory(s1);
        hs1.Apply(new AddNodeCommand(new DiagramNode { Id = "Repairing" }));
        hs1.Apply(new AddEdgeCommand(new DiagramEdge
            { From = "Crash", To = "Repairing", Label = "tow" }));
        hs1.Apply(new AddEdgeCommand(new DiagramEdge
            { From = "Repairing", To = "Still", Label = "fixed" }));
        cases["state-added"] = MerpeepsDocument.Save(StateSource, s1);

        // 10. renaming a state sets a description rather than changing its id,
        //     because transitions reference the id.
        var s2 = MerpeepsDocument.Load(StateSource);
        var hs2 = new EditHistory(s2);
        hs2.Apply(new RenameNodeCommand(s2.Node("Moving")!, "In motion"));
        cases["state-renamed"] = MerpeepsDocument.Save(StateSource, s2);

        // 11. positions and styling on a state diagram must leave the body
        //     byte-identical — SavePositions throws otherwise.
        var s3 = MerpeepsDocument.Load(StateSource);
        var hs3 = new EditHistory(s3);
        hs3.Apply(new StyleNodesCommand(
            s3.Nodes.Where(n => !Parsing.StateAdapter.IsPseudo(n.Id)).ToList(),
            new StyleNodesCommand.Style(
                Fill: "#E1D5E7", Stroke: "#9673A6", TextColor: null,
                StrokeWidth: 2, Dashed: false, Icon: "process")));
        cases["state-styled"] = MerpeepsDocument.SavePositions(StateSource, s3);

        // 12. ids that collide with mermaid's own keywords. `end` is the obvious
        //     id for a terminal state and is a parse error in every form, so the
        //     writer escapes it. Rendering the result in real mermaid is the only
        //     thing that proves the escape actually works — a unit test can only
        //     check we wrote what we intended to write.
        var m7 = MerpeepsDocument.Load(Source);
        var h7 = new EditHistory(m7);
        OperationApplier.Apply(m7, h7,
        [
            new DiagramOperation { Op = "addNode", Id = "end", Label = "Finished" },
            new DiagramOperation { Op = "addNode", Id = "class", Label = "Classify" },
            new DiagramOperation { Op = "addNode", Id = "subgraph", Label = "Group" },
            new DiagramOperation { Op = "connect", From = "a", To = "end" },
        ]);
        cases["reserved-word-ids"] = MerpeepsDocument.Save(Source, m7);

        var dir = FindOracleDir();
        File.WriteAllText(Path.Combine(dir, "generated-cases.json"),
            JsonSerializer.Serialize(cases,
                new JsonSerializerOptions { WriteIndented = true }));

        Assert.Equal(12, cases.Count);
        Assert.All(cases.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    private static string FindOracleDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "oracle")))
            dir = dir.Parent;
        if (dir is null) throw new DirectoryNotFoundException("oracle directory not found");
        return Path.Combine(dir.FullName, "oracle");
    }
}
