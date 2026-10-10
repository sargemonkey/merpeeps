namespace Merpeeps.Core;

/// <summary>
/// Diagram-family capabilities, not a validation of the diagram's syntax.
/// Metadata operations remain available even without a native diagram adapter.
/// </summary>
public sealed record DiagramCapabilities(
    string Kind,
    bool CanReadMetadata,
    bool CanEdit,
    bool CanLayout,
    bool CanRenderNatively,
    string? Diagnostic);

/// <summary>A native operation was requested for a diagram without an adapter.</summary>
public sealed class UnsupportedDiagramException(DiagramCapabilities capabilities)
    : NotSupportedException(capabilities.Diagnostic)
{
    public DiagramCapabilities Capabilities { get; } = capabilities;
}
