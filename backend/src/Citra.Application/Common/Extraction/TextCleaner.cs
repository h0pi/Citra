using System.Text;
using System.Text.RegularExpressions;

namespace Citra.Application.Common.Extraction;

/// <summary>
/// Tidies extracted text before chunking, so answers and citations read like the original document:
/// no invisible characters, no words split by line-end hyphens, no runs of spaces or blank lines, and no
/// running headers/footers ("ACME Handbook — Page 3 of 40") repeated on every page.
/// Page numbers are kept, even for pages that end up empty.
/// </summary>
public static partial class TextCleaner
{
    /// <summary>A line counts as a running header/footer if it repeats on this share of pages...</summary>
    private const double RepeatedLineShare = 0.6;

    /// <summary>...and the document has at least this many pages.</summary>
    private const int MinPagesForRepeatedLines = 3;

    /// <summary>Only the first/last lines of a page are header/footer candidates.</summary>
    private const int EdgeLines = 2;

    private const int MaxHeaderLength = 120;

    public static ExtractedDocument Clean(ExtractedDocument document)
    {
        var pages = document.Pages.Select(p => p with { Text = Normalize(p.Text) }).ToList();
        pages = RemoveRepeatedEdgeLines(pages);
        return document with { Pages = pages };
    }

    /// <summary>Character-level and line-level cleanup of one page.</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            switch (c)
            {
                case '\r':
                case '­': // soft hyphen: invisible, only a line-break hint
                case '​': // zero-width space
                case '﻿': // byte-order mark / zero-width no-break space
                    continue;
                case 'ﬀ': builder.Append("ff"); continue;
                case 'ﬁ': builder.Append("fi"); continue;
                case 'ﬂ': builder.Append("fl"); continue;
                case 'ﬃ': builder.Append("ffi"); continue;
                case 'ﬄ': builder.Append("ffl"); continue;
                case '\n':
                case '\t':
                    builder.Append(c);
                    continue;
            }

            if (char.IsControl(c))
            {
                continue;
            }

            // Non-breaking, thin, em... spaces all become a normal space.
            builder.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        var result = builder.ToString();
        result = HyphenatedLineBreak().Replace(result, "$1$2");
        result = SpacesAndTabs().Replace(result, " ");
        result = SpaceAroundNewline().Replace(result, "\n");
        result = ThreeOrMoreNewlines().Replace(result, "\n\n");
        return result.Trim();
    }

    /// <summary>
    /// Removes running headers/footers: lines at the top or bottom of most pages that are exactly the same
    /// ("ACME Employee Handbook"), and page-number lines at a page's edge ("7", "Page 7 of 40",
    /// "Handbook — Page 7"). Digits are never masked for the "same line" test: that made body lines like
    /// "Section 3" and "Section 4" look identical and deleted real content. A page never becomes empty this way.
    /// </summary>
    private static List<ExtractedPage> RemoveRepeatedEdgeLines(List<ExtractedPage> pages)
    {
        var linesPerPage = pages.Select(p => p.Text.Split('\n').ToList()).ToList();

        var repeated = new HashSet<string>();
        if (pages.Count >= MinPagesForRepeatedLines)
        {
            var threshold = Math.Max(MinPagesForRepeatedLines, (int)Math.Ceiling(pages.Count * RepeatedLineShare));
            repeated = linesPerPage
                .SelectMany(lines => EdgeIndexes(lines).Select(i => lines[i].Trim()).Distinct())
                .Where(key => key.Length is > 0 and <= MaxHeaderLength)
                .GroupBy(key => key)
                .Where(g => g.Count() >= threshold)
                .Select(g => g.Key)
                .ToHashSet();
        }

        var cleaned = new List<ExtractedPage>(pages.Count);
        for (var p = 0; p < pages.Count; p++)
        {
            var lines = linesPerPage[p];
            var drop = EdgeIndexes(lines)
                .Where(i => repeated.Contains(lines[i].Trim())
                    || (pages.Count > 1 && IsPageNumberLine(lines[i]))) // "2025" ending a text file stays
                .ToHashSet();

            var kept = lines.Where((_, i) => !drop.Contains(i));
            var text = ThreeOrMoreNewlines().Replace(string.Join('\n', kept), "\n\n").Trim();

            // Safety net: if everything on a page looked like header/footer, the guess was wrong; keep the page.
            cleaned.Add(pages[p] with { Text = text.Length == 0 ? pages[p].Text : text });
        }

        return cleaned;
    }

    /// <summary>Indexes of the first and last <see cref="EdgeLines"/> non-empty lines of a page.</summary>
    private static IEnumerable<int> EdgeIndexes(List<string> lines)
    {
        var nonEmpty = Enumerable.Range(0, lines.Count).Where(i => lines[i].Trim().Length > 0).ToList();
        return nonEmpty.Take(EdgeLines).Concat(nonEmpty.TakeLast(EdgeLines)).Distinct();
    }

    private static bool IsPageNumberLine(string line) =>
        line.Length <= MaxHeaderLength && (PageNumberLine().IsMatch(line) || LabelledPageNumberLine().IsMatch(line));

    // "infor-\nmation" → "information" (only between lower-case letters, so "Covid-\n19" or "e-\nMail" stay).
    [GeneratedRegex(@"(\p{Ll})-\n(\p{Ll})")]
    private static partial Regex HyphenatedLineBreak();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex SpacesAndTabs();

    [GeneratedRegex(@" ?\n ?")]
    private static partial Regex SpaceAroundNewline();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ThreeOrMoreNewlines();

    // "7", "- 7 -", "Page 7", "Page 7 of 40", "7 / 40", "Strana 7 od 40"
    [GeneratedRegex(@"^\s*[-–—]?\s*((page|p\.|strana|stranica)\s*)?\d{1,4}(\s*(of|/|od)\s*\d{1,4})?\s*[-–—]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumberLine();

    // "ACME Handbook — Page 7", "Confidential | page 7 of 40", "Pravilnik, strana 7 od 40"
    [GeneratedRegex(@"\b(page|strana|stranica)\s*\d{1,4}(\s*(of|/|od)\s*\d{1,4})?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LabelledPageNumberLine();
}
