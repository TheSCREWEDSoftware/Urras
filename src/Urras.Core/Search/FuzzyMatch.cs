namespace Urras.Core.Search;

// Quick Launch matching: the query's letters must appear in order; matches at word starts and in runs score higher
public static class FuzzyMatch
{
    private const int MATCH = 1;
    private const int RUN_BONUS = 4;
    private const int WORD_START_BONUS = 6;
    private const int FIRST_CHARACTER_BONUS = 8;
    private const int GAP_PENALTY = 1;

    // Null when the text doesn't match; otherwise higher is better. An empty query matches everything with 0.
    public static int? Score(string query, string text)
    {
        string needle = query.Trim();
        if (needle.Length == 0)
            return 0;

        int score = 0;
        int position = 0;
        int previous = -2;
        foreach (char character in needle)
        {
            if (character == ' ')
                continue;

            int found = text.IndexOf(character.ToString(), position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return null;

            score += MATCH;
            bool wordStart = found > 0 && IsWordStart(text, found);
            if (found == previous + 1)
                score += RUN_BONUS;
            if (found == 0)
                score += FIRST_CHARACTER_BONUS;
            else if (wordStart)
                score += WORD_START_BONUS;

            // Jumping to the start of the next word is free; skipping letters inside a word is not
            if (previous >= 0 && !wordStart)
                score -= Math.Min(found - previous - 1, 10) * GAP_PENALTY;

            previous = found;
            position = found + 1;
        }

        // Shorter texts win ties: "Push" before "Push all tags"
        return score * 100 - Math.Min(text.Length, 99);
    }

    // Items that match, best first; ties keep their original order
    public static List<T> Filter<T>(IEnumerable<T> items, string query, Func<T, string> text)
    {
        return items.Select((item, index) => (Item: item, Index: index, Score: Score(query, text(item))))
                    .Where(entry => entry.Score != null)
                    .OrderByDescending(entry => entry.Score)
                    .ThenBy(entry => entry.Index)
                    .Select(entry => entry.Item)
                    .ToList();
    }

    private static bool IsWordStart(string text, int index)
    {
        char before = text[index - 1];
        return before is ' ' or '/' or '\\' or '-' or '_' or '.' or ':'
               || (char.IsLower(before) && char.IsUpper(text[index]));
    }
}
