import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DocumentsApi } from '@core/documents/documents-api';
import { Citation } from '@core/models/chat.models';
import { NgxExtendedPdfViewerModule, NgxExtendedPdfViewerService } from 'ngx-extended-pdf-viewer';
import { DocumentChunk, PagedResult } from '@core/models/document.models';
import { Observable, Subject, of, throwError } from 'rxjs';
import { DocumentViewer, textPages } from './document-viewer';

/** The real viewer needs a browser with canvas and workers; the test only checks what it is given. */
@Component({
  // eslint-disable-next-line @angular-eslint/component-selector -- stands in for the library's element
  selector: 'ngx-extended-pdf-viewer',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class PdfViewerStub {
  readonly src = input<Blob>();
  readonly page = input<number>();
  readonly showOpenFileButton = input<boolean>();
  readonly textLayer = input<boolean>();
  readonly pdfLoaded = output<void>();
}

const citation: Citation = {
  number: 1,
  chunkId: 'k1',
  documentId: 'd1',
  documentName: 'Handbook.pdf',
  pageNumber: 3,
  snippet: 'Remote work\n\nEmployees may work  from home.',
  score: 0.03,
};

describe('DocumentViewer', () => {
  const file = vi.fn<(id: string) => Observable<Blob>>();
  const find = vi.fn();
  const get = vi.fn().mockReturnValue(of({ sourceUrl: null }));
  const chunks =
    vi.fn<(id: string, page: number, pageSize: number) => Observable<PagedResult<DocumentChunk>>>();

  async function render(shown: Citation = citation) {
    TestBed.configureTestingModule({
      imports: [DocumentViewer],
      providers: [
        { provide: DocumentsApi, useValue: { file, chunks, get } },
        { provide: NgxExtendedPdfViewerService, useValue: { find } },
      ],
    });
    TestBed.overrideComponent(DocumentViewer, {
      remove: { imports: [NgxExtendedPdfViewerModule] },
      add: { imports: [PdfViewerStub] },
    });
    const fixture = TestBed.createComponent(DocumentViewer);
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);
    fixture.componentRef.setInput('citation', shown);
    fixture.detectChanges(); // not whenStable: the file may still be loading
    const viewer = () =>
      fixture.debugElement.query((d) => d.componentInstance instanceof PdfViewerStub)
        ?.componentInstance as PdfViewerStub | undefined;
    return { fixture, viewer, closed: () => closed };
  }

  beforeEach(() => {
    vi.clearAllMocks();
    get.mockReturnValue(of({ sourceUrl: null }));
  });
  afterEach(() => document.body.querySelectorAll('.p-dialog-mask').forEach((m) => m.remove()));

  it('loads the cited document and opens it on the cited page', async () => {
    const pdf = new Subject<Blob>();
    file.mockReturnValue(pdf);
    const { fixture, viewer } = await render({ ...citation, documentName: 'Handbook.PDF' });

    expect(file).toHaveBeenCalledWith('d1');
    expect(document.body.textContent).toContain('Handbook.PDF');
    expect(document.body.textContent).toContain('Loading the document…');

    const blob = new Blob(['%PDF']);
    pdf.next(blob);
    await fixture.whenStable();

    expect(viewer()!.src()).toBe(blob);
    expect(viewer()!.page()).toBe(3);
    expect(viewer()!.textLayer()).toBe(true);

    viewer()!.pdfLoaded.emit();
    expect(find).toHaveBeenCalledWith('Remote work Employees may work from home.', {
      highlightAll: true,
    });
  });

  it('says when the document cannot be opened, and can be closed', async () => {
    file.mockReturnValue(throwError(() => new Error('404')));
    const { fixture, closed } = await render();
    await fixture.whenStable();

    expect(document.body.textContent).toContain("Couldn't open the document.");
    document.body
      .querySelector<HTMLButtonElement>('.p-dialog-close-button, .p-dialog-header button')!
      .click();
    await fixture.whenStable();
    expect(closed()).toBe(1);
  });

  it('says when a text document cannot be opened', async () => {
    chunks.mockReturnValue(throwError(() => new Error('404')));
    const { fixture } = await render({ ...citation, documentName: 'Policy.docx' });
    await fixture.whenStable();

    expect(document.body.textContent).toContain("Couldn't open the document.");
  });

  it('shows other documents as their text, with the cited passage marked and in view', async () => {
    Element.prototype.scrollIntoView = vi.fn();
    const chunk = (
      id: string,
      content: string,
      charStart: number,
      pageNumber: number | null = null,
    ) => ({
      id,
      index: 0,
      pageNumber,
      heading: null,
      content,
      tokenCount: 1,
      charStart,
      charEnd: charStart + content.length,
    });
    // Two requests: 100 chunks per page.
    chunks
      .mockReturnValueOnce(
        of({
          items: [chunk('k1', 'Intro.', 0)],
          page: 1,
          pageSize: 100,
          totalCount: 2,
          totalPages: 2,
        }),
      )
      .mockReturnValueOnce(
        of({
          items: [chunk('k2', 'Vacation: 25 days.', 8)],
          page: 2,
          pageSize: 100,
          totalCount: 2,
          totalPages: 2,
        }),
      );
    const { fixture, viewer } = await render({
      ...citation,
      documentName: 'Policy.docx',
      chunkId: 'k2',
    });
    await fixture.whenStable();

    expect(file).not.toHaveBeenCalledWith('d1');
    expect(chunks.mock.calls.map((c) => c[1])).toEqual([1, 2]);
    expect(viewer()).toBeUndefined();
    const text = document.querySelector('.document-text')!;
    expect(text.textContent).toBe('Intro.\n\nVacation: 25 days.');
    expect(text.querySelector('mark')!.textContent).toBe('Vacation: 25 days.');
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled();
    expect(document.querySelector('.original-link')).toBeNull(); // a file, not a web page
  });

  it('links web pages to the original page', async () => {
    chunks.mockReturnValue(of({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 1 }));
    get.mockReturnValue(of({ sourceUrl: 'https://example.com/leave' }));
    const { fixture } = await render({ ...citation, documentName: 'Leave policy' });
    await fixture.whenStable();

    const link = document.querySelector<HTMLAnchorElement>('.original-link')!;
    expect(get).toHaveBeenCalledWith('d1');
    expect(link.getAttribute('href')).toBe('https://example.com/leave');
    expect(link.target).toBe('_blank');
    expect(link.rel).toBe('noopener noreferrer');
    expect(link.textContent!.trim()).toBe('Open original page');
  });
});

describe('textPages', () => {
  const chunk = (
    id: string,
    content: string,
    charStart: number,
    pageNumber: number | null = 1,
  ) => ({
    id,
    index: 0,
    pageNumber,
    heading: null,
    content,
    tokenCount: 1,
    charStart,
    charEnd: charStart + content.length,
  });

  it('puts overlapping chunks back together without the repeats, page by page', () => {
    const pages = textPages(
      [
        chunk('a', 'One two three', 0),
        chunk('b', 'three four', 8), // repeats "three"
        chunk('c', 'Page two', 0, 2),
      ],
      'b',
    );

    expect(pages).toEqual([
      { pageNumber: 1, before: 'One two ', passage: 'three four', after: '' },
      { pageNumber: 2, before: 'Page two', passage: '', after: '' },
    ]);
  });

  it('keeps a gap between chunks as a paragraph break', () => {
    expect(
      textPages([chunk('a', 'First.', 0, null), chunk('b', 'Second.', 10, null)], null),
    ).toEqual([{ pageNumber: null, before: 'First.\n\nSecond.', passage: '', after: '' }]);
  });
});
