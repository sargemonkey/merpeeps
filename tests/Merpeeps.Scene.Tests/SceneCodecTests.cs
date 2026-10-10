using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Scene;
using Xunit;

namespace Merpeeps.Scene.Tests;

public class SceneCodecTests
{
    internal static SceneDocument Example() => new()
    {
        Extra = new JsonObject { ["future-root"] = new JsonObject { ["spec"] = 99, ["enabled"] = true } },
        Scenes =
        [
            new SceneModel
            {
                Id = "s1", Width = 1280, Height = 720, Background = "#1F2020",
                Extra = new JsonObject { ["layout"] = "title-and-diagram" },
                Elements =
                [
                    new SceneElement
                    {
                        Id = "title1", Kind = SceneKinds.Text, ContentRef = "title-body",
                        Bounds = new SceneBounds(60.5, 40.25, 1160, 100),
                        Text = new SceneTextStyle
                        {
                            FontSize = 44, Bold = true, Align = "center", Color = "#FFFFFF",
                            Extra = new JsonObject { ["future-style"] = new JsonArray("one", 2, null) },
                        },
                        Extra = new JsonObject { ["role"] = "Title", ["future"] = "keep me" },
                    },
                    new SceneElement
                    {
                        Id = "diagram1", Kind = SceneKinds.Diagram, ContentRef = "diagram-body",
                        Bounds = new SceneBounds(-10.5, 180.25, 800, 400), DiagramTheme = "presentation",
                    },
                ],
            },
            new SceneModel { Id = "s2", Elements = [] },
        ],
    };

