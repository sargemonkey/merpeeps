using System.Diagnostics;

namespace Merpeeps.Core.Tooling;

/// <summary>
/// Finds the desktop editor and hands a file to it.
///
/// <para>This exists because positions are the one thing an automated caller
/// cannot supply honestly. An agent can write a diagram's structure, its
/// colours and its icons — all of those follow from meaning. Where a node
/// belongs on the page does not; that is a judgement, and the person making it
/// needs a canvas. So the useful thing an agent can do is compose the file and
/// then get out of the way, which is what this turns into one call.</para>
///
/// <para>Lives in the core rather than in either front end because the CLI and
/// the MCP server both need it, and a launcher duplicated across two projects
/// is a launcher that will disagree with itself within a release.</para>
/// </summary>
public static class EditorLauncher
{
    /// <summary>Environment variable that overrides the search entirely.</summary>
    public const string OverrideVariable = "MERPEEPS_APP";

    /// <summary>Project directory names that sit beside the editor's own.</summary>
    private static readonly string[] SiblingProjects =
        ["Merpeeps.Cli", "Merpeeps.Mcp"];

    /// <summary>The editor's filename on this platform.</summary>
    public static string ExecutableName =>
        OperatingSystem.IsWindows() ? "Merpeeps.App.exe" : "Merpeeps.App";

    /// <summary>
    /// Every place worth looking, most specific first.
    /// </summary>
    /// <remarks>
    /// <para>Pure, and separated from the probing so it can be tested without a
    /// filesystem. The order encodes three deployment shapes:</para>
    /// <list type="number">
    ///   <item><description>An explicit <c>MERPEEPS_APP</c>, which always
    ///   wins — someone who sets it has a reason.</description></item>
    ///   <item><description>Alongside the calling binary, which is how a
    ///   published install lays out.</description></item>
    ///   <item><description>The sibling project's build output, which is how a
    ///   working tree lays out.</description></item>
    /// </list>
    /// <para>The third case rebuilds the path from the caller's own location
    /// rather than guessing at a configuration or a target framework: a CLI
    /// running from <c>src/Merpeeps.Cli/bin/Debug/net10.0</c> looks in
    /// <c>src/Merpeeps.App/bin/Debug/net10.0</c>. Swapping one segment keeps
    /// the Debug/Release choice and the framework version correct for free,
    /// and stops being a guess that rots the next time either changes.</para>
    /// </remarks>
    public static IReadOnlyList<string> Candidates(
        string baseDirectory, string? overridePath = null)
    {
        var found = new List<string>();

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var trimmed = overridePath.Trim();

            // Accept a directory as well as a file. Pointing at the folder the
            // editor lives in is the more natural thing to type, and refusing
            // it would be pedantry dressed up as strictness.
            found.Add(trimmed);
            found.Add(Path.Combine(trimmed, ExecutableName));
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            var here = baseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            found.Add(Path.Combine(here, ExecutableName));

            if (SiblingBuildOutput(here) is { } sibling) found.Add(sibling);
        }

