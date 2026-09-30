import { TestBed } from '@angular/core/testing';
import { DocumentsApi } from '@core/documents/documents-api';
import { CitraDocument, DocumentChunk, PagedResult } from '@core/models/document.models';
import { Observable, Subject, of, throwError } from 'rxjs';

import { CHUNKS_PAGE_SIZE, DocumentDrawer } from './document-drawer';

function doc(overrides: Partial<CitraDocument> = {}): CitraDocument {
  return {
    id: 'd1',
    botId: 'b1',
    fileName: 'Employee handbook.pdf',
    contentType: 'application/pdf',
    sizeBytes: 12_700,
    sourceType: 'Pdf',
    sourceUrl: null,
    status: 'Ready',
    errorMessage: null,
    pageCount: 6,
    chunkCount: 3,
    createdAt: '2026-09-29T09:05:00Z',
    updatedAt: '2026-09-29T09:05:00Z',
    processedAt: '2026-09-29T09:05:03Z',
    ...overrides,
  };
}

function chunk(index: number, overrides: Partial<DocumentChunk> = {}): DocumentChunk {
  return {
    id: `c${index}`,
    index,
    pageNumber: index + 1,
    heading: 'Vacation',
    content: `Chunk ${index}: employees get 25 days of paid vacation.`,
    tokenCount: 12,
    charStart: 0,
    charEnd: 50,
    ...overrides,
  };
}

function page(items: DocumentChunk[], totalCount = items.length): PagedResult<DocumentChunk> {
  return { items, page: 1, pageSize: CHUNKS_PAGE_SIZE, totalCount, totalPages: 1 };
}

