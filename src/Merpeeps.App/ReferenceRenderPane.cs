using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Merpeeps.Render;
using System.Security.Cryptography;
using System.Text;

namespace Merpeeps.App;

/// <summary>
/// Shows the diagram as the real upstream <c>mermaid</c> draws it, beside our
/// own canvas.
///
/// <para>Our canvas shows what Merpeeps <em>thinks</em> a diagram looks like. This
/// shows what a stock renderer actually draws, which is the only thing that
/// settles a disagreement between the two. Pointed at the Merpeeps mode it doubles
/// as a live check that the published npm package still works, because it loads
/// that package's real source rather than a copy.</para>
///
/// <para>Degrades rather than crashes. The webview needs a platform web engine —
/// WebView2 on Windows, WKWebView on macOS, WebKitGTK on Linux — and on a
/// machine without one the pane explains itself instead of taking the editor
/// down with it. That path is not hypothetical: a bare Linux box without
/// <c>libwebkit2gtk</c> hits it.</para>
/// </summary>
public sealed class ReferenceRenderPane : UserControl
{
    private readonly Panel _host = new();
    private readonly TextBlock _status = new()
    {
        Margin = new Avalonia.Thickness(12),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    private NativeWebView? _web;
    private bool _navigated;
    private string? _pending;
    private RenderAssets.Mode _mode = RenderAssets.Mode.WithMerpeeps;
    private string? _layout;
    private bool _dark;

    /// <summary>
    /// What the pane can be pointed at, in the order the dropdown offers them.
    ///
    /// <para>The pane compares; it no longer computes. It once offered ELK's four
    /// algorithms so their arrangements could be harvested into the file, but the
    /// plugin is no longer embedded and the editor no longer borrows layouts, so
    /// listing them would be an offer it cannot honour.</para>
    /// </summary>
    public static readonly (string Label, RenderAssets.Mode Mode, string? Layout)[] Choices =
    [
        ("mermaid + Merpie", RenderAssets.Mode.WithMerpeeps, null),
        ("stock mermaid (dagre)", RenderAssets.Mode.Stock, "dagre"),
    ];

    /// <summary>True when a web engine was found and the pane is usable.</summary>
    public bool Available => _web is not null;

    /// <summary>Why the pane is unavailable, when it is.</summary>
    public string? Unavailable { get; private set; }

    public ReferenceRenderPane()
    {
        Content = _host;
        TryCreateWebView();
    }

    /// <summary>
    /// Create the webview, or fall back to an explanation.
    ///
    /// <para>Construction is what throws on a machine with no web engine, so it
    /// is caught here rather than left to surface as a blank pane or a crash on
    /// startup.</para>
    /// </summary>
    private void TryCreateWebView()
    {
        // A web view needs a real window. Under Avalonia's headless test
        // platform there isn't one: constructing it spins up WebView2 anyway,
        // costs about 24 seconds per test, and then throws
        // RPC_E_CHANGED_MODE on teardown. Skipping it makes the pane report
        // itself unavailable, which is both true and the path worth testing.
        if (IsHeadless)
        {
            _web = null;
            Unavailable = "The reference view is not available without a desktop window.";
            _status.Text = Unavailable;
            _host.Children.Add(_status);
            return;
        }

        try
        {
            _web = new NativeWebView();
            _web.NavigationCompleted += (_, _) =>
            {
                _navigated = true;
                if (_pending is { } text) { _pending = null; _ = PushAsync(text); }
            };
            _host.Children.Add(_web);
        }
        catch (Exception ex)
        {
            _web = null;
            Unavailable =
                "The reference view needs a system web engine.\n\n" +
                (OperatingSystem.IsLinux()
                    ? "On Linux install libwebkit2gtk-4.1-0, then reopen the editor."
                    : OperatingSystem.IsWindows()
                        ? "On Windows 10 install the Microsoft Edge WebView2 runtime."
                        : "No platform web engine was available.") +
                $"\n\n({ex.GetType().Name}: {ex.Message})";

            _status.Text = Unavailable;
            _host.Children.Add(_status);
        }
    }

    /// <summary>
    /// True when running under Avalonia's headless platform.
    /// </summary>
    /// <remarks>
    /// Detected by the loaded assembly rather than a test-only flag, so the
    /// production code carries no switch anyone could set by accident.
    /// Avalonia.Headless is only ever loaded by a test host.
    /// </remarks>
    private static bool IsHeadless =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name == "Avalonia.Headless");

