import { TestBed } from '@angular/core/testing';
import { Citation } from '@core/models/chat.models';
import { SNIPPET_PREVIEW_LENGTH, SourcesPanel } from './sources-panel';

function citation(number: number, overrides: Partial<Citation> = {}): Citation {
  return {
    number,
    chunkId: `k${number}`,
    documentId: 'd1',
    documentName: 'Handbook.pdf',
    pageNumber: number,
    snippet: `Passage ${number}`,
    score: 0.03,
    ...overrides,
  };
}

describe('SourcesPanel', () => {
  let scrolled: Element[];

  beforeEach(() => {
    scrolled = [];
    Element.prototype.scrollIntoView = vi.fn(function (this: Element) {
      scrolled.push(this);
    });
  });

  async function render(sources: Citation[] = [], selected: number | null = null) {
    await TestBed.configureTestingModule({ imports: [SourcesPanel] }).compileComponents();
    const fixture = TestBed.createComponent(SourcesPanel);
    document.body.appendChild(fixture.nativeElement); // focus needs the page
    const set = async (next: Citation[], nextSelected: number | null = null) => {
      fixture.componentRef.setInput('sources', next);
      fixture.componentRef.setInput('selected', nextSelected);
      await fixture.whenStable();
    };
    await set(sources, selected);
    const el: HTMLElement = fixture.nativeElement;
    const items = () => [...el.querySelectorAll<HTMLElement>('li.source')];
    const more = () => el.querySelector<HTMLButtonElement>('button.more');
    return { fixture, el, set, items, more };
  }

  afterEach(() => document.body.replaceChildren());

  it('explains what appears here while there are no sources', async () => {
    const { el, items } = await render();

    expect(el.querySelector('h2')!.textContent).toBe('Sources');
    expect(el.textContent).toContain('The passages an answer is based on appear here');
    expect(items()).toHaveLength(0);
  });

  it('lists each source with its number, document, page and passage', async () => {
    const { el, items } = await render([
      citation(1, { snippet: 'Line one\nLine two' }),
      citation(3, { documentName: 'Leave policy.md', pageNumber: null }),
    ]);

    expect(el.querySelector('.hint')).toBeNull();
    expect(
      items().map((item) => [
        item.getAttribute('data-number'),
        item.querySelector('.number')!.textContent,
        item.querySelector('.document')!.textContent,
        item.querySelector('.page')?.textContent ?? null,
        item.querySelector('.snippet')!.textContent,
      ]),
    ).toEqual([
      ['1', '1', 'Handbook.pdf', 'Page 1', 'Line one\nLine two'],
      ['3', '3', 'Leave policy.md', null, 'Passage 3'],
    ]);
    expect(items()[1].querySelector('.document')!.getAttribute('title')).toBe('Leave policy.md');
    expect(el.querySelector('ol.source-list')).not.toBeNull();
  });

  it('picks out, scrolls to and focuses the chosen source', async () => {
    const { items } = await render([citation(1), citation(2)], 2);

    expect(items().map((item) => item.classList.contains('chosen'))).toEqual([false, true]);
    expect(items().map((item) => item.getAttribute('aria-current'))).toEqual([null, 'true']);
    expect(scrolled).toEqual([items()[1]]);
    expect(document.activeElement).toBe(items()[1]);
    expect(items()[1].tabIndex).toBe(-1);
  });

  it('scrolls to the source again when the same one is chosen again', async () => {
    const sources = [citation(1), citation(2)];
    const { set, items } = await render(sources, 1);
    items()[1].focus();

    await set([...sources], 1);

    expect(scrolled).toEqual([items()[0], items()[0]]);
    expect(document.activeElement).toBe(items()[0]);
  });

  it('moves nothing when no source is chosen', async () => {
    const { items } = await render([citation(1)]);

    expect(scrolled).toEqual([]);
    expect(document.activeElement).not.toBe(items()[0]);
    expect(items()[0].classList).not.toContain('chosen');
  });

  it('shows the start of a long passage, the whole of it on request', async () => {
    const long = 'abcdef '.repeat(70).trim(); // 489 characters; the preview ends inside a word
    const { fixture, set, items, more } = await render([
      citation(1, { snippet: long }),
      citation(2, { snippet: 'Short' }),
    ]);
    const snippet = () => items()[0].querySelector('.snippet')!.textContent!;

    expect(snippet()).toBe(long.slice(0, 314) + '…'); // the cut-off word is left out
    expect(more()!.textContent!.trim()).toBe('Show more');
    expect(more()!.getAttribute('aria-expanded')).toBe('false');
    expect(items()[1].querySelector('button.more')).toBeNull();

    more()!.click();
    await fixture.whenStable();
    expect(snippet()).toBe(long);
    expect(more()!.textContent!.trim()).toBe('Show less');
    expect(more()!.getAttribute('aria-expanded')).toBe('true');

    more()!.click();
    await fixture.whenStable();
    expect(snippet()).toBe(long.slice(0, 314) + '…');

    // Shown in full, then the same sources again (a chip chosen): still in full.
    more()!.click();
    await fixture.whenStable();
    await set([citation(1, { snippet: long }), citation(2, { snippet: 'Short' })], 2);
    expect(snippet()).toBe(long);
    more()!.click();
    await fixture.whenStable();

    // Shown in full, then the sources of another answer (as many, same numbers): short again.
    more()!.click();
    await fixture.whenStable();
    await set([
      citation(1, { snippet: long, chunkId: 'other1' }),
      citation(2, { snippet: 'Short', chunkId: 'other2' }),
    ]);
    expect(snippet()).toBe(long.slice(0, 314) + '…');
  });

  it('keeps a passage of exactly the preview length whole, and cuts a long word', async () => {
    const exact = 'x'.repeat(SNIPPET_PREVIEW_LENGTH);
    const oneWord = 'y'.repeat(SNIPPET_PREVIEW_LENGTH + 50);
    const { items, more } = await render([
      citation(1, { snippet: exact }),
      citation(2, { snippet: oneWord }),
    ]);

    expect(items()[0].querySelector('.snippet')!.textContent).toBe(exact);
    expect(items()[0].querySelector('button.more')).toBeNull();
    expect(items()[1].querySelector('.snippet')!.textContent).toBe(
      'y'.repeat(SNIPPET_PREVIEW_LENGTH) + '…',
    );
    expect(more()).not.toBeNull();
  });

  it('offers to open the original of documents that still exist', async () => {
    const pdf = citation(1, { documentName: 'Handbook.PDF' });
    const { fixture, items } = await render([
      pdf,
      citation(2, { documentName: 'Policy.docx' }),
      citation(3, { documentId: null }),
    ]);
    const opened: Citation[] = [];
    fixture.componentInstance.open.subscribe((source) => opened.push(source));
    const openButton = (item: HTMLElement) =>
      [...item.querySelectorAll('button')].find((b) => b.textContent!.includes('Open document'));

    expect(items().map((item) => !!openButton(item))).toEqual([true, true, false]);
    openButton(items()[0])!.click();
    expect(opened).toEqual([pdf]);
  });

  it('asks the page to close it', async () => {
    const { fixture, el } = await render([citation(1)]);
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    el.querySelector<HTMLButtonElement>('p-button[ariaLabel="Close sources"] button')!.click();

    expect(closed).toBe(1);
  });
});
