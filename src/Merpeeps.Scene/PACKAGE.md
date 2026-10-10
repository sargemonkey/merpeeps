# Merpeeps.Scene

Renderer-independent visual scenes for Markdown-backed editors. A scene holds
stable object IDs, bounds, styles and references to content owned by the host.
It does not duplicate Markdown text or Mermaid source.

This is a separate package and format from `Merpeeps.Core` diagram metadata.
It has no Avalonia, OpenXML, YAML or Mermaid runtime dependency.

```csharp
using Merpeeps.Scene;

var document = new SceneDocument
{
    Scenes =
    [
        new SceneModel
        {
            Id = "slide1",
            Elements =
            [
                new SceneElement
                {
                    Id = "title1",
                    ContentRef = "title1",
                    Bounds = new SceneBounds(60, 40, 1160, 100),
                    Text = new SceneTextStyle { FontSize = 44, Bold = true },
                },
            ],
        },
    ],
};

var metadata = SceneCodec.Write(document); // JsonObject for YAML/JSON frontmatter
var loaded = SceneCodec.Read(metadata);
if (!loaded.CanEdit)
    foreach (var diagnostic in loaded.Diagnostics)
        Console.Error.WriteLine($"{diagnostic.Path}: {diagnostic.Message}");
```

## Persistence

Embed the envelope under **`x-merpeeps-scenes`**, with its own `spec: 1`.
`scenes` is keyed by scene ID; `elements` contains stable IDs and `contentRef`
bindings to host content. Element IDs are scene-scoped, so the same `title` ID
on two different slides stays valid. Coordinates are logical pixels at 96 DPI. Text color
and scene background may be null to inherit the presentation palette. UI theme
is deliberately absent: dark application chrome must not alter the document.

Hosts own Markdown/YAML extraction and emission, deck order, content binding,
asset resolution and safe file writes. The codec only accepts the extracted
JSON-compatible metadata tree. It validates geometry, colors, supported kinds
and uniqueness, not whether a referenced Markdown block or diagram exists.

## Current scope

Text and nested-diagram elements are supported. The host supplies their content
and rendering. A diagram's own Mermaid body and `x-merpeeps` metadata remain
unchanged and separate; generic scenes are not Mermaid flowcharts.

Unknown kinds, unsupported versions and invalid data return a read-only result
with diagnostics and the original `Raw` tree. `SceneCodec.Preserve` copies that
tree; do not substitute an empty editable document. Malformed JSON or duplicate
JSON properties throw `SceneFormatException` because they cannot be represented
unambiguously as a tree.

Unknown fields at document, scene, element and text-style levels survive an
editable round trip. Unknown bounds fields require read-only preservation
because they may change transform semantics. Unmatched scene/content references
are retained; hosts should diagnose them instead of deleting them.

`SetElementBoundsCommand` and `SetElementTextStyleCommand` implement
`ISceneCommand` (`Do`, `Undo`, `Description`). Adapt them into the host's existing
history; this package does not create a competing undo stack.

MIT.