    [Fact]
    public void RoundTripPreservesGeometryContentReferencesAndUnknownData()
    {
        var input = Example();
        var json = SceneCodec.Serialize(input);
        var read = SceneCodec.Read(json);

        Assert.True(read.CanEdit);
        Assert.Empty(read.Diagnostics);
        Assert.Equal(json, SceneCodec.Serialize(read.Document!));
        var scene = read.Document!.Scenes[0];
        Assert.Equal("s1", scene.Id);
        Assert.Equal(60.5, scene.Elements[0].Bounds.X);
        Assert.Equal("title-body", scene.Elements[0].ContentRef);
        Assert.Equal("keep me", scene.Elements[0].Extra["future"]!.GetValue<string>());
        Assert.Equal("#FFFFFF", scene.Elements[0].Text!.Color);
        Assert.Equal("presentation", scene.Elements[1].DiagramTheme);
        Assert.Null(scene.Elements[1].Text);
        Assert.DoesNotContain("Quarterly", json); // The content is not duplicated into layout.
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void SerializationIsCultureIndependent(string culture)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = SceneCodec.Serialize(Example());
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var localized = SceneCodec.Serialize(Example());

            Assert.Equal(invariant, localized);
            Assert.Equal(60.5, SceneCodec.Read(localized).Document!.Scenes[0].Elements[0].Bounds.X);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void InheritedAndExplicitColorsStayDistinct()
    {
        var doc = Example();
        doc.Scenes[0].Elements[0].Text!.Color = null;
        var read = SceneCodec.Read(SceneCodec.Serialize(doc));

        Assert.Null(read.Document!.Scenes[0].Elements[0].Text!.Color);
        Assert.Equal("#1F2020", read.Document.Scenes[0].Background);
        Assert.Null(read.Document.Scenes[1].Background);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("shape")]
    [InlineData("group")]
    [InlineData("connector")]
    [InlineData("future-object")]
    public void UnimplementedObjectTypesArePreservedButNotAdvertisedAsEditable(string kind)
    {
        var raw = SceneCodec.Write(Example());
        var element = raw["scenes"]!["s1"]!["elements"]![0]!;
        element["kind"] = kind;
        element["payload"] = new JsonObject { ["asset"] = "images/example.png", ["precision"] = 0.123456789 };

        var read = SceneCodec.Read(raw);

        Assert.False(read.CanEdit);
        Assert.Null(read.Document);
        Assert.Contains(read.Diagnostics, d => d.Code == "unsupported-kind" && d.Message.Contains(kind));
        Assert.True(JsonNode.DeepEquals(raw, SceneCodec.Preserve(read)));
    }

    [Fact]
    public void FutureEnvelopeVersionIsPreservedWithoutInterpretingIt()
    {
        var raw = new JsonObject
        {
            ["spec"] = 2,
            ["scenes"] = "a different future representation",
            ["future"] = new JsonArray(1, true, "1"),
        };
        var read = SceneCodec.Read(raw);

        Assert.False(read.CanEdit);
        Assert.Equal("unsupported-version", Assert.Single(read.Diagnostics).Code);
        Assert.True(JsonNode.DeepEquals(raw, SceneCodec.Preserve(read)));
    }

    [Fact]
    public void UnmatchedScenesAndElementsAreNotDroppedByTheSharedCodec()
    {
        var doc = Example();
        doc.Scenes.Add(new SceneModel
        {
            Id = "not-in-this-host",
            Elements =
            [
                new SceneElement { Id = "orphan", ContentRef = "deleted-body-block" },
            ],
        });
        var result = SceneCodec.Read(SceneCodec.Serialize(doc));

        Assert.Equal(3, result.Document!.Scenes.Count);
        Assert.Equal("deleted-body-block", result.Document.Scenes[2].Elements[0].ContentRef);
    }

    [Theory]
    [InlineData("width", 0)]
    [InlineData("height", -1)]
    [InlineData("fontSize", 0)]
    public void InvalidDimensionsDoNotAcquireAnEditableModel(string key, double value)
    {
        var raw = SceneCodec.Write(Example());
        var element = raw["scenes"]!["s1"]!["elements"]![0]!;
        if (key == "fontSize") element["text"]![key] = value;
        else element["bounds"]![key] = value;

        var result = SceneCodec.Read(raw);
        Assert.False(result.CanEdit);
        Assert.Contains(result.Diagnostics, d => d.Code == "invalid-number");
        Assert.True(JsonNode.DeepEquals(raw, SceneCodec.Preserve(result)));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12GG34")]
    [InlineData("#fff")]
    [InlineData("# 12345")]
    [InlineData("")]
    public void InvalidColorsHaveActionableDiagnostics(string color)
    {
        var raw = SceneCodec.Write(Example());
        raw["scenes"]!["s1"]!["background"] = color;
        var result = SceneCodec.Read(raw);

        Assert.False(result.CanEdit);
        Assert.Contains(result.Diagnostics, d => d.Code == "invalid-color" && d.Path.EndsWith(".background"));
    }

    [Theory]
    [InlineData("x", "10,5")]
    [InlineData("width", "100")]
    public void QuotedNumbersAreNotSilentlyCoerced(string property, string value)
    {
        var raw = SceneCodec.Write(Example());
        raw["scenes"]!["s1"]!["elements"]![0]!["bounds"]![property] = value;
        Assert.False(SceneCodec.Read(raw).CanEdit);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteCoordinatesCannotBeWritten(double value)
    {
        var doc = Example();
        doc.Scenes[0].Elements[0].Bounds = new SceneBounds(value, 1, 100, 50);
        var error = Assert.Throws<SceneFormatException>(() => SceneCodec.Serialize(doc));
        Assert.Contains(error.Diagnostics, d => d.Code == "invalid-number");
    }

    [Fact]
    public void DuplicateElementIdentityWithinASceneIsRejected()
    {
        var doc = Example();
        doc.Scenes[0].Elements.Add(doc.Scenes[0].Elements[0].Clone());

        Assert.Contains(SceneCodec.Validate(doc), d => d.Code == "duplicate-id");
        Assert.Throws<SceneFormatException>(() => SceneCodec.Write(doc));
    }

    [Fact]
    public void ElementIdentityIsScopedToItsScene()
    {
        var doc = Example();
        doc.Scenes[1].Elements.Add(doc.Scenes[0].Elements[0].Clone());
        doc.Scenes[1].Elements[0].Bounds = new SceneBounds(10, 20, 300, 80);
        var read = SceneCodec.Read(SceneCodec.Serialize(doc));

        Assert.True(read.CanEdit);
        Assert.Equal("title1", read.Document!.Scenes[0].Elements[0].Id);
        Assert.Equal("title1", read.Document.Scenes[1].Elements[0].Id);
        Assert.NotEqual(read.Document.Scenes[0].Elements[0].Bounds, read.Document.Scenes[1].Elements[0].Bounds);
    }

    [Fact]
    public void DuplicateSceneIdentityCannotOverwriteASceneOnWrite()
    {
        var doc = Example();
        doc.Scenes.Add(doc.Scenes[0].Clone());
        Assert.Throws<SceneFormatException>(() => SceneCodec.Serialize(doc));
    }

    [Fact]
    public void DuplicateWireKeysAreRejectedRatherThanSelectingOneValue()
    {
        var error = Assert.Throws<SceneFormatException>(() =>
            SceneCodec.Read("""{"spec":1,"scenes":{"same":{"elements":[]},"same":{"elements":[]}}}"""));
        Assert.Contains(error.Diagnostics, d => d.Code == "duplicate-key");
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[]")]
    public void MalformedEnvelopeIsNotReplacedByAnEmptyScene(string json)
    {
        Assert.Throws<SceneFormatException>(() => SceneCodec.Read(json));
    }

    [Fact]
    public void UnknownBoundsFieldsRequireReadOnlyPreservation()
    {
        var raw = SceneCodec.Write(Example());
        raw["scenes"]!["s1"]!["elements"]![0]!["bounds"]!["rotation"] = 45;
        var result = SceneCodec.Read(raw);

        Assert.False(result.CanEdit);
        Assert.Contains(result.Diagnostics, d => d.Code == "unsupported-bounds");
        Assert.True(JsonNode.DeepEquals(raw, SceneCodec.Preserve(result)));
    }

    [Fact]
    public void ExtensionsCannotSilentlyReplaceTypedFields()
    {
        var doc = Example();
        doc.Scenes[0].Elements[0].Extra["contentRef"] = "wrong-content";
        Assert.Throws<SceneFormatException>(() => SceneCodec.Serialize(doc));
    }

    [Fact]
    public void ReadWriteAndCloneDoNotShareMutableExtensionData()
    {
        var doc = Example();
        var raw = SceneCodec.Write(doc);
        var result = SceneCodec.Read(raw);
        raw["future-root"]!["enabled"] = false;
        result.Document!.Scenes[0].Elements[0].Extra["future"] = "edited";
        var copy = doc.Clone();
        copy.Scenes[0].Elements[0].Text!.Extra["future-style"]![0] = "changed";

        Assert.True(result.Raw["future-root"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("keep me", doc.Scenes[0].Elements[0].Extra["future"]!.GetValue<string>());
        Assert.Equal("one", doc.Scenes[0].Elements[0].Text!.Extra["future-style"]![0]!.GetValue<string>());
        Assert.Equal("keep me", result.Raw["scenes"]!["s1"]!["elements"]![0]!["future"]!.GetValue<string>());
    }

    [Fact]
    public void NativeIntegerJsonNodesAreAccepted()
    {
        var raw = SceneCodec.Write(Example());
        raw["spec"] = 1;
        raw["scenes"]!["s1"]!["width"] = 1920;
        Assert.Equal(1920, SceneCodec.Read(raw).Document!.Scenes[0].Width);
    }

    [Fact]
    public void FiniteValuesCannotCreateInfiniteExtents()
    {
        var doc = Example();
        doc.Scenes[0].Elements[0].Bounds = new SceneBounds(double.MaxValue, 0, double.MaxValue, 50);
        Assert.Throws<SceneFormatException>(() => SceneCodec.Write(doc));
    }
}
