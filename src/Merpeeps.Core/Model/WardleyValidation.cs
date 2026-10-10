using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public static class WardleyValidation
{
    public static void Check(WardleyDiagramDocument d)
    {
        if (!double.IsFinite(d.Width) || !double.IsFinite(d.Height) || d.Width < 200 || d.Height < 200)
            throw new ArgumentException("Wardley canvas dimensions must be finite and at least 200 pixels.");
        var ids = new HashSet<string>(); var scopedNames = new HashSet<(string?, string)>();
        if (d.Title.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Wardley titles must be a single line.");
        foreach (var c in d.Components)
        {
            Id(c.Id);
            if (string.IsNullOrWhiteSpace(c.Name) || c.Name.IndexOfAny(['\r', '\n', '[', ']']) >= 0 || !scopedNames.Add((c.PipelineParentId, c.Name)))
                throw new ArgumentException("Wardley names must be nonempty and unique within their scope.");
            Unit(c.Evolution);
            if (c.PipelineParentId is { } parent)
            {
                if (c.Visibility is not null || c.Anchor || c.Strategy != WardleyStrategy.None || c.Inertia ||
                    !d.Components.Any(n => n.Id == parent && n.PipelineParentId is null))
                    throw new ArgumentException("Pipeline variants inherit visibility from a root component and cannot have root-only decorators.");
            }
            else if (c.Visibility is { } visibility) Unit(visibility);
            else throw new ArgumentException("Root Wardley components require visibility.");
            if (!Enum.IsDefined(c.Strategy) || !double.IsFinite(c.LabelOffset.X) || !double.IsFinite(c.LabelOffset.Y))
                throw new ArgumentException("Invalid Wardley strategy or label offset.");
            if (c.EvolveTo is { } evolution) Unit(evolution);
            if (c.Trend is { } trend) { Unit(trend.X); Unit(trend.Y); }
        }
        foreach (var link in d.Links)
        {
            Id(link.Id);
            if (!d.Components.Any(c => c.Id == link.From) || !d.Components.Any(c => c.Id == link.To) || !Enum.IsDefined(link.Kind) ||
                link.Label.IndexOfAny(['\r', '\n']) >= 0)
                throw new ArgumentException("Invalid Wardley link endpoints, kind or label.");
        }
        foreach (var a in d.Annotations)
        {
            Id(a.Id); Unit(a.Visibility); Unit(a.Evolution);
            if (!Enum.IsDefined(a.Kind) || a.Kind == WardleyAnnotationKind.Numbered && a.Number is not >= 0 ||
                a.Text.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Invalid Wardley annotation.");
        }
        var last = 0d;
        if (d.Stages.Count == 1) throw new ArgumentException("Evolution stages need at least two entries.");
        for (var index = 0; index < d.Stages.Count; index++)
        {
            var stage = d.Stages[index];
            Unit(stage.End);
            if (stage.End <= last || string.IsNullOrWhiteSpace(stage.Label) || stage.Label.IndexOfAny(['\r', '\n', '@']) >= 0)
                throw new ArgumentException("Wardley stages need increasing boundaries and nonempty labels.");
            last = stage.End;
            if (stage.ExplicitEnd && stage.SecondaryLabel is not null)
                throw new ArgumentException("The Wardley grammar does not combine dual stage labels with explicit boundaries.");
            if (!stage.ExplicitEnd && Math.Abs(stage.End - (index + 1d) / d.Stages.Count) > 1e-9)
                throw new ArgumentException("Automatic stage boundaries must be evenly spaced; choose explicit boundaries to change widths.");
        }
        if (d.Stages.Count > 0 && Math.Abs(last - 1) > 1e-9) throw new ArgumentException("The final Wardley stage must end at 1.");
        if (d.AnnotationBox is { } box) { Unit(box.X); Unit(box.Y); }
        if (d.GlobalConfiguration["look"]?.ToString() == "handDrawn")
            throw new DataDiagramNotEditableException([new("wardley-look", "", "Wardley does not support hand-drawn rendering.", true)]);
        void Id(string id) { if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw new ArgumentException("Wardley identities must be unique."); }
        static void Unit(double value) { if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentException("Wardley normalized coordinates must lie between 0 and 1."); }
    }
}
