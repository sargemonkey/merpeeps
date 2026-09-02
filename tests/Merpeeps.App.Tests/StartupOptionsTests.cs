using Avalonia.Styling;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// The command line is the only way an automated caller can put the editor into
/// a known visual state without clicking a menu, so it needs to be exact.
/// </summary>
public class StartupOptionsTests
{
    [Fact]
    public void NoArgumentsMeansFollowTheSystem()
    {
        var options = StartupOptions.Parse(Array.Empty<string>());

        Assert.Null(options.OpenPath);
        Assert.Equal(ThemeVariant.Default, options.Theme);
    }

    [Fact]
    public void NullArgumentsAreTolerated()
    {
        // desktop.Args is nullable on some lifetimes; crashing on startup would
        // be a poor trade for a missing array.
        Assert.Equal(StartupOptions.Defaults, StartupOptions.Parse(null));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("Light")]
    [InlineData("LIGHT")]
    public void ThemeNamesAreCaseInsensitive(string value)
    {
        Assert.Equal(ThemeVariant.Light, StartupOptions.Parse(["--theme", value]).Theme);
    }

    [Theory]
    [InlineData("--theme", "dark")]
    [InlineData("--theme=dark", null)]
    public void BothFlagSpellingsWork(string first, string? second)
    {
        string[] args = second is null ? [first] : [first, second];

        Assert.Equal(ThemeVariant.Dark, StartupOptions.Parse(args).Theme);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("default")]
    [InlineData("auto")]
    public void SystemHasSynonyms(string value)
    {
        Assert.Equal(ThemeVariant.Default, StartupOptions.Parse(["--theme", value]).Theme);
    }

    [Fact]
    public void AnUnknownThemeFallsBackRatherThanFailing()
    {
        // A typo should not stop the editor opening.
        Assert.Equal(ThemeVariant.Default, StartupOptions.Parse(["--theme", "purple"]).Theme);
    }

    [Fact]
    public void ATrailingThemeFlagWithNoValueIsIgnored()
    {
        Assert.Equal(ThemeVariant.Default, StartupOptions.Parse(["--theme"]).Theme);
    }

    [Fact]
    public void AnExistingFileIsOpenedAndMadeAbsolute()
    {
        var path = Path.Combine(Path.GetTempPath(), $"merpeeps-{Guid.NewGuid():N}.mmd");
        File.WriteAllText(path, "flowchart TD\n    a --> b\n");

        try
        {
            var options = StartupOptions.Parse([path]);

            Assert.Equal(Path.GetFullPath(path), options.OpenPath);
            Assert.True(Path.IsPathRooted(options.OpenPath));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingFileIsIgnoredSoTheEditorStillOpens()
    {
        Assert.Null(StartupOptions.Parse(["no-such-file.mmd"]).OpenPath);
    }

    [Fact]
    public void AThemeValueIsNotMistakenForAFile()
    {
        // "light" as a bare word must be consumed by the flag, not treated as a
        // path — otherwise a file called "light" in the cwd would be opened.
        var path = Path.Combine(Directory.GetCurrentDirectory(), "light");
        File.WriteAllText(path, "flowchart TD\n    a --> b\n");

        try
        {
            var options = StartupOptions.Parse(["--theme", "light"]);

            Assert.Null(options.OpenPath);
            Assert.Equal(ThemeVariant.Light, options.Theme);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileAndAThemeCanBeGivenTogetherInEitherOrder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"merpeeps-{Guid.NewGuid():N}.mmd");
        File.WriteAllText(path, "flowchart TD\n    a --> b\n");

        try
        {
            foreach (string[] args in new[]
                     {
                         new[] { path, "--theme", "dark" },
                         new[] { "--theme", "dark", path },
                     })
            {
                var options = StartupOptions.Parse(args);

                Assert.Equal(Path.GetFullPath(path), options.OpenPath);
                Assert.Equal(ThemeVariant.Dark, options.Theme);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnknownFlagsAreSkipped()
    {
        var options = StartupOptions.Parse(["--verbose", "-x", "--theme", "light"]);

        Assert.Equal(ThemeVariant.Light, options.Theme);
        Assert.Null(options.OpenPath);
    }
}
