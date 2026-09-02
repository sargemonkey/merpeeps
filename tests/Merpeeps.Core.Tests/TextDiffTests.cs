using Merpeeps.Core;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// This number is reported to agents as evidence that edits are minimal, so it
/// must not over-report. A positional comparison would claim an inserted line
/// changed everything below it.
/// </summary>
public class TextDiffTests
{
    [Fact]
    public void IdenticalTextHasNoChanges()
    {
        var t = "a\nb\nc";
        Assert.Equal(0, TextDiff.CountChangedLines(t, t));
    }

    [Fact]
    public void InsertionInTheMiddleCountsOnlyTheInsertedLine()
    {
        // The naive positional diff would report 3 here.
        var before = "a\nb\nc";
        var after = "a\nNEW\nb\nc";
        Assert.Equal(1, TextDiff.CountChangedLines(before, after));
    }

    [Fact]
    public void DeletionCountsOnlyTheRemovedLine()
    {
        Assert.Equal(1, TextDiff.CountChangedLines("a\nb\nc", "a\nc"));
    }

    [Fact]
    public void ModificationCountsAsOneRemovalAndOneAddition()
    {
        Assert.Equal(2, TextDiff.CountChangedLines("a\nb\nc", "a\nB!\nc"));
    }

    [Fact]
    public void RealisticEditScalesWithTheEditNotTheFile()
    {
        var before = string.Join("\n",
            Enumerable.Range(0, 200).Select(i => $"    n{i} --> n{i + 1}"));
        var after = before + "\n    extra[Added]";

        Assert.Equal(1, TextDiff.CountChangedLines(before, after));
    }

    [Fact]
    public void TrailingNewlinesDoNotCountAsChanges()
    {
        Assert.Equal(0, TextDiff.CountChangedLines("a\nb\n", "a\nb"));
        Assert.Equal(0, TextDiff.CountChangedLines("a\r\nb", "a\nb"));
    }

    [Fact]
    public void CompletelyDifferentTextCountsEverything()
    {
        Assert.Equal(4, TextDiff.CountChangedLines("a\nb", "x\ny"));
    }
}
