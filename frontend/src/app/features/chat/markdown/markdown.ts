import { lexer, MarkedToken, Token } from 'marked';

/** A piece of text inside a block: words, emphasis, code, a link, a line break, a citation marker. */
export type MarkdownInline =
  | { kind: 'text'; text: string }
  | { kind: 'citation'; text: string; numbers: number[] }
  | { kind: 'strong' | 'em' | 'del'; content: MarkdownInline[] }
  | { kind: 'code'; text: string }
  | { kind: 'link'; href: string; content: MarkdownInline[] }
  | { kind: 'break' };

/** A block of an answer: paragraph, heading, list, table, code, quote or line. */
export type MarkdownBlock =
  | { kind: 'paragraph'; content: MarkdownInline[] }
  | { kind: 'heading'; content: MarkdownInline[] }
  | { kind: 'code'; text: string }
  | { kind: 'list'; ordered: boolean; start: number; items: MarkdownListItem[] }
  | { kind: 'table'; align: TableAlign[]; header: MarkdownInline[][]; rows: MarkdownInline[][][] }
  | { kind: 'quote'; blocks: MarkdownBlock[] }
  | { kind: 'rule' };

export interface MarkdownListItem {
  /** A task list item ("- [x] done") is checked or not; other items have null. */
  checked: boolean | null;
  blocks: MarkdownBlock[];
}

export type TableAlign = 'left' | 'center' | 'right' | null;

/** Same markers as the API reads (CitationMarkers): "[1]", "[2][3]" (two markers) and "[1, 2]". */
const CITATION_MARKER = /\[(\d{1,3}(?:\s*,\s*\d{1,3})*)\]/g;

/** Links open only to the web or e-mail; anything else (javascript:, data:, relative paths) stays plain text. */
const SAFE_LINK = /^(https?:|mailto:)/i;

/**
 * Reads an answer written in Markdown (GitHub flavour, single line breaks kept) into blocks the page shows with
 * its own elements. Nothing is ever inserted as HTML: HTML the model writes is shown as the text it is, so an
 * answer cannot run scripts or change the page. Works on a half-written answer too: what is not finished yet
 * (an open "**", a table without its second line) shows as plain text until it is.
 */
export function parseMarkdown(markdown: string): MarkdownBlock[] {
  return blocks(lexer(markdown, { gfm: true, breaks: true }));
}

function blocks(tokens: Token[]): MarkdownBlock[] {
  return (tokens as MarkedToken[]).flatMap((token): MarkdownBlock[] => {
    switch (token.type) {
      case 'paragraph':
      case 'heading':
        return [{ kind: token.type, content: inlines(token.tokens) }];
      case 'text':
        // Text directly in a list item (a "tight" list) is a paragraph without the spacing.
        return [
          { kind: 'paragraph', content: token.tokens ? inlines(token.tokens) : textOf(token.text) },
        ];
      case 'code':
        return [{ kind: 'code', text: token.text }];
      case 'list':
        return [
          {
            kind: 'list',
            ordered: token.ordered,
            start: typeof token.start === 'number' ? token.start : 1,
            items: token.items.map((item) => ({
              checked: item.task ? !!item.checked : null,
              blocks: blocks(item.tokens.filter((child) => child.type !== 'checkbox')),
            })),
          },
        ];
      case 'table':
        return [
          {
            kind: 'table',
            align: token.align,
            header: token.header.map((cell) => inlines(cell.tokens)),
            rows: token.rows.map((row) => row.map((cell) => inlines(cell.tokens))),
          },
        ];
      case 'blockquote':
        return [{ kind: 'quote', blocks: blocks(token.tokens) }];
      case 'hr':
        return [{ kind: 'rule' }];
      case 'space':
        return [];
      default:
        // HTML, a link definition ("[1]: …") or anything unknown: shown as the text that was written.
        return token.raw.trim() ? [{ kind: 'paragraph', content: textOf(token.raw.trim()) }] : [];
    }
  });
}

/** Citation markers are found in text, but not in code or in a link's text (a link cannot hold a button). */
function inlines(tokens: Token[] | undefined, citations = true): MarkdownInline[] {
  const result: MarkdownInline[] = [];
  for (const token of (tokens ?? []) as MarkedToken[]) {
    for (const piece of inline(token, citations)) {
      // Neighbouring text is joined, so "[1]" split by the parser reads as one piece again.
      const last = result.at(-1);
      if (piece.kind === 'text' && last?.kind === 'text') {
        last.text += piece.text;
      } else if (piece.kind !== 'text' || piece.text) {
        result.push(piece);
      }
    }
  }
  return citations ? result.flatMap(withCitations) : result;
}

/** Splits citation markers out of a text piece. */
function withCitations(piece: MarkdownInline): MarkdownInline[] {
  if (piece.kind !== 'text') {
    return [piece];
  }

  const result: MarkdownInline[] = [];
  let at = 0;
  for (const marker of piece.text.matchAll(CITATION_MARKER)) {
    if (marker.index > at) {
      result.push({ kind: 'text', text: piece.text.slice(at, marker.index) });
    }
    const numbers = marker[1].split(',').map((n) => Number(n.trim()));
    result.push({ kind: 'citation', text: marker[0], numbers });
    at = marker.index + marker[0].length;
  }
  if (at < piece.text.length) {
    result.push({ kind: 'text', text: piece.text.slice(at) });
  }
  return result;
}

function inline(token: MarkedToken, citations: boolean): MarkdownInline[] {
  switch (token.type) {
    case 'text':
      return token.tokens ? inlines(token.tokens, citations) : textOf(token.text);
    case 'escape':
      return textOf(token.text);
    case 'strong':
    case 'em':
    case 'del':
      return [{ kind: token.type, content: inlines(token.tokens, citations) }];
    case 'codespan':
      return [{ kind: 'code', text: token.text }];
    case 'br':
      return [{ kind: 'break' }];
    case 'link':
      return SAFE_LINK.test(token.href)
        ? [{ kind: 'link', href: token.href, content: inlines(token.tokens, false) }]
        : inlines(token.tokens, citations);
    case 'image':
      // Answers show no pictures from outside; the description stands in for it.
      return textOf(token.text);
    default:
      // HTML tags and anything unknown: the text as written.
      return textOf(token.raw);
  }
}

function textOf(text: string): MarkdownInline[] {
  return [{ kind: 'text', text }];
}
