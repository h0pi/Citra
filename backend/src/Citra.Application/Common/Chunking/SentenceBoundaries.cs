namespace Citra.Application.Common.Chunking;

/// <summary>
/// Finds where sentences (and list items, table rows, headings) start, so chunks can begin and end on whole
/// sentences. Deliberately careful rather than clever: "e.g. the", "3.5", "Dr. Smith", "J. Smith" and line
/// breaks inside a sentence (PDF lines wrap anywhere) are not boundaries.
/// </summary>
public static class SentenceBoundaries
{
    private const string EndPunctuation = ".!?…";

    /// <summary>Closing quotes and brackets that may follow a sentence's final punctuation: <c>He said "Yes."</c></summary>
    private const string Closers = "\"'”’»)]";

    /// <summary>Words that end with a dot but not a sentence (compared in lower case, without the dot).</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "mt", "no", "nos", "vs", "etc", "e.g", "i.e", "cf",
        "approx", "inc", "ltd", "co", "corp", "dept", "est", "fig", "figs", "vol", "ed", "p", "pp", "ch", "sec",
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
        // Bosnian / Croatian / Serbian
        "npr", "tj", "itd", "sl", "br", "str", "god", "ul", "mr", "dr", "ing", "dipl", "tzv", "odn", "čl", "st",
    };

    /// <summary>
    /// Positions in <paramref name="text"/> between <paramref name="start"/> (exclusive) and
    /// <paramref name="end"/> (exclusive) where a new sentence or structural line begins, in increasing order.
    /// Each position is the first non-whitespace character of the new sentence.
    /// </summary>
    public static List<int> Starts(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        var starts = new List<int>();

        for (var i = start; i < end; i++)
        {
            var c = text[i];
            int next;

            if (EndPunctuation.Contains(c))
            {
                var j = i + 1;
                while (j < end && EndPunctuation.Contains(text[j]))
                {
                    j++; // "?!", "..."
                }

                while (j < end && Closers.Contains(text[j]))
                {
                    j++;
                }

                if (j >= end || !char.IsWhiteSpace(text[j]))
                {
                    continue; // "3.5", "acme.com", "e.g.," — no space after, not an end
                }

                next = SkipWhitespace(text, j, end);
                if (next >= end || char.IsLower(text[next])
                    || (c == '.' && (IsAbbreviationOrInitial(text, i) || IsListNumber(text, i))))
                {
                    continue;
                }

                i = j - 1; // continue scanning after the punctuation run
            }
            else if (c == '\n')
            {
                next = SkipWhitespace(text, i + 1, end);
                if (next >= end || !(StartsStructure(text, next, end) || PreviousNonSpace(text, i, start) == ':'))
                {
                    continue; // a wrapped line inside a sentence
                }
            }
            else
            {
                continue;
            }

            if (next > start && next < end && (starts.Count == 0 || starts[^1] < next))
            {
                starts.Add(next);
            }
        }

        return starts;
    }

    /// <summary>"e.g.", "Dr.", "npr.", or a single letter ("J. Smith") right before the dot at <paramref name="dot"/>.</summary>
    private static bool IsAbbreviationOrInitial(string text, int dot)
    {
        var begin = dot;
        while (begin > 0 && (char.IsLetter(text[begin - 1]) || (text[begin - 1] == '.' && begin - 1 > 0 && char.IsLetter(text[begin - 2]))))
        {
            begin--;
        }

        var word = text[begin..dot];
        return word.Length == 1 ? char.IsUpper(word[0]) : Abbreviations.Contains(word);
    }

    /// <summary>
    /// The dot of a numbered-list marker ("1. First"): digits right before it that start their line.
    /// ("Price is 30. 25 are left." is still a sentence end: that number does not start a line.)
    /// </summary>
    private static bool IsListNumber(string text, int dot)
    {
        var begin = dot;
        while (begin > 0 && char.IsAsciiDigit(text[begin - 1]))
        {
            begin--;
        }

        if (begin == dot || dot - begin > 3)
        {
            return false;
        }

        while (begin > 0 && text[begin - 1] is ' ' or '\t')
        {
            begin--;
        }

        return begin == 0 || text[begin - 1] == '\n';
    }

    /// <summary>A list item, table row or heading starts at <paramref name="position"/>.</summary>
    private static bool StartsStructure(string text, int position, int end)
    {
        var line = text[position..LineEnd(text, position, end)];
        if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• ") || line.StartsWith('#'))
        {
            return true;
        }

        // "1. Item", "12) Item"
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        if (digits is > 0 and <= 3 && digits + 1 < line.Length && line[digits] is '.' or ')' && line[digits + 1] == ' ')
        {
            return true;
        }

        return line.Contains(" | ", StringComparison.Ordinal); // table row
    }

    private static int LineEnd(string text, int position, int end)
    {
        var newline = text.IndexOf('\n', position, end - position);
        return newline < 0 ? end : newline;
    }

    private static char PreviousNonSpace(string text, int position, int start)
    {
        for (var i = position - 1; i >= start; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return text[i];
            }
        }

        return '\0';
    }

    private static int SkipWhitespace(string text, int position, int end)
    {
        while (position < end && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
    }
}
