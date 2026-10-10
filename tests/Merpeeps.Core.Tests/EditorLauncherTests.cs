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

        Assert.Equal([Path.Combine(here, Exe), Path.Combine(here, EditorLauncher.LegacyExecutableName)], candidates);
    }

    [Fact]
    public void Names_the_platforms_executable()
    {
        Assert.Equal(
            OperatingSystem.IsWindows() ? "Merpie.App.exe" : "Merpie.App",
            EditorLauncher.ExecutableName);
    }

    [Fact]
    public void Prefers_Merpie_and_can_still_open_legacy_installs()
    {
        var here = Path.Combine("C:", "tools", "merpeeps");
        var modern = Path.Combine(here, Exe);
        var legacy = Path.Combine(here, EditorLauncher.LegacyExecutableName);
        Assert.Equal(modern, EditorLauncher.Locate(here, null, Only(modern, legacy)).Path);
        Assert.Equal(legacy, EditorLauncher.Locate(here, null, Only(legacy)).Path);
        Assert.Equal(legacy, EditorLauncher.Locate(here, legacy, Only(modern, legacy)).Path);
        Assert.Equal(legacy, EditorLauncher.Locate("", here, Only(legacy)).Path);
    }

    [Fact]
    public void Retains_legacy_sibling_build_and_finds_the_branded_portable_package()
    {
        var caller = Path.Combine("C:", "repo", "src", "Merpeeps.Cli", "bin", "Release", "net10.0");
        var oldBuild = Path.Combine("C:", "repo", "src", "Merpeeps.App", "bin", "Release", "net10.0", EditorLauncher.LegacyExecutableName);
        Assert.Equal(oldBuild, EditorLauncher.Locate(caller, null, Only(oldBuild)).Path);
        var package = Path.Combine("C:", "portable", "merpie-win-x64");
        var editor = Path.Combine(package, "app", Exe);
        Assert.Equal(editor, EditorLauncher.Locate(Path.Combine(package, "mcp"), null, Only(editor)).Path);
    }

    [Fact]
    public void Finds_the_mac_application_bundle_without_changing_the_override_variable()
    {
        var package = Path.Combine("C:", "portable", "merpie-osx-arm64");
        var bundle = Path.Combine(package, "Merpie.app");
        var editor = Path.Combine(bundle, "Contents", "MacOS", Exe);
        Assert.Equal(editor, EditorLauncher.Locate("", bundle, Only(editor)).Path);
        Assert.Equal(editor, EditorLauncher.Locate(Path.Combine(package, "mcp"), null, Only(editor)).Path);
        Assert.Equal("MERPEEPS_APP", EditorLauncher.OverrideVariable);
    }
}
