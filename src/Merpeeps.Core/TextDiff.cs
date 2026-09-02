namespace Merpeeps.Core;

/// <summary>
/// Line diffing used to report how much of a document an edit actually touched.
///
/// <para>A naive positional comparison over-reports badly: inserting one line
/// makes every line below it look changed. Since this number is surfaced to
/// agents as evidence that edits are minimal, it has to be honest, so this uses
/// a longest-common-subsequence diff.</para>
/// </summary>
public static class TextDiff
{
    /// <summary>
    /// Number of lines that were genuinely added or removed between the two
    /// texts. Lines that merely shifted position are not counted.
    /// </summary>
    public static int CountChangedLines(string before, string after)
    {
        var a = Split(before);
        var b = Split(after);
        var common = LongestCommonSubsequence(a, b);
        return (a.Length - common) + (b.Length - common);
    }

    private static string[] Split(string text) =>
        text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static int LongestCommonSubsequence(string[] a, string[] b)
    {
        // Trim the common prefix and suffix first: diagram edits are local, so
        // this usually reduces the quadratic core to a handful of lines.
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;

        var endA = a.Length - 1;
        var endB = b.Length - 1;
        while (endA >= start && endB >= start && a[endA] == b[endB]) { endA--; endB--; }

        var lenA = endA - start + 1;
        var lenB = endB - start + 1;
        if (lenA <= 0 || lenB <= 0) return start + (a.Length - 1 - endA);

        var table = new int[lenA + 1, lenB + 1];
        for (var i = 1; i <= lenA; i++)
            for (var j = 1; j <= lenB; j++)
                table[i, j] = a[start + i - 1] == b[start + j - 1]
                    ? table[i - 1, j - 1] + 1
                    : Math.Max(table[i - 1, j], table[i, j - 1]);

        return start + (a.Length - 1 - endA) + table[lenA, lenB];
    }
}
