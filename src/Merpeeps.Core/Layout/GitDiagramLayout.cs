using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public static class GitDiagramLayout
{
    public static DrawingScene Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var document = model.GitDiagram ?? throw new ArgumentException("GitGraph content is missing.");
        var state = document.Replay();
        var b = new DataDiagramLayouts.Builder(measure, document.ThemeVariables, document.GlobalConfiguration, themeOverride ?? model.Theme);
        var parallel = document.Configuration["parallelCommits"]?.GetValue<bool>() ?? false;
        var showBranches = document.Configuration["showBranches"]?.GetValue<bool>() ?? true;
        var showLabels = document.Configuration["showCommitLabel"]?.GetValue<bool>() ?? true;
        var rotateLabels = document.Configuration["rotateCommitLabel"]?.GetValue<bool>() ?? true;
        var branchMap = state.Branches.Select((branch, index) => (branch.Name, index)).ToDictionary(p => p.Name, p => p.index);
        var levels = new Dictionary<string, int>();
        foreach (var commit in state.Commits)
            levels[commit.Id] = parallel ? commit.Parents.Select(p => levels[p] + 1).DefaultIfEmpty(0).Max() : commit.Ordinal;
        var last = Math.Max(1, levels.Values.DefaultIfEmpty(0).Max());
        DiagramPoint Point(int order, int branch) => document.Direction switch
        {
            GitDiagramDirection.TB => new(90 + branch * 110, 60 + order * 95),
            GitDiagramDirection.BT => new(90 + branch * 110, 60 + (last - order) * 95),
            _ => new(90 + order * 110, 50 + branch * 100),
        };
        ClassColor Color(int branch)
        {
            var key = "git" + branch % 8;
            return document.ThemeVariables[key] is { } value && SequenceColors.TryParse(ChartConfiguration.Raw(value), out var color) ? color : b.Scale(branch);
        }
        ClassColor Paint(string key, ClassColor fallback) => document.ThemeVariables[key] is { } value && SequenceColors.TryParse(ChartConfiguration.Raw(value), out var color) ? color : fallback;
        double FontSize(string key) => document.ThemeVariables[key] is { } value ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) : 12;
        if (showBranches)
            foreach (var branch in state.Branches)
            {
                var index = branchMap[branch.Name]; var a = Point(0, index); var z = Point(last, index);
                b.Line("branch-" + branch.Name, "GitBranch", branch.Name, [a, z], Color(index), 2);
                var labelX = document.Direction == GitDiagramDirection.LR ? 10 : a.X - 25;
                var labelY = document.Direction == GitDiagramDirection.LR ? a.Y - 8 : 10;
                var labelSize = b.MeasureWrapped(branch.Name, 14, 120, true);
                b.Box("branch-label-bg-" + branch.Name, "GitBranch", branch.Name, new(labelX - 4, labelY - 3, labelSize.Width + 8, labelSize.Height + 6), Color(index), null);
                b.Text("branch-label-" + branch.Name, "GitBranch", branch.Name, branch.Name, 14,
                    labelX, labelY, 120, true, Paint("gitBranchLabel" + index % 8, b.Background));
                b.Hit("GitBranch", branch.Name, new(a.X - 30, a.Y - 20, Math.Abs(z.X - a.X) + 60, Math.Abs(z.Y - a.Y) + 40));
            }
        var points = state.Commits.ToDictionary(c => c.Id, c => Point(levels[c.Id], branchMap[c.Branch]));
        foreach (var commit in state.Commits)
            foreach (var parent in commit.Parents)
            {
                var a = points[parent]; var z = points[commit.Id];
                var middle = document.Direction == GitDiagramDirection.LR ? new DiagramPoint((a.X + z.X) / 2, a.Y) : new(a.X, (a.Y + z.Y) / 2);
                b.Line(commit.Id + "-parent-" + parent, "GitCommit", commit.Id, [a, middle, z], Color(branchMap[commit.Branch]), 2);
            }
        foreach (var commit in state.Commits)
        {
            var at = points[commit.Id]; var color = Color(branchMap[commit.Branch]);
            if (commit.Kind == GitDiagramCommitKind.Highlight)
                b.Box(commit.Id + "-highlight", "GitCommit", commit.Id, new(at.X - 12, at.Y - 12, 24, 24), Paint("gitInv" + branchMap[commit.Branch] % 8, b.Tint(color)), color, 2);
            else b.Circle(commit.Id, "GitCommit", commit.Id, at, 9, color, b.Ink);
            if (commit.Kind == GitDiagramCommitKind.Reverse)
            {
                b.Line(commit.Id + "-x1", "GitCommit", commit.Id, [new(at.X - 5, at.Y - 5), new(at.X + 5, at.Y + 5)], b.Background, 2);
                b.Line(commit.Id + "-x2", "GitCommit", commit.Id, [new(at.X - 5, at.Y + 5), new(at.X + 5, at.Y - 5)], b.Background, 2);
            }
            if (commit.Parents.Count > 1 && commit.CherrySource is null) b.Circle(commit.Id + "-merge", "GitCommit", commit.Id, at, 4, b.Background, null);
            if (commit.CherrySource is not null)
            {
                b.Circle(commit.Id + "-cherry-a", "GitCommit", commit.Id, new(at.X - 3, at.Y + 1), 4, b.Background, color);
                b.Circle(commit.Id + "-cherry-b", "GitCommit", commit.Id, new(at.X + 4, at.Y + 1), 4, b.Background, color);
            }
            if (showLabels)
            {
                var font = FontSize("commitLabelFontSize");
                var size = b.MeasureWrapped(commit.DisplayId, font, double.MaxValue);
                var bounds = new DiagramRect(at.X - 20, at.Y + 18, size.Width, size.Height);
                var angle = rotateLabels ? -Math.PI / 4 : 0;
                var center = new DiagramPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                var corners = new[] { new DiagramPoint(bounds.X - 2, bounds.Y - 2), new(bounds.X + bounds.Width + 2, bounds.Y - 2), new(bounds.X + bounds.Width + 2, bounds.Y + bounds.Height + 2), new(bounds.X - 2, bounds.Y + bounds.Height + 2) }
                    .Select(p => new DiagramPoint(center.X + (p.X - center.X) * Math.Cos(angle) - (p.Y - center.Y) * Math.Sin(angle), center.Y + (p.X - center.X) * Math.Sin(angle) + (p.Y - center.Y) * Math.Cos(angle))).ToArray();
                b.Polygon(commit.Id + "-label-bg", "GitCommit", commit.Id, corners, Paint("commitLabelBackground", b.Background), Paint("commitLabelBackground", b.Background));
                b.RotatedText(commit.Id + "-label", "GitCommit", commit.Id, commit.DisplayId, font, bounds.X, bounds.Y, rotateLabels ? -45 : 0, Paint("commitLabelColor", b.Ink));
            }
            var tags = commit.Tags.Count > 0 ? commit.Tags : commit.CherrySource is { } source ? new[] { "cherry-pick:" + state.Commits.Single(c => c.Id == source).DisplayId } : Array.Empty<string>();
            for (var i = 0; i < tags.Count; i++)
            {
                var size = b.MeasureWrapped(tags[i], FontSize("tagLabelFontSize"), 140);
                b.Box(commit.Id + "-tag-" + i, "GitCommit", commit.Id, new(at.X - 15, at.Y - 34 - i * (size.Height + 8), size.Width + 12, size.Height + 6), Paint("tagLabelBackground", b.Tint(color)), Paint("tagLabelBorder", color));
                b.Text(commit.Id + "-tagtext-" + i, "GitCommit", commit.Id, tags[i], FontSize("tagLabelFontSize"), at.X - 9, at.Y - 31 - i * (size.Height + 8), 140, color: Paint("tagLabelColor", b.Ink));
            }
            b.Hit("GitCommit", commit.Id, new(at.X - 14, at.Y - 14, 28, 28));
        }
        return b.Finish();
    }
}
