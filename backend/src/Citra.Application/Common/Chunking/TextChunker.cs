using System.Text.RegularExpressions;
using Citra.Application.Common.Extraction;
using Citra.Domain.Documents;

namespace Citra.Application.Common.Chunking;

/// <summary>
/// Splits a document's cleaned text into chunks of about <see cref="ChunkingOptions.TargetTokens"/> tokens for
/// search. It follows the document's structure: paragraphs (blocks separated by blank lines) are kept together,
/// and every heading ("# Title" lines, as the Word/web/Markdown readers produce them) starts a new chunk, so a
/// chunk never mixes two sections. Consecutive chunks of one section overlap by about
/// <see cref="ChunkingOptions.OverlapTokens"/> tokens, so a fact on the border between two chunks is still found
/// whole. A chunk never crosses a page, which keeps page numbers and character offsets exact for citations.
/// </summary>
public sealed partial class TextChunker(ITokenCounter tokens)
{
    /// <summary>Longest heading path kept (matches the Chunk.Heading column).</summary>
    public const int MaxHeadingLength = 300;

    private const string HeadingSeparator = " › ";

    /// <summary>
    /// Whether chunks of this kind of document get page numbers: PDFs and Word files have real pages;
    /// text files, Markdown and web pages are one continuous text (their chunks have no page number).
    /// </summary>
    public static bool HasPages(DocumentSourceType sourceType) =>
        sourceType is DocumentSourceType.Pdf or DocumentSourceType.Docx;

    /// <param name="document">Extracted and cleaned text (see <see cref="TextCleaner"/>).</param>
    /// <param name="hasPages">True for PDF/Word (chunks get page numbers), false for text files and web pages.</param>
    /// <param name="title">Document title put in front of every chunk for embedding (see <see cref="ChunkContext.DocumentTitle"/>).</param>
    public IReadOnlyList<TextChunk> Chunk(
        ExtractedDocument document, bool hasPages, string? title = null, ChunkingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= ChunkingOptions.Default;
        options.Validate();

        var run = new ChunkingRun(tokens, options, hasPages, title);
        foreach (var page in document.Pages)
        {
            run.AddPage(page);
        }

        return run.Chunks;
    }

    // "# Title" … "###### Title": the Markdown heading lines the extractors write.
    [GeneratedRegex(@"^(#{1,6})[ \t]+(\S.*?)[ \t#]*$")]
    private static partial Regex HeadingLine();

    // Blocks are separated by one or more blank lines.
    [GeneratedRegex(@"\n[ \t]*\n\s*")]
    private static partial Regex BlankLines();

    /// <summary>The state of one chunking pass: the current section, the chunk being filled, the previous chunk.</summary>
    private sealed class ChunkingRun(ITokenCounter tokens, ChunkingOptions options, bool hasPages, string? title)
    {
        /// <summary>Generous upper bound on characters per token (real tokens average about 4).</summary>
        private const int MaxCharsPerToken = 64;

        private readonly List<(int Level, string Text)> _headings = [];

        private ExtractedPage _page = null!;

        // The chunk being filled: page.Text[_start.._end]. Null when nothing is being filled.
        private int? _start;
        private int _end;

        /// <summary>The chunk being filled holds only heading lines so far (e.g. "# Handbook").</summary>
        private bool _onlyHeadings;

        /// <summary>The last emitted chunk, while the next one may overlap it (same page and section).</summary>
        private (int Start, int End)? _previous;

        public List<TextChunk> Chunks { get; } = [];

        public void AddPage(ExtractedPage page)
        {
            _page = page;
            _start = null;
            _previous = null; // no overlap across pages: a chunk's text must come from one page

            foreach (var (start, end) in Blocks(page.Text))
            {
                var firstLineEnd = page.Text.IndexOf('\n', start, end - start);
                var firstLine = page.Text[start..(firstLineEnd < 0 ? end : firstLineEnd)];
                var heading = HeadingLine().Match(firstLine);
                if (heading.Success)
                {
                    // A new section starts a new chunk, unless the chunk so far is only headings
                    // ("# Handbook" directly followed by "## Vacation"): those stay with the section they introduce.
                    if (!_onlyHeadings)
                    {
                        Flush();
                    }

                    _previous = null; // no overlap across sections
                    EnterSection(heading.Groups[1].Length, heading.Groups[2].Value.Trim());
                }

                var startedNewChunk = AddBlock(start, end);
                var isHeadingOnly = heading.Success && firstLineEnd < 0;
                _onlyHeadings = _start is not null && isHeadingOnly && (startedNewChunk || _onlyHeadings);
            }

            Flush();
        }

