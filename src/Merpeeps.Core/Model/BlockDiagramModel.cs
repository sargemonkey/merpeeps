using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum BlockItemKind { Shape, Composite, Arrow, Space }
[Flags]
public enum BlockArrowDirections { None = 0, Left = 1, Right = 2, Up = 4, Down = 8 }
public enum BlockLinkEnd { None, Arrow, Circle, Cross }
public enum BlockLinkStroke { Solid, Dotted, Thick, Invisible }
public sealed record BlockItem(string Id, BlockItemKind Kind, IReadOnlyList<BlockItem> Children)
{
    public string? SourceId { get; init; }
    public string? Label { get; init; }
    public bool Markdown { get; init; }
    public NodeShape Shape { get; init; } = NodeShape.Rectangle;
    public int Span { get; init; } = 1;
    /// <summary>Null selects automatic columns for this composite only.</summary>
    public int? Columns { get; init; }
    public BlockArrowDirections Arrows { get; init; }
    public JsonObject Style { get; init; } = new();
    public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();
    public string Display => Label ?? SourceId ?? "";
    public BlockItem DeepCopy() => this with
    {
        Children = Children.Select(n => n.DeepCopy()).ToArray(),
        Style = (JsonObject)Style.DeepClone(), Classes = Classes.ToArray(),
    };
}
public sealed record BlockConnection(string Id, string From, string To)
{
    public string Label { get; init; } = "";
    public BlockLinkEnd FromEnd { get; init; }
    public BlockLinkEnd ToEnd { get; init; } = BlockLinkEnd.Arrow;
    public BlockLinkStroke Stroke { get; init; }
}
public sealed class BlockDiagramDocument
{
    public string Directive { get; set; } = "block";
    public int? Columns { get; set; }
    public List<BlockItem> Items { get; } = new();
    public List<BlockConnection> Connections { get; } = new();
    public Dictionary<string, JsonObject> Classes { get; } = new(StringComparer.Ordinal);
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public string AccessibilityTitle { get; set; } = "";
    public string AccessibilityDescription { get; set; } = "";
    public IEnumerable<BlockItem> AllItems => Descendants(Items);
    public BlockDiagramDocument Clone()
    {
        var copy = new BlockDiagramDocument
        {
            Directive = Directive, Columns = Columns,
            AccessibilityTitle = AccessibilityTitle, AccessibilityDescription = AccessibilityDescription,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Items.AddRange(Items.Select(n => n.DeepCopy())); copy.Connections.AddRange(Connections);
        foreach (var pair in Classes) copy.Classes[pair.Key] = (JsonObject)pair.Value.DeepClone();
        copy.Comments.AddRange(Comments);
        return copy;
    }
    public static IEnumerable<BlockItem> Descendants(IEnumerable<BlockItem> items)
    {
        foreach (var item in items) { yield return item; foreach (var child in Descendants(item.Children)) yield return child; }
    }
}

public static class BlockDiagramValidation
{
    public static IReadOnlyList<DataDiagramDiagnostic> Validate(BlockDiagramDocument document)
    {
        var issues = new List<DataDiagramDiagnostic>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<BlockItem>();
        var pending = new Stack<(BlockItem Item, int? Columns)>(document.Items.Select(item => (item, document.Columns)).Reverse());
        if (document.Directive is not ("block" or "block-beta")) issues.Add(new("block-directive", "", "Unknown Block diagram directive.", true));
        if (document.Columns is <= 0) issues.Add(new("block-columns", "", "Column count must be positive or automatic.", true));
        foreach (var name in document.Classes.Keys)
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^\w+$"))
                issues.Add(new("block-class-name", name, "A style class name must contain letters, digits or underscores.", true));
        while (pending.TryPop(out var entry))
        {
            var item = entry.Item;
            if (string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id))
            {
                issues.Add(new("block-identity", item.Id, "Block identities must be unique; cyclic/shared subtrees are not allowed.", true));
                continue;
            }
            items.Add(item);
            if (item.SourceId is { } name && (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[^\s\(\)\[\]\{\}<>:=\-;""`]+$") || !names.Add(name)))
                issues.Add(new("block-source-id", item.Id, "Block source identifiers must be unique and nonempty.", true));
            if (item.Kind is BlockItemKind.Shape or BlockItemKind.Arrow && item.SourceId is null)
                issues.Add(new("block-source-id", item.Id, "Shapes and arrows need a source identifier.", true));
            if (!Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Shape) || item.Span < 1 || item.Columns is <= 0)
                issues.Add(new("block-properties", item.Id, "Block kind, shape, span or column count is invalid.", true));
            if (entry.Columns is { } columns && item.Span > columns)
                issues.Add(new("block-span", item.Id, "Block span exceeds its parent grid's columns.", true));
            if (item.Kind == BlockItemKind.Composite && item.SourceId is null && item.Span != 1)
                issues.Add(new("block-composite-span", item.Id, "A spanning composite needs a source identifier.", true));
            if (item.Kind != BlockItemKind.Composite && (item.Children.Count != 0 || item.Columns is not null))
                issues.Add(new("block-parent", item.Id, "Only composite blocks may own children or columns.", true));
            if (item.Kind == BlockItemKind.Arrow && (item.Arrows == BlockArrowDirections.None || ((int)item.Arrows & ~15) != 0))
                issues.Add(new("block-arrow", item.Id, "Choose at least one valid block-arrow direction.", true));
            try { _ = ResolveStyle(document, item); }
            catch (DataDiagramNotEditableException error) { issues.AddRange(error.Diagnostics); }
            foreach (var child in item.Children.Reverse()) pending.Push((child, item.Columns));
        }
        var targets = items.Where(n => n.SourceId is not null && n.Kind != BlockItemKind.Space).Select(n => n.Id).ToHashSet();
        foreach (var connection in document.Connections)
        {
            if (string.IsNullOrEmpty(connection.Id) || !ids.Add(connection.Id))
                issues.Add(new("block-link-id", connection.Id, "Connection identities must be unique.", true));
            if (!targets.Contains(connection.From) || !targets.Contains(connection.To))
                issues.Add(new("block-link-endpoint", connection.Id, "Connections must refer to existing named blocks.", true));
            if (!Enum.IsDefined(connection.FromEnd) || !Enum.IsDefined(connection.ToEnd) || !Enum.IsDefined(connection.Stroke))
                issues.Add(new("block-link-style", connection.Id, "Unknown connection decoration or line style.", true));
        }
        return issues;
    }

    public static ClassResolvedStyle ResolveStyle(BlockDiagramDocument document, BlockItem item)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.Classes.TryGetValue("default", out var defaults)) Merge(defaults);
        foreach (var key in item.Classes)
        {
            if (!document.Classes.TryGetValue(key, out var style))
                throw new DataDiagramNotEditableException([new("block-class", item.Id, "Undefined block style class: " + key, true)]);
            Merge(style);
        }
        Merge(item.Style);
        var result = ClassDiagramStyle.ResolveValues(new DiagramNode { Id = item.Id }, values, false);
        if (result.Diagnostics.Count > 0)
            throw new DataDiagramNotEditableException(result.Diagnostics.Select(message => new DataDiagramDiagnostic("block-style", item.Id, message, true)).ToArray());
        return result;
        void Merge(JsonObject style) { foreach (var pair in style) if (pair.Value is not null) values[pair.Key] = ChartConfiguration.Raw(pair.Value); }
    }
}