    /// <summary>
    /// Choose whether to render with stock mermaid or with our layout plugin.
    /// Changing this reloads the page, because the two shells differ in what
    /// they load.
    /// </summary>
    public void SetMode(RenderAssets.Mode mode, bool dark) => SetMode(mode, null, dark);

    /// <summary>
    /// Choose renderer and layout. Changing either reloads the page, because the
    /// layout name is baked into <c>mermaid.initialize</c> in the shell rather
    /// than settable afterwards.
    /// </summary>
    public void SetMode(RenderAssets.Mode mode, string? layout, bool dark)
    {
        if (_mode == mode && _layout == layout && _dark == dark) return;
        _mode = mode;
        _layout = layout;
        _dark = dark;
        _navigated = false;
    }

    /// <summary>
    /// Render <paramref name="source"/>.
    ///
    /// <para>Navigation happens once and the diagram is pushed in afterwards.
    /// Re-navigating per edit would re-parse a three-megabyte bundle on every
    /// keystroke.</para>
    /// </summary>
    public async Task ShowAsync(string source)
    {
        if (_web is null) return;

        if (!_navigated)
        {
            _pending = source;
            try
            {
                _web.Navigate(ShellUri(_mode, _layout, _dark));
            }
            catch (Exception ex)
            {
                Fail($"could not load the reference renderer: {ex.Message}");
            }
            return;
        }

        await PushAsync(source);
    }

    /// <summary>
    /// Shell content by mode and theme. The content is a pure function of those
    /// two, so building it more than once is pure waste — and at ~3.6 MB a time,
    /// repeated builds churn the large-object heap enough to be measurable
    /// (seconds, in a test host). The editor benefits too: switching mode twice
    /// no longer recomposes megabytes.
    /// </summary>
    private static readonly Dictionary<(RenderAssets.Mode, string?, bool), string> ShellCache = new();
    private static readonly string ShellDirectory = Path.Combine(
        Path.GetTempPath(), "merpeeps-preview", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Write the shell to a temporary file and return its URI.
    ///
    /// <para>Not <c>NavigateToString</c>, which is the obvious choice and does
    /// not work here: WebView2 caps that string at about 2 MB and the shell is
    /// 3.5 MB with mermaid embedded. Exceeding it took the whole editor down
    /// rather than failing the call, so the size limit has to be designed around
    /// rather than caught.</para>
    ///
    /// <para>A file URL also keeps inline module scripts and blob imports
    /// working, which the oracle already proves against real Chrome.</para>
    /// </summary>
    /// <remarks>
    /// Static and internal so the file-writing half can be tested without a web
    /// engine — which is the only half a headless runner can reach.
    /// </remarks>
    internal static Uri ShellUri(RenderAssets.Mode mode, bool dark) => ShellUri(mode, null, dark);

    internal static Uri ShellUri(RenderAssets.Mode mode, string? layout, bool dark)
    {
        var dir = ShellDirectory;
        Directory.CreateDirectory(dir);

        // One file per mode and theme, so switching does not rewrite megabytes.
        var tag = layout is null ? "default" : layout.Replace('.', '-');
        lock (ShellCache)
        {
            if (!ShellCache.TryGetValue((mode, layout, dark), out var html))
            {
                html = RenderAssets.BuildShell(mode, dark, autoRender: null, layout: layout);
                ShellCache[(mode, layout, dark)] = html;
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
            var file = Path.Combine(dir, $"shell-{mode}-{tag}-{(dark ? "dark" : "light")}-{fingerprint}.html");
            if (!File.Exists(file))
            {
                using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }
            return new Uri(file);
        }
    }

    private void Fail(string message)
    {
        Unavailable = message;
        if (!_host.Children.Contains(_status)) _host.Children.Add(_status);
        _status.Text = message;
        _status.IsVisible = true;
    }

    private async Task PushAsync(string source)
    {
        if (_web is null) return;

        try
        {
            // The shell exposes one entry point. Passing the diagram as a JS
            // string literal rather than interpolating it means a stray quote or
            // backtick in a label cannot break the call.
            await _web.InvokeScript(
                $"window.merpeepsRender && window.merpeepsRender({RenderAssets.JsLiteralForHost(source)})");
        }
        catch (Exception ex)
        {
            // A failed preview must never take the editor with it.
            Unavailable = $"The reference view could not render: {ex.Message}";
        }
    }
}