        /// <summary>Adds a block to the chunk being filled. Returns true if the block started a new chunk.</summary>
        private bool AddBlock(int start, int end)
        {
            if (tokens.Count(_page.Text[start..end]) > options.MaxTokens)
            {
                Flush();
                SplitOversizedBlock(start, end);
                return true;
            }

            if (_start is not null && tokens.Count(_page.Text[_start.Value..end]) <= options.TargetTokens)
            {
                _end = end;
                return false;
            }

            Flush();

            // Start the new chunk with the tail of the previous one, if that still fits the maximum.
            var chunkStart = start;
            if (OverlapStart(start) is { } tail && tokens.Count(_page.Text[tail..end]) <= options.MaxTokens)
            {
                chunkStart = tail;
            }

            (_start, _end) = (chunkStart, end);
            return true;
        }

        /// <summary>
        /// A block too big for one chunk (e.g. a whole PDF page, which has no blank lines): cut into pieces of about
        /// the target size, always between sentences. Each piece after the first starts with the previous piece's
        /// last sentences (overlap) and then continues with new text.
        /// </summary>
        private void SplitOversizedBlock(int start, int end)
        {
            var sentenceStarts = SentenceBoundaries.Starts(_page.Text, start, end);
            var position = start; // where the text not yet in any chunk begins
            while (position < end)
            {
                var chunkStart = OverlapStart(position) ?? position;
                var limit = chunkStart + tokens.IndexAfterTokens(Window(chunkStart, end, options.TargetTokens), options.TargetTokens);

                if (limit <= position)
                {
                    // The overlap would leave no room for new text (cannot happen with sane sizes): skip it.
                    chunkStart = position;
                    limit = position + tokens.IndexAfterTokens(Window(position, end, options.TargetTokens), options.TargetTokens);
                    if (limit <= position)
                    {
                        limit = end; // a real tokenizer always fits one token; never loop forever
                    }
                }

                var cut = limit >= end ? end : CutBetweenSentences(sentenceStarts, chunkStart, position, limit, end);

                (_start, _end) = (chunkStart, cut);
                Flush();
                position = SkipWhitespace(cut, end);
            }
        }

        /// <summary>
        /// Where to end a piece that would reach <paramref name="limit"/> (the target size): at the last sentence
        /// start before it; or, if the piece is one long sentence, at its end as long as that stays within the
        /// maximum size; only a single sentence longer than the maximum is cut at a space.
        /// </summary>
        private int CutBetweenSentences(List<int> sentenceStarts, int chunkStart, int position, int limit, int end)
        {
            if (LastStartIn(sentenceStarts, position, limit) is { } sentenceStart)
            {
                return sentenceStart;
            }

            var maxLimit = chunkStart + tokens.IndexAfterTokens(Window(chunkStart, end, options.MaxTokens), options.MaxTokens);
            if (FirstStartIn(sentenceStarts, position, maxLimit) is { } afterLongSentence)
            {
                return afterLongSentence;
            }

            if (maxLimit >= end)
            {
                return end; // the rest is one sentence that fits the maximum
            }

            // One sentence longer than the maximum: no clean place exists, so cut at a space near the target.
            var space = _page.Text.LastIndexOfAny([' ', '\n', '\t'], limit - 1, limit - position);
            return space > position + ((limit - position) * 2 / 3) ? space : limit;
        }

        /// <summary>
        /// The text from <paramref name="from"/> that can hold <paramref name="maxTokens"/> tokens, not the whole rest
        /// of the block: a huge block (a 20 MB text file) would otherwise be copied again for every piece. A token is
        /// never close to <see cref="MaxCharsPerToken"/> characters, so the window always holds enough tokens.
        /// </summary>
        private string Window(int from, int end, int maxTokens) =>
            _page.Text[from..Math.Min(end, from + (maxTokens * MaxCharsPerToken))];

