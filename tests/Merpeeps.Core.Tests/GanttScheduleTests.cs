using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Editing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class GanttScheduleTests
{
    private static readonly GanttEvaluationContext Clock = new(new DateTimeOffset(2026, 2, 10, 12, 34, 56, TimeSpan.Zero), TimeZoneInfo.Utc);
    [Theory]
    [InlineData(1, "2024-01-08", "2024-01-06")]
    [InlineData(2, "2024-01-09", "2024-01-09")]
    [InlineData(3, "2024-01-10", "2024-01-10")]
    public void ExclusionsKeepDependencyEndSeparateFromRenderedEnd(double days, string end, string renderEnd)
    {
        var document = Document("2024-01-05", days, GanttDurationUnit.Day);
        document.Excludes.Add("weekends");
        var task = GanttScheduleResolver.Resolve(document, Clock).Tasks.Single();
        Assert.Equal(Date(end), task.End);
        Assert.Equal(Date(renderEnd), task.RenderEnd);
    }
    [Theory]
    [InlineData("2024-01-31", 1.5, GanttDurationUnit.Month, "2024-02-29")]
    [InlineData("2024-02-29", 1.5, GanttDurationUnit.Year, "2025-02-28")]
    [InlineData("2024-01-01", 1.5, GanttDurationUnit.Day, "2024-01-03")]
    [InlineData("2024-01-01", .5, GanttDurationUnit.Week, "2024-01-05")]
    public void CalendarDurationsUseThePinnedRuntimeRules(string start, double amount, GanttDurationUnit unit, string end)
    {
        Assert.Equal(Date(end), GanttScheduleResolver.Resolve(Document(start, amount, unit), Clock).Tasks.Single().End);
    }
    [Fact]
    public void TimeOnlyInputUsesTheExplicitClockAndEpochUnitsRemainSourceSpecific()
    {
        Assert.Equal(new DateTimeOffset(2026, 2, 10, 17, 49, 0, TimeSpan.Zero), GanttDateCodec.Parse("17:49", "HH:mm", Clock, true));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1), GanttDateCodec.Parse("1000", "X", Clock, true));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2000), GanttDateCodec.Parse("2000", "X", Clock));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2), GanttDateCodec.Parse("2000", "x", Clock));
        Assert.Equal(Date("1900-01-01"), GanttDateCodec.Parse("1900", "YYYY", Clock, true));
    }
    [Fact]
    public void IncludesOverrideExclusionsAndManualIsoEndBypassesExpansion()
    {
        var document = Document("2024-01-05", 1, GanttDurationUnit.Day);
        document.Excludes.Add("weekends"); document.Includes.Add("2024-01-06");
        Assert.Equal(Date("2024-01-06"), GanttScheduleResolver.Resolve(document, Clock).Tasks.Single().End);
        document.Tasks[0] = document.Tasks[0] with { Start = new GanttStartSpec.Date("2024-01-06"), End = new GanttEndSpec.Date("2024-01-07") };
        Assert.Equal(Date("2024-01-07"), GanttScheduleResolver.Resolve(document, Clock).Tasks.Single().End);
    }
    [Fact]
    public void MissingFirstStartReferencesAndCyclesNeverFallBackToToday()
    {
        var document = Document("2024-01-01", 1, GanttDurationUnit.Day);
        document.Tasks[0] = document.Tasks[0] with { Start = new GanttStartSpec.Previous() };
        Assert.Throws<DataDiagramNotEditableException>(() => GanttScheduleResolver.Resolve(document, Clock));
        document.Tasks[0] = document.Tasks[0] with { Start = new GanttStartSpec.After(["missing"]) };
        Assert.Throws<DataDiagramNotEditableException>(() => GanttScheduleResolver.Resolve(document, Clock));
        document.Tasks[0] = document.Tasks[0] with { Start = new GanttStartSpec.After(["a"]) };
        Assert.Throws<DataDiagramNotEditableException>(() => GanttScheduleResolver.Resolve(document, Clock));
    }

    [Fact]
    public void ParserKeepsTemporalSpecificationsAndResolvesNamedReferences()
    {
        const string source = "gantt\ndateFormat YYYY-MM-DD\nexcludes weekends\nsection Build\nFirst :a,2024-01-05,1d\nNext :b,after a,until c\nFinal :milestone,c,2024-01-12,0d\n";
        var adapter = new GanttDiagramAdapter(); var model = adapter.Parse(source);
        var document = model.Gantt!;
        Assert.Empty(model.Nodes);
        var after = Assert.IsType<GanttStartSpec.After>(document.Tasks[1].Start);
        Assert.Equal(document.Tasks[0].Id, after.TaskIds.Single());
        Assert.Equal(document.Tasks[2].Id, Assert.IsType<GanttEndSpec.Until>(document.Tasks[1].End).TaskIds.Single());
        var schedule = GanttScheduleResolver.Resolve(document, Clock);
        Assert.Equal(Date("2024-01-08"), schedule.Tasks[1].Start);
        Assert.Equal(Date("2024-01-12"), schedule.Tasks[1].End);
        var text = adapter.Write(model);
        Assert.Equal(text, adapter.Write(adapter.Parse(text)));
    }

    [Fact]
    public void InvalidDurationTokenDoesNotBecomeZeroDuration()
    {
        var document = new GanttDiagramAdapter().Parse("gantt\nTask :2024-01-01,3dX\n").Gantt!;
        Assert.Throws<FormatException>(() => GanttScheduleResolver.Resolve(document, Clock));
    }

    [Fact]
    public void TemporalLayoutKeepsVerticalMarkersOutOfTaskRowsAndUsesRenderEnd()
    {
        var model = new GanttDiagramAdapter().Parse("gantt\nexcludes weekends\nFirst :a,2024-01-05,1d\nMarker :vert,v,2024-01-08,0d\nNext :b,after a,1d\n");
        var layout = GanttDiagramLayout.Create(model, Clock);
        Assert.Equal(2, layout.Rows.Count);
        var first = model.Gantt!.Tasks[0];
        Assert.Equal(layout.Coordinates.X(Date("2024-01-06")) - layout.Coordinates.X(Date("2024-01-05")), layout.TaskBounds[first.Id].Width, 6);
        Assert.Equal(Date("2024-01-08"), layout.Schedule.Tasks[0].End);
    }

    [Fact]
    public void InvalidTemporalEditsAreAtomicAndValidEditsUndo()
    {
        var model = new GanttDiagramAdapter().Parse("gantt\nTask :a,2024-01-01,2d\n");
        var before = model.Gantt!;
        var history = new EditHistory(model);
        Assert.Throws<FormatException>(() => history.Apply(GanttDiagramEditing.UpdateTask(before.Tasks[0] with { End = new GanttEndSpec.Date("3dX") }, Clock)));
        Assert.Same(before, model.Gantt);
        history.Apply(GanttDiagramEditing.UpdateTask(before.Tasks[0] with { End = new GanttEndSpec.Duration(3, GanttDurationUnit.Day) }, Clock));
        Assert.Equal(Date("2024-01-04"), GanttScheduleResolver.Resolve(model.Gantt!, Clock).Tasks.Single().End);
        history.Undo(); Assert.Same(before, model.Gantt);
    }

    [Fact]
    public void SourceIdentityAndRootCompactModeRoundtripWithoutAHiddenClock()
    {
        const string source = "---\ndisplayMode: compact\n---\ngantt\nTask :a,2024-01-01,2d\n";
        var model = MerpeepsDocument.Load(source); var id = model.Gantt!.Tasks[0].Id;
        new EditHistory(model).Apply(GanttDiagramEditing.SetCompact(false, Clock));
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.False(reopened.Gantt!.Compact);
        Assert.Equal(id, reopened.Gantt.Tasks[0].Id);
        Assert.DoesNotContain("ReferenceInstant", saved);
        Assert.DoesNotContain("TimeZone", saved);
        Assert.Throws<ArgumentException>(() => Merpeeps.Core.Export.SvgExporter.ToSvg(model));
        Assert.Contains("<svg", Merpeeps.Core.Export.SvgExporter.ToSvg(model, evaluationContext: Clock));
    }

    [Fact]
    public void InvalidMarkerAndThemeColorsCannotCommit()
    {
        var model = new GanttDiagramAdapter().Parse("gantt\nTask :a,2024-01-01,2d\n"); var before = model.Gantt;
        Assert.Throws<DataDiagramNotEditableException>(() => new EditHistory(model).Apply(GanttDiagramEditing.SetTodayMarker("stroke:invalid", Clock)));
        Assert.Same(before, model.Gantt);
        var copy = model.Gantt!.Clone(); copy.ThemeVariables["taskBkgColor"] = "not-a-color";
        Assert.Throws<DataDiagramNotEditableException>(() => GanttScheduleResolver.Resolve(copy, Clock));
    }

    [Fact]
    public void AddingIntoAnEarlierSectionDoesNotSplitItsSourceIdentity()
    {
        const string source = "gantt\nsection First\nA :a,2024-01-01,1d\nsection Second\nB :b,2024-01-03,1d\n";
        var model = MerpeepsDocument.Load(source);
        var section = model.Gantt!.Sections[0].Id;
        var task = new GanttTask("c", "C", new GanttStartSpec.Date("2024-01-05"), new GanttEndSpec.Duration(1, GanttDurationUnit.Day))
        { SourceId = "c", SectionId = section };
        new EditHistory(model).Apply(GanttDiagramEditing.AddTask(task, Clock));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model)).Gantt!;
        Assert.Equal(model.Gantt.Sections.Select(s => s.Id), reopened.Sections.Select(s => s.Id));
        Assert.Equal(section, reopened.Tasks.Single(t => t.SourceId == "c").SectionId);
        Assert.Equal(new[] { "a", "c", "b" }, model.Gantt.Tasks.Select(t => t.SourceId));
    }

    [Fact]
    public void SectionChangesRelocateTasksAndExplicitSplitMovesAreRefused()
    {
        const string source = "gantt\nsection First\nA :a,2024-01-01,1d\nC :c,2024-01-02,1d\nsection Second\nB :b,2024-01-03,1d\n";
        var model = MerpeepsDocument.Load(source);
        var before = model.Gantt!; var history = new EditHistory(model);
        Assert.Throws<DataDiagramNotEditableException>(() => history.Apply(GanttDiagramEditing.MoveTask(before.Tasks[0].Id, 2, Clock)));
        Assert.Same(before, model.Gantt);
        history.Apply(GanttDiagramEditing.UpdateTask(before.Tasks[2] with { SectionId = before.Sections[0].Id }, Clock));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model)).Gantt!;
        Assert.Equal(2, reopened.Sections.Count);
        Assert.All(reopened.Tasks, t => Assert.Equal(before.Sections[0].Id, t.SectionId));
        history.Undo(); Assert.Same(before, model.Gantt);
    }
    private static GanttDiagramDocument Document(string start, double amount, GanttDurationUnit unit)
    {
        var document = new GanttDiagramDocument();
        document.Tasks.Add(new("a", "Task", new GanttStartSpec.Date(start), new GanttEndSpec.Duration(amount, unit)));
        return document;
    }
    private static DateTimeOffset Date(string value) => DateTimeOffset.Parse(value + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
}
