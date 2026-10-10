# Shared visual scenes

Merpeeps.Scene defines a visual surface independently of an editor or export
format. MerDeck uses one scene per slide while retaining deck order, notes and
presentation workflow. Other consumers can use the same scene model without
depending on MerDeck.

## Separate from Mermaid metadata

Existing `.mmd` files and diagram `x-merpeeps.spec: 1` keep their meaning.
The scene envelope is a new **`x-merpeeps-scenes`** frontmatter key with an
independent schema version. It does not make a generic slide a Mermaid diagram.
Markdown hosts extract this envelope and pass it to `SceneCodec`; the package
does not parse slide separators or Markdown content.

```yaml
x-merpeeps-scenes:
  spec: 1
  scenes:
    slide1:
      width: 1280
      height: 720
      background: null
      elements:
        - id: title1
          kind: text
          contentRef: title1
          bounds: {x: 60, y: 40, width: 1160, height: 100}
          text: {fontSize: 44, bold: true, color: null, align: left}
        - id: diagram1
          kind: diagram
          contentRef: diagram1
          bounds: {x: 100, y: 180, width: 1000, height: 460}
          diagramTheme: presentation
```

Here `title1` binds to a host Markdown text block; `diagram1` binds to an inline
Mermaid block or an external `.mmd` reference. Content is authoritative in those
blocks, not duplicated into the scene. A deck can embed every scene in one
Markdown file, but referenced external diagrams and images remain dependencies.

## Version 1 contract

| Field | Meaning |
|---|---|
| `spec` | Exactly `1` for this reader/writer |
| `scenes` | Map keyed by nonempty stable scene IDs; map order is not deck order |
| `width`, `height` | Positive finite dimensions in logical 96-DPI pixels |
| `background` | `#RRGGBB` override or null/inherited |
| `elements` | Ordered array, preserving the host's object order |
| `id` | Nonempty element ID, unique within its scene; identity is `(sceneId, elementId)` |
| `kind` | `text` or `diagram` in this implementation |
| `contentRef` | Stable host content ID within that scene, never the content itself |
| `bounds` | Finite `x`, `y`; positive finite `width`, `height`; negative coordinates are allowed |
| `text` | Optional style: positive `fontSize`, boolean `bold`, nullable `#RRGGBB` `color`, `align` of left/center/right/justify |
| `diagramTheme` | Optional host-defined diagram-theme provenance string, retained without interpreting source ownership |

Default text style is 26 pixels, normal weight, inherited color and left
alignment. The package does not select fonts, measure text, resolve references,
change a document theme or rewrite external files. An explicit color remains an
explicit color even when it happens to equal a default palette value.

Images, shapes, groups, animation and routing are not made editable merely by
storing an unknown object type. They require future modeling/rendering support.

## Forward compatibility and validation

- Extra properties at the envelope, scene, element and text-style levels
  survive round trips through typed objects.
- Unknown bounds/transform fields, object kinds or versions yield diagnostics
  and a read-only result carrying the original tree. Invalid fields do too.
- Malformed JSON and duplicate property names are rejected explicitly rather
  than guessed. Duplicate scene/element identity is invalid.
- `SceneCodec.Preserve` retains a read-only envelope semantically, including
  unknown data. It is not a promise to preserve whitespace or YAML comments.
- Unknown referenced content stays in the model. The host owns binding checks,
  read-only presentation and recoverable orphan handling.
- Serialization is culture-invariant. Non-finite numbers, invalid extents and
  extension fields colliding with modeled fields cannot be written.

## Host integration and migration

Use the shared `SceneElement` as the authoritative backing state for geometry
and styles, not a second parallel map beside editor fields. Keep content and
deck order in the host. Shared edit commands adapt into the host's one history.

Legacy MerDeck `x-merdeck` object maps remain the host's import responsibility.
Opening or exporting an old document must not rewrite it. An explicit Save As
to the shared-scene format can migrate objects and leave the original intact.
Do not retain two authoritative object maps in the new file. Unsupported scene
versions should be preserved read-only, not upgraded blindly.

App theme is a user preference. Presentation theme is document data. Scene
background/text defaults may resolve against the latter, never the former.

The package codec does no file I/O; rollback, conflict detection, external
diagram ownership and staged PPTX output remain host responsibilities.
