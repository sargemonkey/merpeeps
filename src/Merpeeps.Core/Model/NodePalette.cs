namespace Merpeeps.Core.Model;

/// <summary>
/// One entry in the shape palette: a ready-made node rather than a bare shape.
///
/// <para>Picking "Database" should give you a cylinder with a database icon and
/// a sensible colour, not a rectangle you then have to configure three times.
/// That is the difference between a stencil and a shape menu.</para>
/// </summary>
/// <param name="Name">Label shown in the palette.</param>
/// <param name="Label">Default label for the created node.</param>
/// <param name="Shape">Geometry, which is the part mermaid itself understands.</param>
/// <param name="Icon">Merpeeps icon, or null.</param>
/// <param name="Fill">Merpeeps fill, or null to leave unstyled.</param>
/// <param name="Stroke">Merpeeps outline, or null to leave unstyled.</param>
/// <param name="Hint">One line explaining when to use it.</param>
public sealed record PaletteItem(
    string Name,
    string Label,
    NodeShape Shape,
    string? Icon = null,
    string? Fill = null,
    string? Stroke = null,
    string Hint = "")
{
    public override string ToString() => Name;
}

/// <summary>A named group of palette entries — a stencil.</summary>
public sealed record PaletteCollection(string Name, IReadOnlyList<PaletteItem> Items)
{
    public override string ToString() => Name;
}

/// <summary>
/// The stencils offered in the editor.
///
/// <para>Only <see cref="PaletteItem.Shape"/> ends up in the diagram body; the
/// icon and colours go into the Merpeeps block. So a C4 "Person" is a stadium
/// node to stock mermaid and a styled person to anything that reads Merpeeps —
/// which is the whole point of the layer.</para>
/// </summary>
public static class NodePalette
{
    /// <summary>
    /// The four collections that ship with the editor, loaded lazily so a
    /// malformed built-in surfaces on first use rather than in a static
    /// constructor, where the exception would be wrapped in a
    /// TypeInitializationException and lose its message.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<PaletteCollection>> BuiltIn =
        new(LoadBuiltIn);

    /// <summary>Every stencil, in the order the dropdown offers them.</summary>
    public static IReadOnlyList<PaletteCollection> All => BuiltIn.Value;

    public static PaletteCollection Flowchart => Named("Flowchart");
    public static PaletteCollection C4 => Named("C4 model");
    public static PaletteCollection Architecture => Named("Architecture");
    public static PaletteCollection State => Named("State machine");

    /// <summary>
    /// The built-in collections are ordinary packs, embedded rather than
    /// installed.
    ///
    /// <para>They go through exactly the same loader a third-party pack does.
    /// That is deliberate: a pack format only used by other people is a format
    /// nobody notices is broken. If the loader cannot read these four, it cannot
    /// read anyone else's either, and the editor fails loudly on startup instead
    /// of quietly offering an empty palette.</para>
    /// </summary>
    private static IReadOnlyList<PaletteCollection> LoadBuiltIn()
    {
        var assembly = typeof(NodePalette).Assembly;

        // Load order is the dropdown order, so it is fixed here rather than left
        // to whatever order the manifest happens to enumerate in.
        string[] ids = ["flowchart", "c4", "architecture", "state"];
        var collections = new List<PaletteCollection>(ids.Length);

        foreach (var id in ids)
        {
            var resource = $"Merpeeps.Core.Packs.{id}.merpeeps-pack.yml";
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"Built-in pack '{resource}' is missing from the assembly. " +
                    "Available: " + string.Join(", ", assembly.GetManifestResourceNames()));

            using var reader = new StreamReader(stream);
            var pack = StencilPackLoader.Parse(reader.ReadToEnd(), out var problem)
                ?? throw new InvalidOperationException(
                    $"Built-in pack '{id}' failed to load: {problem?.Message}");

            collections.Add(StencilPackLoader.ToCollection(pack));
        }

        return collections;
    }

    private static PaletteCollection Named(string name) =>
        All.FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"No built-in collection named '{name}'");
}