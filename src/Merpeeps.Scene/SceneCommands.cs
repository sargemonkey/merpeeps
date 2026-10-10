namespace Merpeeps.Scene;

/// <summary>A reversible scene edit; the host owns the single document history.</summary>
public interface ISceneCommand
{
    string Description { get; }
    void Do();
    void Undo();
}

public sealed class SetElementBoundsCommand(SceneElement element, SceneBounds next) : ISceneCommand
{
    private SceneBounds _previous;
    private bool _applied;
    public string Description => $"Position/resize {element.Id}";

    public void Do()
    {
        var issues = new List<SceneDiagnostic>();
        SceneCodec.ValidateBounds(next, element.Id + ".bounds", issues);
        if (issues.Count > 0) throw new SceneFormatException(issues);
        if (_applied) throw new InvalidOperationException("The scene edit is already applied.");
        _previous = element.Bounds;
        element.Bounds = next;
        _applied = true;
    }

    public void Undo()
    {
        if (!_applied) throw new InvalidOperationException("The scene edit has not been applied.");
        element.Bounds = _previous;
        _applied = false;
    }
}

public sealed class SetElementTextStyleCommand(SceneElement element, SceneTextStyle? next) : ISceneCommand
{
    private readonly SceneTextStyle? _next = next?.Clone();
    private SceneTextStyle? _previous;
    private bool _applied;
    public string Description => $"Style {element.Id}";

    public void Do()
    {
        var issues = new List<SceneDiagnostic>();
        if (_next is not null) SceneCodec.ValidateText(_next, element.Id + ".text", issues);
        if (issues.Count > 0) throw new SceneFormatException(issues);
        if (_applied) throw new InvalidOperationException("The scene edit is already applied.");
        _previous = element.Text?.Clone();
        element.Text = _next?.Clone();
        _applied = true;
    }

    public void Undo()
    {
        if (!_applied) throw new InvalidOperationException("The scene edit has not been applied.");
        element.Text = _previous?.Clone();
        _applied = false;
    }
}
