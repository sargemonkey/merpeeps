using System.Text.Json;
using System.Xml.Linq;
using Merpeeps.Core;
using Merpeeps.Core.Export;
using Merpeeps.Core.Model;
using Merpeeps.Render;
using Merpeeps.Scene;

var reports = new List<object>();
var families = MermaidDiagramCatalog.Families;
if (families.Count != 33 || families.Sum(f => f.Dialects.Count) != 48) throw new Exception("Catalog coverage changed.");
var context = new GanttEvaluationContext(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
foreach (var family in families)
foreach (var dialect in family.Dialects)
{
    var draft = MermaidDiagramCatalog.CreateEmpty(family.Id, dialect.Id);
    if (!draft.Capabilities.CanEdit || !draft.Capabilities.CanRenderNatively) throw new Exception("Missing typed family capability.");
    var empty = MerpeepsDocument.Load(MerpeepsDocument.Save(draft.Source, draft.Model));
    var source = Fixtures.Source(family.Id, dialect.Directive);
    var model = MerpeepsDocument.Load(source);
    var saved = MerpeepsDocument.Save(source, model);
    var reopened = MerpeepsDocument.Load(saved);
    if (empty.Kind != dialect.Directive || reopened.Kind != dialect.Directive) throw new Exception("Dialect drifted.");
    var svg = XDocument.Parse(SvgExporter.ToSvg(reopened, evaluationContext: context));
    if (svg.Root?.Name.LocalName != "svg" || !svg.Descendants().Any(e => e.Name.LocalName == "text" && !string.IsNullOrWhiteSpace(e.Value)))
        throw new Exception($"Vacuous populated output: {dialect.Id}");
    var second = MerpeepsDocument.Save(saved, reopened);
    if (MerpeepsDoc.BodyOf(second) != MerpeepsDoc.BodyOf(saved)) throw new Exception($"Source body drift: {dialect.Id}");
    reports.Add(new { family = family.Id, dialect = dialect.Id, draft = true, populated = true,
        nativeSvg = true, canGraphLayout = draft.Capabilities.CanLayout });
}
RenderAssets.Verify();
if (RenderAssets.MermaidVersion != "12.0.0" || !MermaidValidationPage.BuildShell().Contains("script"))
    throw new Exception("Packaged runtime/validation resources are missing.");
var scene = new SceneDocument { Scenes = [new() { Id = "slide", Elements = [new() {
    Id = "text", ContentRef = "body", Bounds = new(20, 30, 300, 80), Text = new() { FontSize = 24 } }] }] };
var sceneRoundTrip = SceneCodec.Read(SceneCodec.Write(scene));
if (!sceneRoundTrip.CanEdit || sceneRoundTrip.Document!.Scenes[0].Elements[0].ContentRef != "body")
    throw new Exception("Packed scene contract failed.");
var output = Environment.GetEnvironmentVariable("MERPIE_PACKAGE_REPORT");
if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PACKED NUGET CONSUMERS VERIFIED: {families.Count} families, {reports.Count} empty/populated dialects, Render assets and Scene round trip.");
