using Merpeeps.Core.Tooling;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// How the CLI and the MCP server find the editor.
///
/// <para>Worth testing precisely because the failure is silent and
/// environmental: on the machine where it was written the editor is always
/// exactly where the developer expects, so a resolution order that is wrong for
/// a published install looks perfect until someone else runs it. Injecting the
/// existence probe lets every deployment shape be checked here rather than
/// discovered later.</para>
/// </summary>
public class EditorLauncherTests
{
    private static string Exe => EditorLauncher.ExecutableName;

    private static Func<string, bool> Only(params string[] present) =>
        path => present.Contains(path, StringComparer.OrdinalIgnoreCase);

    private static Func<string, bool> Nothing => _ => false;

    [Fact]
    public void Prefers_the_binary_sitting_next_to_the_caller()
    {
        var here = Path.Combine("C:", "tools", "merpeeps");
        var beside = Path.Combine(here, Exe);

        var found = EditorLauncher.Locate(here, null, Only(beside));

        Assert.True(found.Found);
        Assert.Equal(beside, found.Path);
    }

    /// <summary>
    /// The working-tree case: a CLI in its own bin directory finds the editor
    /// in the matching one, without being told the configuration or framework.
    /// </summary>
    [Theory]
    [InlineData("Merpeeps.Cli")]
    [InlineData("Merpeeps.Mcp")]
    public void Finds_the_sibling_project_build(string project)
    {
        var caller = Path.Combine("C:", "repo", "src", project, "bin", "Debug", "net10.0");
        var editor = Path.Combine(
            "C:", "repo", "src", "Merpeeps.App", "bin", "Debug", "net10.0", Exe);

        var found = EditorLauncher.Locate(caller, null, Only(editor));

        Assert.True(found.Found);
        Assert.Equal(editor, found.Path);
    }

    /// <summary>
    /// Release must not resolve to Debug. Silently launching the other
    /// configuration would produce bug reports about a build that is not the
    /// one being run.
    /// </summary>
    [Fact]
    public void Keeps_the_callers_configuration()
    {
        var caller = Path.Combine(
            "C:", "repo", "src", "Merpeeps.Cli", "bin", "Release", "net10.0");
        var debugBuild = Path.Combine(
            "C:", "repo", "src", "Merpeeps.App", "bin", "Debug", "net10.0", Exe);

        var found = EditorLauncher.Locate(caller, null, Only(debugBuild));

        Assert.False(found.Found);
        Assert.DoesNotContain(debugBuild, found.Searched);
    }

    [Fact]
    public void The_override_wins_over_a_binary_that_is_right_there()
    {
        var here = Path.Combine("C:", "tools", "merpeeps");
        var beside = Path.Combine(here, Exe);
        var chosen = Path.Combine("D:", "builds", "custom", Exe);

        var found = EditorLauncher.Locate(here, chosen, Only(beside, chosen));

        Assert.Equal(chosen, found.Path);
    }

    [Fact]
    public void The_override_may_name_the_folder_rather_than_the_file()
    {
        var folder = Path.Combine("D:", "builds", "custom");
        var editor = Path.Combine(folder, Exe);

        var found = EditorLauncher.Locate("", folder, Only(editor));

        Assert.Equal(editor, found.Path);
    }

    /// <summary>
    /// An override pointing at some other executable is a mistake, and one that
    /// would otherwise surface as a launched application that is not the editor.
    /// </summary>
    [Fact]
    public void Refuses_an_override_that_is_not_the_editor()
    {
        var wrong = Path.Combine("D:", "builds", "notepad.exe");

        var found = EditorLauncher.Locate("", wrong, Only(wrong));

        Assert.False(found.Found);
    }

    /// <summary>
    /// The control for every test above: the same inputs with nothing on disk
    /// must fail. Without it, a <c>Locate</c> that returned its first candidate
    /// unconditionally would pass all of them.
    /// </summary>
    [Fact]
    public void Finds_nothing_when_nothing_is_installed()
    {
        var caller = Path.Combine(
            "C:", "repo", "src", "Merpeeps.Cli", "bin", "Debug", "net10.0");

        var found = EditorLauncher.Locate(caller, null, Nothing);

        Assert.False(found.Found);
        Assert.Null(found.Path);
    }

    /// <summary>
    /// A failure has to say where it looked, or the person reading it has no
    /// next move.
    /// </summary>
    [Fact]
    public void Reports_every_place_it_looked()
    {
        var caller = Path.Combine(
            "C:", "repo", "src", "Merpeeps.Cli", "bin", "Debug", "net10.0");

        var found = EditorLauncher.Locate(caller, null, Nothing);

        Assert.Contains(Path.Combine(caller, Exe), found.Searched);
        Assert.Contains(found.Searched, p => p.Contains("Merpeeps.App"));
    }

    [Fact]
    public void Does_not_list_the_same_place_twice()
    {
        var here = Path.Combine("C:", "tools", "merpeeps");

        var found = EditorLauncher.Locate(here, Path.Combine(here, Exe), Nothing);

        Assert.Equal(found.Searched.Count, found.Searched.Distinct().Count());
    }

    /// <summary>
    /// A checkout inside a folder of the same name must resolve against the
    /// build, not the outer directory that happens to share its spelling.
    /// </summary>
    [Fact]
    public void Uses_the_rightmost_project_segment()
    {
        var caller = Path.Combine(
            "C:", "Merpeeps.Cli", "src", "Merpeeps.Cli", "bin", "Debug", "net10.0");
        var editor = Path.Combine(
            "C:", "Merpeeps.Cli", "src", "Merpeeps.App", "bin", "Debug", "net10.0", Exe);

        var found = EditorLauncher.Locate(caller, null, Only(editor));

        Assert.Equal(editor, found.Path);
    }

    /// <summary>
    /// Nothing to swap means nothing to offer — a caller somewhere unrelated
    /// should get one candidate, not a fabricated sibling path.
    /// </summary>
    [Fact]
    public void Offers_no_sibling_when_the_caller_is_not_in_a_project_tree()
    {
        var here = Path.Combine("C:", "somewhere", "else");

        var candidates = EditorLauncher.Candidates(here);

        Assert.Equal([Path.Combine(here, Exe)], candidates);
    }

    [Fact]
    public void Names_the_platforms_executable()
    {
        Assert.Equal(
            OperatingSystem.IsWindows() ? "Merpeeps.App.exe" : "Merpeeps.App",
            EditorLauncher.ExecutableName);
    }
}
