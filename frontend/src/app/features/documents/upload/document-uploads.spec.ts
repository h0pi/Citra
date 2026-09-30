import { HttpErrorResponse, HttpEvent, HttpEventType, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { DocumentsApi } from '@core/documents/documents-api';
import { CitraDocument } from '@core/models/document.models';
import { Observable, Subject } from 'rxjs';

import { DocumentUploads } from './document-uploads';

function file(name = 'Handbook.pdf', size = 1000): File {
  const f = new File(['x'], name);
  Object.defineProperty(f, 'size', { value: size });
  return f;
}

function doc(id: string): CitraDocument {
  return {
    id,
    botId: 'b1',
    fileName: 'Handbook.pdf',
    contentType: 'application/pdf',
    sizeBytes: 1000,
    sourceType: 'Pdf',
    sourceUrl: null,
    status: 'Uploaded',
    errorMessage: null,
    pageCount: 0,
    chunkCount: 0,
    createdAt: '2026-09-29T10:00:00Z',
    updatedAt: '2026-09-29T10:00:00Z',
    processedAt: null,
  };
}

const progress = (loaded: number, total: number): HttpEvent<CitraDocument> => ({
  type: HttpEventType.UploadProgress,
  loaded,
  total,
});
const response = (body: CitraDocument) => new HttpResponse({ status: 201, body });

describe('DocumentUploads', () => {
  let requests: {
    botId: string;
    file: File;
    events: Subject<HttpEvent<CitraDocument>>;
    cancelled: boolean;
  }[];
  let uploads: DocumentUploads;
  let uploaded: CitraDocument[];
  let destroy: () => void;

  beforeEach(() => {
    vi.useFakeTimers();
    requests = [];
    uploaded = [];
    TestBed.configureTestingModule({
      providers: [
        DocumentUploads,
        {
          provide: DocumentsApi,
          useValue: {
            upload: (botId: string, f: File) => {
              const events = new Subject<HttpEvent<CitraDocument>>();
              const entry = { botId, file: f, events, cancelled: false };
              requests.push(entry);
              // Like HttpClient: unsubscribing before the answer cancels the request.
              return new Observable<HttpEvent<CitraDocument>>((subscriber) => {
                const inner = events.subscribe(subscriber);
                return () => {
                  entry.cancelled = !events.closed && !events.isStopped;
                  inner.unsubscribe();
                };
              });
            },
          },
        },
      ],
    });
    uploads = TestBed.inject(DocumentUploads);
    uploads.uploaded.subscribe((d) => uploaded.push(d));
    destroy = () => TestBed.resetTestingModule();
  });

  afterEach(() => vi.useRealTimers());

  it('shows each file’s progress, then the new document, and clears it after a moment', () => {
    uploads.add('b1', [file()]);
    const [request] = requests;
    expect(request.botId).toBe('b1');
    expect(uploads.items()[0]).toMatchObject({
      fileName: 'Handbook.pdf',
      state: 'uploading',
      progress: 0,
    });

    request.events.next(progress(250, 1000));
    expect(uploads.items()[0].progress).toBe(25);
    request.events.next(progress(1000, 1000));
    expect(uploads.items()[0].progress).toBe(99); // the server still has to answer

    request.events.next(response(doc('d1')));
    request.events.complete();
    expect(uploads.items()[0]).toMatchObject({ state: 'done', progress: 100, error: null });
    expect(uploaded.map((d) => d.id)).toEqual(['d1']);

    vi.advanceTimersByTime(DocumentUploads.DoneVisibleMs);
    expect(uploads.items()).toEqual([]);
  });

  it('refuses files the server would refuse without uploading them', () => {
    uploads.add('b1', [file('photo.jpg'), file('empty.pdf', 0)]);

    expect(requests).toEqual([]);
    expect(uploads.items().map((i) => [i.state, i.error])).toEqual([
      ['failed', 'This file type is not supported. Upload .pdf, .docx, .txt, .md.'],
      ['failed', 'The file is empty.'],
    ]);
  });

  it('shows the server’s reason when it refuses a file, and keeps it until dismissed', () => {
    uploads.add('b1', [file('a.pdf'), file('b.pdf'), file('c.pdf')]);
    const [duplicate, conflict, offline] = requests;

    duplicate.events.error(
      new HttpErrorResponse({
        status: 400,
        error: {
          title: 'Validation failed',
          errors: { file: ['This file was already uploaded as "a.pdf".'] },
        },
      }),
    );
    conflict.events.error(
      new HttpErrorResponse({ status: 409, error: { detail: 'The bot was deleted.' } }),
    );
    offline.events.error(new HttpErrorResponse({ status: 0 }));

    expect(uploads.items().map((i) => i.error)).toEqual([
      'This file was already uploaded as "a.pdf".',
      'The bot was deleted.',
      'Cannot reach the server. Check your internet connection and try again.',
    ]);
    vi.advanceTimersByTime(60_000);
    expect(uploads.items()).toHaveLength(3);

    uploads.dismiss(uploads.items()[0].id);
    expect(uploads.items()).toHaveLength(2);
  });

  it('sends at most three files at once; the next starts when one finishes', () => {
    uploads.add('b1', [file('1.pdf'), file('2.pdf'), file('3.pdf'), file('4.pdf')]);

    expect(requests.map((r) => r.file.name)).toEqual(['1.pdf', '2.pdf', '3.pdf']);
    expect(uploads.items()[3].state).toBe('waiting');

    requests[1].events.next(response(doc('d2')));
    requests[1].events.complete();

    expect(requests.map((r) => r.file.name)).toEqual(['1.pdf', '2.pdf', '3.pdf', '4.pdf']);
    expect(uploads.items()[3].state).toBe('uploading');
  });

  it('cancels an upload that is running, and one that is still waiting', () => {
    uploads.add('b1', [file('1.pdf'), file('2.pdf'), file('3.pdf'), file('4.pdf'), file('5.pdf')]);
    const [first] = requests;
    const waitingId = uploads.items()[4].id;

    uploads.dismiss(waitingId);
    uploads.dismiss(uploads.items()[0].id);

    expect(first.cancelled).toBe(true);
    expect(uploads.items().map((i) => i.fileName)).toEqual(['2.pdf', '3.pdf', '4.pdf']);
    expect(requests.map((r) => r.file.name)).toEqual(['1.pdf', '2.pdf', '3.pdf', '4.pdf']);
  });

  it('cancels running uploads when the page is left', () => {
    uploads.add('b1', [file()]);
    const [request] = requests;

    destroy();

    expect(request.cancelled).toBe(true);
  });
});
