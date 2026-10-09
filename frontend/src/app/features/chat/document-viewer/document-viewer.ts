import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  DOCUMENT,
  inject,
  input,
  output,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { DocumentsApi } from '@core/documents/documents-api';
import { Citation } from '@core/models/chat.models';
import { DocumentChunk } from '@core/models/document.models';
import { NgxExtendedPdfViewerModule, NgxExtendedPdfViewerService } from 'ngx-extended-pdf-viewer';
import { DialogModule } from 'primeng/dialog';
import { EMPTY, expand, reduce } from 'rxjs';

/** One page of a document's text, with the cited passage (if it is on this page) cut out to be marked. */
export interface TextPage {
  pageNumber: number | null;
  before: string;
  passage: string;
  after: string;
}

const CHUNKS_PER_REQUEST = 100;

/**
 * The original of a cited source, with the cited passage highlighted: PDFs in a PDF viewer on the cited page,
 * other documents (Word, text, web pages) as their text, put together from the document's chunks.
 */
@Component({
  selector: 'app-document-viewer',
  templateUrl: './document-viewer.html',
  styleUrl: './document-viewer.scss',
  imports: [DialogModule, NgxExtendedPdfViewerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentViewer {
  /** The source to show; null = closed. */
  readonly citation = input<Citation | null>(null);
  readonly closed = output<void>();

  private readonly documentsApi = inject(DocumentsApi);

  protected readonly isPdf = computed(
    () => this.citation()?.documentName.toLowerCase().endsWith('.pdf') ?? false,
  );

  protected readonly file = rxResource({
    params: () => (this.isPdf() ? this.citation()?.documentId : null) ?? undefined,
    stream: ({ params }) => this.documentsApi.file(params),
  });

  /** All chunks of a document that is not a PDF, in reading order. */
  protected readonly chunks = rxResource({
    params: () => (this.isPdf() ? null : this.citation()?.documentId) ?? undefined,
    stream: ({ params }) =>
      this.documentsApi.chunks(params, 1, CHUNKS_PER_REQUEST).pipe(
        expand((result) =>
          result.page < result.totalPages
            ? this.documentsApi.chunks(params, result.page + 1, CHUNKS_PER_REQUEST)
            : EMPTY,
        ),
        reduce((all, result) => [...all, ...result.items], [] as DocumentChunk[]),
      ),
  });

  /** For web pages: where the original is (shown as a link). */
  protected readonly details = rxResource({
    params: () => (this.isPdf() ? null : this.citation()?.documentId) ?? undefined,
    stream: ({ params }) => this.documentsApi.get(params),
  });

  protected readonly pages = computed(() =>
    textPages(this.chunks.value() ?? [], this.citation()?.chunkId ?? null),
  );

  private readonly pdfViewer = inject(NgxExtendedPdfViewerService);

  constructor() {
    const document = inject(DOCUMENT);
    // The marked passage of a text document is scrolled into view once it is shown.
    afterRenderEffect(() => {
      if (this.pages().some((page) => page.passage)) {
        document.querySelector('.document-text mark')?.scrollIntoView({ block: 'center' });
      }
    });
  }

  /** Highlights the passage (searched as one phrase, line breaks as spaces) once the PDF is open. */
  protected highlight(): void {
    const passage = this.citation()?.snippet.replace(/\s+/g, ' ').trim();
    if (passage) {
      this.pdfViewer.find(passage, { highlightAll: true });
    }
  }
}

/**
 * Puts a document's text back together from its chunks. Chunks repeat the end of the previous chunk (overlap)
 * and know where they sit in their page's text, so each page's text is rebuilt without the repeats.
 */
export function textPages(chunks: DocumentChunk[], citedChunkId: string | null): TextPage[] {
  const pages: { pageNumber: number | null; text: string; end: number; cited?: number[] }[] = [];
  for (const chunk of chunks) {
    let page = pages.at(-1);
    if (!page || page.pageNumber !== chunk.pageNumber) {
      page = { pageNumber: chunk.pageNumber, text: '', end: 0 };
      pages.push(page);
    }
    if (page.text && chunk.charStart > page.end) {
      page.text += '\n\n'; // what lay between the chunks was only white space
    }
    const repeated = Math.max(0, page.end - chunk.charStart);
    const start = page.text.length - repeated;
    page.text += chunk.content.slice(repeated);
    page.end = Math.max(page.end, chunk.charEnd);
    if (chunk.id === citedChunkId) {
      page.cited = [start, page.text.length];
    }
  }
  return pages.map(({ pageNumber, text, cited }) => ({
    pageNumber,
    before: cited ? text.slice(0, cited[0]) : text,
    passage: cited ? text.slice(cited[0], cited[1]) : '',
    after: cited ? text.slice(cited[1]) : '',
  }));
}
