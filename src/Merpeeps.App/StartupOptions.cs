using Avalonia.Styling;

namespace Merpeeps.App;

/// <summary>
/// The command line the editor accepts. Deliberately tiny: a file to open and
/// a theme to force.
/// </summary>
/// <remarks>
/// <para>
/// <c>--theme</c> exists because automated UI checks — screenshot comparison,
/// contrast assertions, computer-use driving — need the window to look the same
/// on every machine. Following the operating system is the right default for a
/// person and the wrong default for a test runner, so the flag lets a caller opt
/// out of the ambient setting rather than clicking through a menu first.
/// </para>
/// <para>
/// Parsing lives here, apart from <see cref="App"/>, so it can be tested without
/// starting a windowing system.
/// </para>
/// </remarks>
public sealed record StartupOptions(string? OpenPath, ThemeVariant Theme)
{
    public static StartupOptions Defaults { get; } = new(null, ThemeVariant.Default);

    /// <summary>
    /// Reads options from a raw argument list. Unknown flags and unreadable
    /// paths are ignored: a launcher or file association may hand us anything,
    /// and refusing to start is a worse outcome than starting empty.
    /// </summary>
    public static StartupOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0) return Defaults;

        string? path = null;
        var theme = ThemeVariant.Default;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            // Accept "--theme dark" and "--theme=dark" alike; both spellings turn
            // up in shells, task runners and CI YAML.
            if (TryFlagValue(args, ref i, "--theme", out var value))
            {
                if (ParseTheme(value) is { } picked) theme = picked;
                continue;
            }

            if (arg.StartsWith('-')) continue;

            if (path is null && File.Exists(arg)) path = Path.GetFullPath(arg);
        }

        return new StartupOptions(path, theme);
    }

    private static bool TryFlagValue(
        IReadOnlyList<string> args, ref int i, string name, out string value)
    {
        var arg = args[i];

        if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(name.Length + 1)..];
            return true;
        }

        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
        {
            value = args[++i];
            return true;
        }

        value = "";
        return false;
    }

    private static ThemeVariant? ParseTheme(string value) => value.ToLowerInvariant() switch
    {
        "light" => ThemeVariant.Light,
        "dark" => ThemeVariant.Dark,
        "system" or "default" or "auto" => ThemeVariant.Default,
        _ => null,
    };
}
