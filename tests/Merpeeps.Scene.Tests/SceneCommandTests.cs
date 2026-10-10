using System.Text.Json.Nodes;
using Merpeeps.Scene;
using Xunit;

namespace Merpeeps.Scene.Tests;

public class SceneCommandTests
{
    [Fact]
    public void BoundsCommandUndoRedoRestoresNondefaultGeometry()
    {
        var node = SceneCodecTests.Example().Scenes[0].Elements[0];
        var before = node.Bounds;
        var after = new SceneBounds(125.25, -12.5, 333, 81);
        ISceneCommand command = new SetElementBoundsCommand(node, after);

        command.Do();
        Assert.Equal(after, node.Bounds);
        command.Undo();
        Assert.Equal(before, node.Bounds);
        command.Do();
        Assert.Equal(after, node.Bounds);
        Assert.Equal("title-body", node.ContentRef);
        Assert.Equal("#FFFFFF", node.Text!.Color);
    }

    [Fact]
    public void InvalidBoundsFailBeforeMutation()
    {
        var node = SceneCodecTests.Example().Scenes[0].Elements[0];
        var before = node.Bounds;
        var command = new SetElementBoundsCommand(node, new SceneBounds(1, 2, -1, 100));

        Assert.Throws<SceneFormatException>(command.Do);
        Assert.Equal(before, node.Bounds);
    }

    [Fact]
    public void TextCommandSnapshotsStyleAndUnknownFields()
    {
        var node = SceneCodecTests.Example().Scenes[0].Elements[0];
        var next = new SceneTextStyle
        {
            FontSize = 30.5, Color = null, Align = "right",
            Extra = new JsonObject { ["future"] = true },
        };
        ISceneCommand command = new SetElementTextStyleCommand(node, next);
        next.FontSize = 999;

        command.Do();
        Assert.Equal(30.5, node.Text!.FontSize);
        Assert.Null(node.Text.Color);
        command.Undo();
        Assert.Equal(44, node.Text!.FontSize);
        Assert.Equal("#FFFFFF", node.Text.Color);
        Assert.NotNull(node.Text.Extra["future-style"]);
        command.Do();
        Assert.True(node.Text!.Extra["future"]!.GetValue<bool>());
    }

    [Fact]
    public void UndoRestoresAbsenceOfTextStyle()
    {
        var node = new SceneElement { Id = "a", ContentRef = "b" };
        var command = new SetElementTextStyleCommand(node, new SceneTextStyle());
        command.Do();
        Assert.NotNull(node.Text);
        command.Undo();
        Assert.Null(node.Text);
    }

    [Fact]
    public void InvalidStyleDoesNotChangeTheElement()
    {
        var node = SceneCodecTests.Example().Scenes[0].Elements[0];
        var before = node.Text;
        var command = new SetElementTextStyleCommand(node, new SceneTextStyle { FontSize = double.NaN });
        Assert.Throws<SceneFormatException>(command.Do);
        Assert.Same(before, node.Text);
    }
}