        // Distinct because an override naming the calling directory would
        // otherwise produce the same path twice, and a report that lists the
        // same place it looked twice reads like a bug.
        return found.Distinct(PathComparer).ToArray();
    }

    /// <summary>
    /// Resolve the editor, reporting where we looked when we fail.
    /// </summary>
    /// <param name="baseDirectory">Directory the calling binary runs from.</param>
    /// <param name="overridePath">
    /// Value of <see cref="OverrideVariable"/>, or null when it is unset.
    /// </param>
    /// <param name="exists">
    /// Filesystem probe, injected so the resolution order can be tested
    /// exhaustively without building anything.
    /// </param>
    public static EditorLocation Locate(
        string baseDirectory, string? overridePath, Func<string, bool> exists)
    {
        var candidates = Candidates(baseDirectory, overridePath);

        foreach (var candidate in candidates)
        {
            // A bare directory that happens to exist is not the editor. Only
            // accept a path that ends in the executable's own name, otherwise
            // an override pointing at a folder would "succeed" and then fail
            // confusingly at launch.
            if (!IsExecutable(candidate)) continue;
            if (exists(candidate)) return new EditorLocation(candidate, candidates);
        }

        return new EditorLocation(null, candidates);
    }

    /// <summary>Resolve against the real environment.</summary>
    public static EditorLocation Locate() =>
        Locate(AppContext.BaseDirectory,
               Environment.GetEnvironmentVariable(OverrideVariable),
               File.Exists);

    /// <summary>
    /// Start the editor on a file and return immediately.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately does not wait. The caller is a CLI invocation or an
    /// agent turn; both should end while the person is still arranging the
    /// diagram, and an agent blocked on a window it cannot see is an agent that
    /// has hung.</para>
    /// <para>Making that true took two attempts, and the first one is worth
    /// recording because it looks correct. A child launched with
    /// <c>UseShellExecute = false</c> keeps its caller's terminal open for as
    /// long as the editor lives: any shell or agent capturing output waits on a
    /// pipe the editor is still holding, so the launcher returns, the process
    /// exits, and the caller hangs anyway. Redirecting the three standard
    /// streams does <em>not</em> fix it — on Windows that path calls
    /// <c>CreateProcess</c> with handle inheritance on, so the child receives a
    /// duplicate of every inheritable handle, the caller's pipe among them,
    /// whatever the standard streams were pointed at.</para>
    /// <para>So Windows goes through <c>ShellExecuteEx</c> instead, which does
    /// not inherit handles at all. Elsewhere the reverse is true: POSIX only
    /// passes the three standard descriptors plus any without close-on-exec,
    /// and .NET already sets that flag on its own, so redirecting is both
    /// sufficient and necessary — <c>UseShellExecute</c> on Unix means
    /// <c>xdg-open</c>, which would try to open the editor binary as a document
    /// rather than run it.</para>
    /// </remarks>
    public static int Launch(string editorPath, string filePath)
    {
        var windows = OperatingSystem.IsWindows();

        var start = new ProcessStartInfo(editorPath)
        {
            UseShellExecute = windows,
            RedirectStandardOutput = !windows,
            RedirectStandardError = !windows,
            RedirectStandardInput = !windows,
        };
        start.ArgumentList.Add(filePath);

        // The editor resolves assets relative to its own directory, so start it
        // there rather than in whatever directory the caller happened to be in.
        var home = Path.GetDirectoryName(editorPath);
        if (!string.IsNullOrEmpty(home)) start.WorkingDirectory = home;

        var process = Process.Start(start)
            ?? throw new InvalidOperationException($"could not start {editorPath}");

        // Nothing will read these, and an undrained pipe would eventually block
        // the editor if it ever did log.
        if (!windows) process.StandardInput.Close();

        return process.Id;
    }

    /// <summary>
    /// The same build output, one project across.
    /// </summary>
    private static string? SiblingBuildOutput(string baseDirectory)
    {
        var separators = new[]
        {
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
        };
        var segments = baseDirectory.Split(
            separators, StringSplitOptions.RemoveEmptyEntries);

        // Search from the right: a path could contain the project name more
        // than once (a checkout inside a folder of the same name), and the
        // occurrence that identifies *this* build is the last one.
        var at = Array.FindLastIndex(segments, s =>
            SiblingProjects.Contains(s, StringComparer.OrdinalIgnoreCase));
        if (at < 0) return null;

        segments[at] = "Merpeeps.App";

        // Path.Combine drops everything before a rooted segment, so a Unix
        // absolute path has to have its leading slash put back by hand.
        var joined = Path.Combine(segments);
        if (baseDirectory.StartsWith('/')) joined = "/" + joined;

        return Path.Combine(joined, ExecutableName);
    }

    private static bool IsExecutable(string candidate) =>
        string.Equals(Path.GetFileName(candidate), ExecutableName,
            StringComparison.OrdinalIgnoreCase);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}

/// <summary>
/// The outcome of a search: the editor, or an account of where it was not.
/// </summary>
/// <remarks>
/// <see cref="Searched"/> is carried even on success so a caller can explain
/// itself. "Could not find the editor" is a dead end; "could not find the
/// editor, looked in these three places" is something a person can act on.
/// </remarks>
public sealed record EditorLocation(string? Path, IReadOnlyList<string> Searched)
{
    public bool Found => Path is not null;
}