describe('DocumentDrawer', () => {
  const chunks =
    vi.fn<(id: string, page: number, size: number) => Observable<PagedResult<DocumentChunk>>>();

  beforeEach(async () => {
    chunks.mockReset();
    window.matchMedia ??= ((query: string) =>
      ({
        matches: false,
        media: query,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
      }) as unknown as MediaQueryList) as typeof window.matchMedia;
    await TestBed.configureTestingModule({
      imports: [DocumentDrawer],
      providers: [{ provide: DocumentsApi, useValue: { chunks } }],
    }).compileComponents();
  });

  afterEach(() =>
    document.querySelectorAll('.p-drawer-mask, .p-drawer').forEach((e) => e.remove()),
  );

  async function open(document: CitraDocument | null) {
    const fixture = TestBed.createComponent(DocumentDrawer);
    fixture.componentRef.setInput('document', document);
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);
    await fixture.whenStable();
    return {
      fixture,
      closedCount: () => closed,
      text: () => globalThis.document.body.textContent?.replace(/\s+/g, ' ') ?? '',
      chunkItems: () => [...globalThis.document.querySelectorAll('.chunk-list li')],
      set: async (d: CitraDocument | null) => {
        fixture.componentRef.setInput('document', d);
        await fixture.whenStable();
      },
    };
  }

  it('stays closed without a document', async () => {
    const { text } = await open(null);

    expect(text()).not.toContain('Chunks');
    expect(chunks).not.toHaveBeenCalled();
  });

  it('shows the document’s details and its chunks in reading order', async () => {
    chunks.mockReturnValue(
      of(page([chunk(0), chunk(1, { heading: null, pageNumber: 2 }), chunk(2)])),
    );
    const { text, chunkItems } = await open(doc());

    expect(chunks).toHaveBeenCalledWith('d1', 1, CHUNKS_PAGE_SIZE);
    expect(text()).toContain('Employee handbook.pdf');
    expect(text()).toContain('12.4 KB');
    expect(text()).toContain('Chunks (3)');
    const items = chunkItems();
    expect(items.map((i) => i.querySelector('.chunk-number')?.textContent?.trim())).toEqual([
      '#1',
      '#2',
      '#3',
    ]);
    expect(items[0].textContent).toContain('Page 1');
    expect(items[0].textContent).toContain('Vacation');
    expect(items[0].textContent).toContain('12 tokens');
    expect(items[1].querySelector('.chunk-heading')).toBeNull();
    // Exactly the chunk's text: no added spaces or line breaks (it is shown with pre-wrap).
    expect(items[0].querySelector('.chunk-content')?.textContent).toBe(
      'Chunk 0: employees get 25 days of paid vacation.',
    );
  });

  it('links a web page’s address, opening safely in a new tab', async () => {
    chunks.mockReturnValue(of(page([chunk(0, { pageNumber: null })])));
    await open(
      doc({
        sourceType: 'Url',
        sourceUrl: 'https://acme.test/help',
        fileName: 'Help',
        sizeBytes: 0,
      }),
    );

    const link = globalThis.document.querySelector<HTMLAnchorElement>('.source-link')!;
    expect(link.href).toBe('https://acme.test/help');
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');
    expect(globalThis.document.querySelector('.chunk-list li')?.textContent).not.toContain('Page');
  });

  it('explains a failure and loads no chunks', async () => {
    const { text } = await open(
      doc({ status: 'Failed', errorMessage: 'This PDF is a scanned image.', chunkCount: 0 }),
    );

    expect(text()).toContain('This PDF is a scanned image.');
    expect(text()).toContain('No chunks: processing failed.');
    expect(chunks).not.toHaveBeenCalled();
  });

  it('loads the chunks as soon as a live update says the document is ready', async () => {
    chunks.mockReturnValue(of(page([chunk(0)])));
    const { set, text, chunkItems } = await open(doc({ status: 'Embedding', processedAt: null }));
    expect(text()).toContain('Chunks appear here once processing is finished.');
    expect(chunks).not.toHaveBeenCalled();

    await set(doc({ status: 'Ready' }));

    expect(chunks).toHaveBeenCalledTimes(1);
    expect(chunkItems()).toHaveLength(1);
  });

  it('does not reload for an update that changes nothing about the chunks', async () => {
    chunks.mockReturnValue(of(page([chunk(0)])));
    const { set } = await open(doc());

    await set(doc({ fileName: 'Renamed.pdf' }));

    expect(chunks).toHaveBeenCalledTimes(1);
  });

  it('loads more chunks on request, without repeating any', async () => {
    const first = Array.from({ length: CHUNKS_PAGE_SIZE }, (_, i) => chunk(i));
    chunks
      .mockReturnValueOnce(of(page(first, 25)))
      .mockReturnValueOnce(
        of(page([chunk(19), ...Array.from({ length: 5 }, (_, i) => chunk(20 + i))], 25)),
      );
    const { fixture, chunkItems, text } = await open(doc({ chunkCount: 25 }));
    expect(text()).toContain('Load more (5 left)');

    [...globalThis.document.querySelectorAll<HTMLButtonElement>('p-button button')]
      .find((b) => b.textContent?.includes('Load more'))!
      .click();
    await fixture.whenStable();

    expect(chunks).toHaveBeenLastCalledWith('d1', 2, CHUNKS_PAGE_SIZE);
    expect(chunkItems()).toHaveLength(25);
    expect(text()).not.toContain('Load more');
  });

  it('clips long chunks until opened', async () => {
    chunks.mockReturnValue(of(page([chunk(0, { content: 'Long text. '.repeat(60) }), chunk(1)])));
    const { fixture, chunkItems } = await open(doc());
    const [long, short] = chunkItems();
    expect(long.querySelector('.chunk-content')?.classList).toContain('clipped');
    expect(short.querySelector('button.toggle')).toBeNull();
    expect(long.querySelector('button.toggle')?.getAttribute('aria-expanded')).toBe('false');

    long.querySelector<HTMLButtonElement>('button.toggle')!.click();
    await fixture.whenStable();

    expect(chunkItems()[0].querySelector('.chunk-content')?.classList).not.toContain('clipped');
    const toggle = chunkItems()[0].querySelector('button.toggle')!;
    expect(toggle.textContent).toContain('Show less');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
  });

  it('offers a retry when the chunks cannot be loaded', async () => {
    chunks
      .mockReturnValueOnce(throwError(() => new Error('offline')))
      .mockReturnValueOnce(of(page([chunk(0)])));
    const { fixture, text, chunkItems } = await open(doc());
    expect(text()).toContain('The chunks could not be loaded.');

    [...globalThis.document.querySelectorAll<HTMLButtonElement>('p-button button')]
      .find((b) => b.textContent?.includes('Try again'))!
      .click();
    await fixture.whenStable();

    expect(chunkItems()).toHaveLength(1);
  });

  it('ignores a late answer for the document shown before', async () => {
    const late = new Subject<PagedResult<DocumentChunk>>();
    chunks.mockImplementation((id) =>
      id === 'd1' ? late : of(page([chunk(0, { id: 'other', content: 'Other document' })])),
    );
    const { fixture, set, chunkItems } = await open(doc());

    await set(doc({ id: 'd2', fileName: 'Other.pdf' }));
    late.next(page([chunk(0, { content: 'Too late' })]));
    await fixture.whenStable();

    expect(chunkItems().map((i) => i.querySelector('.chunk-content')?.textContent?.trim())).toEqual(
      ['Other document'],
    );
  });

  it('tells the page when the user closes it', async () => {
    chunks.mockReturnValue(of(page([])));
    const { fixture, closedCount } = await open(doc());

    fixture.debugElement.children[0].triggerEventHandler('visibleChange', false);

    expect(closedCount()).toBe(1);
  });
});
