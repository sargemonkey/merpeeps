using Merpeeps.Core;
using Merpeeps.Render;
using Xunit;

namespace Merpeeps.App.Tests;

public class MermaidValidationPageTests
{
    [Fact]
    public void ShellUsesTheExactFrozenOfflineParser()
    {
        var shell = MermaidValidationPage.BuildShell();
        Assert.Equal(MermaidDiagramCatalog.MermaidVersion, RenderAssets.MermaidVersion);
        Assert.Contains("data-mermaid-version=\"12.0.0\"", shell);
        Assert.Contains("data-zenuml-version=\"3.47.9\"", shell);
        Assert.Contains("await instance.parse(code)", shell);
        Assert.Contains("parsed.errorDetails.length", shell);
        Assert.Equal("3.47.9", RenderAssets.ZenUmlVersion);
        Assert.Contains("Peng Xiao", RenderAssets.ZenUmlLicence);
        Assert.Contains("react@19.", RenderAssets.ZenUmlThirdPartyNotices);
        Assert.Contains("antlr4@4.11.", RenderAssets.ZenUmlThirdPartyNotices);
        Assert.Contains("default-src 'none'", shell);
        Assert.Contains("engine.parse(source", shell);
        Assert.DoesNotContain("<script src=", shell);
        Assert.EndsWith("</body></html>\n", shell);
    }

    [Fact]
    public void HostSourceIsEscapedInsteadOfInterpolatedAsCode()
    {
        var script = MermaidValidationPage.StartScript("request", "</script>\n'\"`\\");
        Assert.DoesNotContain("</script>", script);
        Assert.Contains("\\u003c", script);
        Assert.Contains("\\n", script);
    }

    [Theory]
    [InlineData("pending", MermaidSyntaxStatus.Pending)]
    [InlineData("valid", MermaidSyntaxStatus.Valid)]
    [InlineData("invalid", MermaidSyntaxStatus.Invalid)]
    [InlineData("unsupported", MermaidSyntaxStatus.Unsupported)]
    [InlineData("unavailable", MermaidSyntaxStatus.Unavailable)]
    public void StatusNeverCollapsesFailureIntoSuccess(string status, MermaidSyntaxStatus expected)
    {
        var result = MermaidValidationPage.ParseResult(Response(status), "r1");
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void StaleOrWrongVersionResultIsRejected()
    {
        Assert.Throws<FormatException>(() => MermaidValidationPage.ParseResult(Response("valid"), "r2"));
        Assert.Throws<FormatException>(() => MermaidValidationPage.ParseResult(
            Response("valid").Replace("12.0.0", "11.16.1"), "r1"));
        Assert.Throws<FormatException>(() => MermaidValidationPage.ParseResult(Response("surprise"), "r1"));
    }

    [Fact]
    public void AValidResultRequiresActualTypeAndNoErrors()
    {
        Assert.Throws<FormatException>(() => MermaidValidationPage.ParseResult(
            Response("valid").Replace("\"flowchart-v2\"", "null"), "r1"));
        Assert.Throws<FormatException>(() => MermaidValidationPage.ParseResult(
            Response("invalid").Replace("\"invalid\"", "\"valid\""), "r1"));
    }

    [Fact]
    public void MissingDiagnosticCannotHideAFailure()
    {
        Assert.Throws<FormatException>(() => MermaidValidationPage.ParseResult(
            Response("pending").Replace("\"pending\"", "\"invalid\""), "r1"));
    }

    private static string Response(string status)
    {
        var diagnostics = status is "valid" or "pending" ? "[]"
            : """[{"code":"test-error","message":"Explicit failure","parserLine":null,"parserColumn":null}]""";
        return $$"""
            {"requestId":"r1","mermaidVersion":"12.0.0","status":"{{status}}",
             "diagramType":"flowchart-v2","diagnostics":{{diagnostics}}}
            """;
    }
}
