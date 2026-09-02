using Merpeeps.Core;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The NativeAOT build suppresses YamlDotNet's IL3050 reflection warning on the
/// grounds that only a closed set of simple types ever reaches the serialiser.
/// A suppression is only as good as the invariant behind it, so this asserts
/// the invariant directly.
/// </summary>
public class AotSafetyTests
{
    private enum Colour { Red }

    private sealed record Point(int X, int Y);

    private static readonly HashSet<Type> Allowed =
    [
        typeof(string), typeof(bool),
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
    ];

    private static void AssertSafe(object? v, string path = "$")
    {
        switch (v)
        {
            case null:
                return;
            case Dictionary<string, object?> d:
                foreach (var (k, item) in d) AssertSafe(item, $"{path}.{k}");
                return;
            case List<object?> l:
                for (var i = 0; i < l.Count; i++) AssertSafe(l[i], $"{path}[{i}]");
                return;
            default:
                Assert.True(Allowed.Contains(v.GetType()),
                    $"{path} is {v.GetType().Name}, which the YAML serialiser can " +
                    "only describe by reflection — unsafe under NativeAOT");
                return;
        }
    }

    [Fact]
    public void NormaliseProducesOnlyAotSafeTypes()
    {
        var hostile = new Dictionary<string, object?>
        {
            ["str"] = "text",
            ["int"] = 42,
            ["long"] = 42L,
            ["double"] = 4.2,
            ["bool"] = true,
            ["null"] = null,
            ["enum"] = Colour.Red,
            ["date"] = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ["guid"] = Guid.Empty,
            ["record"] = new Point(1, 2),
            ["uri"] = new Uri("https://example.com"),
            ["nested"] = new Dictionary<string, object?>
            {
                ["list"] = new List<object?> { Colour.Red, new Point(3, 4), 7 },
            },
            ["json"] = System.Text.Json.JsonDocument
                .Parse("""{"a":[1,2,{"b":true}]}""").RootElement,
        };

        AssertSafe(MerpeepsDoc.Normalise(hostile));
    }

    /// <summary>
    /// The real consequence: an exotic type must not blow up or smuggle its
    /// type name into the user's file in place of a value.
    /// </summary>
    [Fact]
    public void ExoticValuesSurviveARoundTripThroughTheFile()
    {
        const string src = "flowchart LR\n    a --> b\n";

        var written = MerpeepsDoc.Inject(src, new Dictionary<string, object?>
        {
            ["spec"] = 1,
            ["odd"] = new Dictionary<string, object?>
            {
                ["colour"] = Colour.Red,
                ["point"] = new Point(1, 2),
            },
        });

        Assert.Equal(MerpeepsDoc.BodyOf(src), MerpeepsDoc.BodyOf(written));
        Assert.DoesNotContain("ValueKind", written, StringComparison.Ordinal);

        var read = MerpeepsDoc.Parse(written) as Dictionary<string, object?>;
        Assert.NotNull(read);
        var odd = Assert.IsType<Dictionary<string, object?>>(read!["odd"]);
        Assert.Equal("Red", odd["colour"]);
        AssertSafe(read);
    }

    /// <summary>Numbers must not be stringified by the closing rule.</summary>
    [Fact]
    public void NumericTypesKeepTheirType()
    {
        var v = MerpeepsDoc.Normalise(new Dictionary<string, object?>
        {
            ["i"] = 7, ["l"] = 7L, ["d"] = 7.5, ["f"] = 7.5f, ["m"] = 7.5m,
        }) as Dictionary<string, object?>;

        Assert.NotNull(v);
        Assert.IsNotType<string>(v!["i"]);
        Assert.IsNotType<string>(v["l"]);
        Assert.IsNotType<string>(v["d"]);
        Assert.IsNotType<string>(v["f"]);
        Assert.IsNotType<string>(v["m"]);
    }
}