        /// <summary>The last sentence start in (from, to].</summary>
        private static int? LastStartIn(List<int> starts, int from, int to)
        {
            for (var i = starts.Count - 1; i >= 0; i--)
            {
                if (starts[i] <= to)
                {
                    return starts[i] > from ? starts[i] : null;
                }
            }

            return null;
        }

        /// <summary>The first sentence start in (from, to].</summary>
        private static int? FirstStartIn(List<int> starts, int from, int to)
        {
            foreach (var start in starts)
            {
                if (start > from)
                {
                    return start <= to ? start : null;
                }
            }

            return null;
        }

        /// <summary>
        /// Where a new chunk starting at <paramref name="newTextStart"/> should begin to repeat the end of the
        /// previous chunk: its last whole sentences, about <see cref="ChunkingOptions.OverlapTokens"/> tokens
        /// (up to twice that if the last sentence is long). Never all of the previous chunk, never mid-sentence.
        /// Null when there is nothing to overlap (first chunk, new page or section, or no sentence short enough).
        /// </summary>
        private int? OverlapStart(int newTextStart)
        {
            if (options.OverlapTokens == 0 || _previous is not { } previous || previous.End > newTextStart)
            {
                return null;
            }

            var tail = previous.Start
                + tokens.IndexBeforeLastTokens(_page.Text[previous.Start..previous.End], options.OverlapTokens);
            var sentenceStarts = SentenceBoundaries.Starts(_page.Text, previous.Start, previous.End);

            // The first sentence that starts inside the last ~80 tokens...
            if (FirstStartIn(sentenceStarts, tail - 1, previous.End - 1) is { } within)
            {
                return within;
            }

            // ...or, if the final sentence is longer than that, its start while the overlap stays reasonable.
            if (LastStartIn(sentenceStarts, previous.Start, tail - 1) is { } longer
                && tokens.Count(_page.Text[longer..previous.End]) <= 2 * options.OverlapTokens)
            {
                return longer;
            }

            return null;
        }

        private void EnterSection(int level, string title)
        {
            while (_headings.Count > 0 && _headings[^1].Level >= level)
            {
                _headings.RemoveAt(_headings.Count - 1);
            }

            _headings.Add((level, title));
        }

        /// <summary>Emits the chunk being filled (if any), trimmed to non-blank text.</summary>
        private void Flush()
        {
            if (_start is not { } start)
            {
                return;
            }

            var end = _end;
            _start = null;
            _onlyHeadings = false;

            start = SkipWhitespace(start, end);
            while (end > start && char.IsWhiteSpace(_page.Text[end - 1]))
            {
                end--;
            }

            if (start == end)
            {
                return;
            }

            var content = _page.Text[start..end];
            var heading = HeadingPath();
            Chunks.Add(new TextChunk(
                Chunks.Count,
                content,
                heading,
                hasPages ? _page.PageNumber : null,
                start,
                end,
                tokens.Count(content),
                ChunkContext.ForEmbedding(title, heading, content)));
            _previous = (start, end);
        }

        private string? HeadingPath()
        {
            if (_headings.Count == 0)
            {
                return null;
            }

            var path = string.Join(HeadingSeparator, _headings.Select(h => h.Text));
            return path.Length <= MaxHeadingLength ? path : path[..(MaxHeadingLength - 1)].TrimEnd() + "…";
        }

        private int SkipWhitespace(int position, int end)
        {
            while (position < end && char.IsWhiteSpace(_page.Text[position]))
            {
                position++;
            }

            return position;
        }

        /// <summary>(start, end) of each non-blank block of the page, in order, trimmed of surrounding whitespace.</summary>
        private static IEnumerable<(int Start, int End)> Blocks(string text)
        {
            var position = 0;
            foreach (Match separator in BlankLines().Matches(text))
            {
                if (Trimmed(text, position, separator.Index) is { } block)
                {
                    yield return block;
                }

                position = separator.Index + separator.Length;
            }

            if (Trimmed(text, position, text.Length) is { } last)
            {
                yield return last;
            }
        }

        private static (int Start, int End)? Trimmed(string text, int start, int end)
        {
            while (start < end && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }

            return start < end ? (start, end) : null;
        }
    }
}
