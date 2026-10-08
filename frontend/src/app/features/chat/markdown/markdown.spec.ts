import { MarkdownBlock, parseMarkdown } from './markdown';

const text = (value: string) => ({ kind: 'text', text: value });
const paragraph = (...content: unknown[]) => ({ kind: 'paragraph', content });
const citation = (marker: string, ...numbers: number[]) => ({
  kind: 'citation',
  text: marker,
  numbers,
});

describe('parseMarkdown', () => {
  it('reads emphasis and code', () => {
    expect(parseMarkdown('You get **25 days** of *paid* ~~unpaid~~ leave `per year`.')).toEqual([
      paragraph(
        text('You get '),
        { kind: 'strong', content: [text('25 days')] },
        text(' of '),
        { kind: 'em', content: [text('paid')] },
        text(' '),
        { kind: 'del', content: [text('unpaid')] },
        text(' leave '),
        { kind: 'code', text: 'per year' },
        text('.'),
      ),
    ]);
  });

  it('finds citation markers as the API reads them: [1], [2][3] and [1, 2]', () => {
    expect(parseMarkdown('Vacation [1]. Carry-over [2][3], see [4, 5 ,6]. [12]')).toEqual([
      paragraph(
        text('Vacation '),
        citation('[1]', 1),
        text('. Carry-over '),
        citation('[2]', 2),
        citation('[3]', 3),
        text(', see '),
        citation('[4, 5 ,6]', 4, 5, 6),
        text('. '),
        citation('[12]', 12),
      ),
    ]);
  });

  it('finds citation markers inside emphasis, lists and tables', () => {
    expect(parseMarkdown('**25 days [1]**\n\n- Apply [2]\n\n| Days |\n|--|\n| 25 [3] |')).toEqual([
      paragraph({ kind: 'strong', content: [text('25 days '), citation('[1]', 1)] }),
      {
        kind: 'list',
        ordered: false,
        start: 1,
        items: [{ checked: null, blocks: [paragraph(text('Apply '), citation('[2]', 2))] }],
      },
      {
        kind: 'table',
        align: [null],
        header: [[text('Days')]],
        rows: [[[text('25 '), citation('[3]', 3)]]],
      },
    ]);
  });

  it('leaves text that only looks like a marker, code and link text as written', () => {
    expect(
      parseMarkdown(
        'Not [a], [] or [1234] or [1,] or [ 1]. `x[1]` [see [2]](https://example.com)\n\n```\nlist[3]\n```',
      ),
    ).toEqual([
      paragraph(
        text('Not [a], [] or [1234] or [1,] or [ 1]. '),
        { kind: 'code', text: 'x[1]' },
        text(' '),
        { kind: 'link', href: 'https://example.com', content: [text('see [2]')] },
      ),
      { kind: 'code', text: 'list[3]' },
    ]);
  });

  it('keeps single line breaks and separates paragraphs', () => {
    expect(parseMarkdown('Line one\nline two\n\nNext paragraph')).toEqual([
      paragraph(text('Line one'), { kind: 'break' }, text('line two')),
      paragraph(text('Next paragraph')),
    ]);
  });

  it('reads nested lists, numbered lists from their first number, and task lists', () => {
    expect(
      parseMarkdown('- Apply\n  - online\n- Wait\n\n3. third\n4. fourth\n\n- [x] done\n- [ ] open'),
    ).toEqual([
      {
        kind: 'list',
        ordered: false,
        start: 1,
        items: [
          {
            checked: null,
            blocks: [
              paragraph(text('Apply')),
              {
                kind: 'list',
                ordered: false,
                start: 1,
                items: [{ checked: null, blocks: [paragraph(text('online'))] }],
              },
            ],
          },
          { checked: null, blocks: [paragraph(text('Wait'))] },
        ],
      },
      {
        kind: 'list',
        ordered: true,
        start: 3,
        items: [
          { checked: null, blocks: [paragraph(text('third'))] },
          { checked: null, blocks: [paragraph(text('fourth'))] },
        ],
      },
      {
        kind: 'list',
        ordered: false,
        start: 1,
        items: [
          { checked: true, blocks: [paragraph(text('done'))] },
          { checked: false, blocks: [paragraph(text('open'))] },
        ],
      },
    ]);
  });

  it('reads tables with their alignment', () => {
    expect(parseMarkdown('| Plan | Days |\n|:--|--:|\n| Basic | **20** |\n| Plus | 25 |')).toEqual([
      {
        kind: 'table',
        align: ['left', 'right'],
        header: [[text('Plan')], [text('Days')]],
        rows: [
          [[text('Basic')], [{ kind: 'strong', content: [text('20')] }]],
          [[text('Plus')], [text('25')]],
        ],
      },
    ]);
  });

  it('reads code blocks, headings, quotes and lines', () => {
    expect(parseMarkdown('## Steps\n\n```js\nif (a < b) {}\n```\n\n> Note\n\n---')).toEqual([
      { kind: 'heading', content: [text('Steps')] },
      { kind: 'code', text: 'if (a < b) {}' },
      { kind: 'quote', blocks: [paragraph(text('Note'))] },
      { kind: 'rule' },
    ]);
  });

  it('shows HTML as the text it is', () => {
    expect(
      parseMarkdown(
        '<div onclick="x()">Hi</div>\n\nA <b>bold</b> <img src=x onerror=alert(1)> try',
      ),
    ).toEqual([
      paragraph(text('<div onclick="x()">Hi</div>')),
      paragraph(text('A <b>bold</b> <img src=x onerror=alert(1)> try')),
    ]);
  });

  it('links only to the web and e-mail, and shows no pictures', () => {
    expect(
      parseMarkdown(
        '[Site](https://example.com) [Mail](mailto:hr@example.com) [Bad](javascript:alert(1)) [Data](data:text/html,x) [Page](/admin) ![Logo](https://example.com/logo.png)',
      ),
    ).toEqual([
      paragraph(
        { kind: 'link', href: 'https://example.com', content: [text('Site')] },
        text(' '),
        { kind: 'link', href: 'mailto:hr@example.com', content: [text('Mail')] },
        text(' Bad Data Page Logo'),
      ),
    ]);
  });

  it('shows escaped characters without their backslash', () => {
    expect(parseMarkdown('Not \\*bold\\*, 5 \\< 6')).toEqual([
      paragraph(text('Not *bold*, 5 < 6')),
    ]);
  });

  it('shows a link definition as written', () => {
    expect(parseMarkdown('[1]: https://example.com/handbook')).toEqual([
      paragraph(text('[1]: https://example.com/handbook')),
    ]);
  });

  it('shows a half-written answer as far as it goes', () => {
    expect(parseMarkdown('This is **impor')).toEqual([paragraph(text('This is **impor'))]);
    expect(parseMarkdown('| Plan | Days |')).toEqual([paragraph(text('| Plan | Days |'))]);
    expect(parseMarkdown('Run:\n\n```sh\nnpm inst')).toEqual<MarkdownBlock[]>([
      paragraph(text('Run:')) as MarkdownBlock,
      { kind: 'code', text: 'npm inst' },
    ]);
  });

  it('gives nothing for an empty answer', () => {
    expect(parseMarkdown('')).toEqual([]);
    expect(parseMarkdown('  \n\n ')).toEqual([]);
  });
});
