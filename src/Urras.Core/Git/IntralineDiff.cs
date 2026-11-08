using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// A piece of a changed line; Changed marks the words that differ from the paired line
public readonly record struct InlineSegment(string Text, bool Changed);

// Finds the words that changed between a removed line and the added line that replaced it
public static partial class IntralineDiff
{
    // Longer lines are compared by common start and end only, to keep large diffs fast
    private const int MAX_TOKENS = 400;

    // Below this share of unchanged text the lines are treated as unrelated, and nothing is highlighted
    private const double MIN_SIMILARITY = 0.3;

    [GeneratedRegex(@"\w+|\s+|.", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static (List<InlineSegment> Old, List<InlineSegment> New) Compare(string oldText, string newText)
    {
        List<string> oldTokens = Tokenize(oldText);
        List<string> newTokens = Tokenize(newText);

        bool[] oldKept;
        bool[] newKept;
        if (oldTokens.Count > MAX_TOKENS || newTokens.Count > MAX_TOKENS)
            (oldKept, newKept) = CommonEnds(oldTokens, newTokens);
        else
            (oldKept, newKept) = LongestCommonSubsequence(oldTokens, newTokens);

        int keptLength = oldTokens.Where((_, index) => oldKept[index]).Sum(token => token.Length);
        int longest = Math.Max(oldText.Length, newText.Length);
        if (longest == 0 || (double)keptLength / longest < MIN_SIMILARITY)
            return (Whole(oldText), Whole(newText));

        return (Merge(oldTokens, oldKept), Merge(newTokens, newKept));
    }

    private static List<string> Tokenize(string text)
    {
        return TokenRegex().Matches(text).Select(match => match.Value).ToList();
    }

    private static (bool[], bool[]) LongestCommonSubsequence(List<string> first, List<string> second)
    {
        int[,] lengths = new int[first.Count + 1, second.Count + 1];
        for (int i = first.Count - 1; i >= 0; --i)
        {
            for (int j = second.Count - 1; j >= 0; --j)
            {
                lengths[i, j] = first[i] == second[j] ? lengths[i + 1, j + 1] + 1
                                                      : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        bool[] firstKept = new bool[first.Count];
        bool[] secondKept = new bool[second.Count];
        int a = 0;
        int b = 0;
        while (a < first.Count && b < second.Count)
        {
            if (first[a] == second[b])
            {
                firstKept[a++] = true;
                secondKept[b++] = true;
            }
            else if (lengths[a + 1, b] >= lengths[a, b + 1])
                ++a;
            else
                ++b;
        }

        return (firstKept, secondKept);
    }

    private static (bool[], bool[]) CommonEnds(List<string> first, List<string> second)
    {
        bool[] firstKept = new bool[first.Count];
        bool[] secondKept = new bool[second.Count];

        int start = 0;
        while (start < first.Count && start < second.Count && first[start] == second[start])
        {
            firstKept[start] = true;
            secondKept[start] = true;
            ++start;
        }

        int end = 0;
        while (end < first.Count - start && end < second.Count - start &&
               first[first.Count - 1 - end] == second[second.Count - 1 - end])
        {
            firstKept[first.Count - 1 - end] = true;
            secondKept[second.Count - 1 - end] = true;
            ++end;
        }

        return (firstKept, secondKept);
    }

    private static List<InlineSegment> Merge(List<string> tokens, bool[] kept)
    {
        List<InlineSegment> segments = new List<InlineSegment>();
        for (int i = 0; i < tokens.Count; ++i)
        {
            bool changed = !kept[i];
            if (segments.Count > 0 && segments[^1].Changed == changed)
                segments[^1] = segments[^1] with { Text = segments[^1].Text + tokens[i] };
            else
                segments.Add(new InlineSegment(tokens[i], changed));
        }

        return segments;
    }

    private static List<InlineSegment> Whole(string text)
    {
        List<InlineSegment> segments = new List<InlineSegment>();
        if (text.Length > 0)
            segments.Add(new InlineSegment(text, false));
        return segments;
    }
}
