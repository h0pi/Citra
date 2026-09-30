import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { NotificationService } from '@core/services/notification.service';
import { ConfirmationService } from 'primeng/api';
import { Paginator } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Tooltip } from 'primeng/tooltip';
import { CurrentBot } from '@core/bots/current-bot';
import { DocumentUpdates, LiveState, RemovedDocument } from '@core/documents/document-updates';
import { DocumentsApi } from '@core/documents/documents-api';
import { BotSummary } from '@core/models/bot.models';
import { CitraDocument, DocumentListQuery, PagedResult } from '@core/models/document.models';
import { Observable, Subject, of, throwError } from 'rxjs';
import { By } from '@angular/platform-browser';

import { DocumentDrawer } from './document-drawer/document-drawer';
import { DocumentsPage } from './documents-page';
import { DocumentUploads, UploadItem } from './upload/document-uploads';
import { UploadZone } from './upload/upload-zone';
import { AddUrlDialog } from './add-url-dialog/add-url-dialog';

@Component({ template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class BotsStub {}

function botSummary(id: string, name: string): BotSummary {
  return {
    id,
    name,
    slug: name.toLowerCase(),
    description: null,
    isPublic: false,
    widgetColor: '#6366F1',
    documentCount: 0,
    messageCount: 0,
    createdAt: '2026-09-26T10:00:00Z',
    updatedAt: '2026-09-26T10:00:00Z',
  };
}

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
    chunkCount: 14,
    createdAt: '2026-09-29T09:05:00Z',
    updatedAt: '2026-09-29T09:05:00Z',
    processedAt: '2026-09-29T09:05:03Z',
    ...overrides,
  };
}

function page(items: CitraDocument[]): PagedResult<CitraDocument> {
  return {
    items,
    page: 1,
    pageSize: 20,
    totalCount: items.length,
    totalPages: items.length ? 1 : 0,
  };
}

describe('DocumentsPage', () => {
  const list =
    vi.fn<(botId: string, query?: DocumentListQuery) => Observable<PagedResult<CitraDocument>>>();
  const reprocess = vi.fn<(id: string) => Observable<CitraDocument>>();
  const remove = vi.fn<(id: string) => Observable<void>>();
  const success = vi.fn();
  const refresh = vi.fn();
  const bots = signal<BotSummary[]>([]);
  const selectedId = signal<string | null>(null);
  const status = signal<'idle' | 'loading' | 'ready' | 'error'>('ready');
  const canEdit = signal(true);
  const uploads = {
    items: signal<UploadItem[]>([]),
    add: vi.fn(),
    dismiss: vi.fn(),
    uploaded: new Subject<CitraDocument>(),
  };

  const live = {
    state: signal<LiveState>('connected'),
    changed: new Subject<CitraDocument>(),
    removed: new Subject<RemovedDocument>(),
    resynced: new Subject<void>(),
    start: vi.fn(),
  };

  beforeEach(async () => {
    live.state.set('connected');
    live.changed = new Subject<CitraDocument>();
    live.removed = new Subject<RemovedDocument>();
    live.resynced = new Subject<void>();
    live.start.mockReset();
    uploads.add.mockReset();
    uploads.items.set([]);
    uploads.uploaded = new Subject<CitraDocument>();
    list.mockReset();
    reprocess.mockReset();
    remove.mockReset();
    success.mockReset();
    refresh.mockReset();
    bots.set([botSummary('b1', 'Support'), botSummary('b2', 'HR')]);
    selectedId.set('b1');
    status.set('ready');
    canEdit.set(true);
    await TestBed.configureTestingModule({
      imports: [DocumentsPage],
      providers: [
        provideRouter([{ path: 'bots', component: BotsStub }]),
        {
          provide: DocumentsApi,
          useValue: {
            list,
            reprocess,
            delete: remove,
            chunks: () => of({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
          },
        },
        { provide: NotificationService, useValue: { success } },
        { provide: AuthService, useValue: { canEdit: canEdit.asReadonly() } },
        {
          provide: CurrentBot,
          useValue: {
            status: status.asReadonly(),
            selectedId: selectedId.asReadonly(),
            bot: computed(() => bots().find((b) => b.id === selectedId()) ?? null),
            refresh,
          },
        },
      ],
    })
      // The page provides its own uploads and live connection; tests swap in fakes.
      .overrideComponent(DocumentsPage, {
        set: {
          providers: [
            { provide: DocumentUploads, useValue: uploads },
            { provide: DocumentUpdates, useValue: live },
            ConfirmationService,
          ],
        },
      })
      .compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(DocumentsPage);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const rows = (el: HTMLElement) => [...el.querySelectorAll('tbody tr')];
  const cells = (row: Element) => [...row.querySelectorAll('td')].map((c) => c.textContent?.trim());

  it('shows placeholders while the documents load', async () => {
    list.mockReturnValue(new Subject<PagedResult<CitraDocument>>());
    const { el } = await render();

    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(el.querySelectorAll('p-skeleton').length).toBeGreaterThan(0);
    expect(el.querySelector('p-table')).toBeNull();
  });

  it('shows placeholders while the bots themselves load', async () => {
    status.set('loading');
    selectedId.set(null);
    const { el } = await render();

    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(list).not.toHaveBeenCalled();
  });

  it('lists the documents with their details and status', async () => {
    list.mockReturnValue(
      of(
        page([
          doc(),
          doc({
            id: 'd2',
            fileName: 'Returns policy',
            sourceType: 'Url',
            sourceUrl: 'https://acme.test/returns',
            sizeBytes: 0,
            status: 'Embedding',
            pageCount: 0,
            chunkCount: 0,
          }),
          doc({ id: 'd3', fileName: 'scan.pdf', status: 'Failed', errorMessage: 'Scanned PDF' }),
        ]),
      ),
    );
    const { el } = await render();

    expect(list).toHaveBeenCalledWith('b1', { page: 1, pageSize: 20, status: null, search: '' });
    const [pdf, web, failed] = rows(el);
    expect(cells(pdf)).toEqual([
      'Employee handbook.pdf',
      '12.4 KB',
      '6',
      '14',
      'Ready',
      // Shown in the viewer's own time zone.
      formatDate('2026-09-29T09:05:00Z', 'MMM d, y, HH:mm', 'en-US'),
      '', // actions: only an icon button (delete)
    ]);
    expect(pdf.querySelector('i[role="img"]')?.getAttribute('aria-label')).toBe('PDF');

    expect(cells(web)[0]).toContain('Returns policy');
    expect(cells(web)[0]).toContain('https://acme.test/returns');
    expect(cells(web).slice(1, 5)).toEqual(['—', '—', '—', 'Indexing']);
    expect(web.querySelector('.pi-spinner')).not.toBeNull();

    expect(cells(failed)[4]).toBe('Failed');
    expect(failed.querySelector('[data-status="Failed"]')).not.toBeNull();
    expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe(
      'Documents of Support',
    );
  });

  it('invites the first upload when the bot has no documents', async () => {
    list.mockReturnValue(of(page([])));
    const { el } = await render();

    expect(el.textContent).toContain('Upload your first document');
    expect(el.textContent).toContain('Support answers questions from them');
    expect(el.querySelector('p-table')).toBeNull();
  });

  it('asks for a bot first when the company has none', async () => {
    bots.set([]);
    selectedId.set(null);
    const { fixture, el } = await render();

    expect(el.textContent).toContain('Create a bot first');
    expect(list).not.toHaveBeenCalled();

    el.querySelector<HTMLButtonElement>('p-button button')!.click();
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/bots');
  });

  it('offers a retry when loading fails', async () => {
    list
      .mockReturnValueOnce(throwError(() => new Error('offline')))
      .mockReturnValueOnce(of(page([doc()])));
    const { fixture, el } = await render();

    expect(el.textContent).toContain("Couldn't load the documents");
    el.querySelector<HTMLButtonElement>('p-button button')!.click();
    await fixture.whenStable();

    expect(list).toHaveBeenCalledTimes(2);
    expect(rows(el)).toHaveLength(1);
  });

  it('offers a retry when the bots could not be loaded', async () => {
    status.set('error');
    selectedId.set(null);
    const { fixture, el } = await render();

    expect(el.textContent).toContain("Couldn't load your bots");
    el.querySelector<HTMLButtonElement>('p-button button')!.click();
    await fixture.whenStable();
    expect(refresh).toHaveBeenCalled();
  });

  it('follows the bot chosen in the top bar, ignoring a late answer for the old bot', async () => {
    const forSupport = new Subject<PagedResult<CitraDocument>>();
    list.mockImplementation((botId) =>
      botId === 'b1'
        ? forSupport
        : of(page([doc({ id: 'hr1', botId: 'b2', fileName: 'Leave.docx', sourceType: 'Docx' })])),
    );
    const { fixture, el } = await render();

    selectedId.set('b2');
    await fixture.whenStable();
    forSupport.next(page([doc({ fileName: 'late answer.pdf' })])); // the old bot's list arrives too late

    await fixture.whenStable();
    expect(list.mock.calls.map((c) => c[0])).toEqual(['b1', 'b2']);
    expect(rows(el).map((r) => cells(r)[0])).toEqual(['Leave.docx']);
    expect(el.textContent).toContain('Files and web pages HR answers from.');
  });

  describe('adding documents', () => {
    it('gives editors a drop zone and an "Add web page" button above the list', async () => {
      list.mockReturnValue(of(page([doc()])));
      const { fixture, el } = await render();

      const zone = fixture.debugElement.query(By.directive(UploadZone));
      expect(zone).not.toBeNull();
      expect(zone.componentInstance.compact()).toBe(true);
      expect(el.textContent).toContain('Add web page');
    });

    it('turns the empty page into a big drop zone with a web page option', async () => {
      list.mockReturnValue(of(page([])));
      const { fixture, el } = await render();

      const zone = fixture.debugElement.query(By.directive(UploadZone));
      expect(zone.componentInstance.compact()).toBe(false);
      expect(el.textContent).toContain('Or add a web page');
    });

    it('gives viewers no way to add documents', async () => {
      canEdit.set(false);
      list.mockReturnValue(of(page([])));
      const { fixture, el } = await render();

      expect(fixture.debugElement.query(By.directive(UploadZone))).toBeNull();
      expect(el.textContent).toContain('No documents yet');
      expect(el.textContent).toContain(
        'An owner or admin of your workspace can add the files Support answers from.',
      );
      expect(el.textContent).not.toContain('Add web page');
    });

    it('uploads dropped files to the bot shown', async () => {
      list.mockReturnValue(of(page([doc()])));
      const { fixture } = await render();
      const files = [new File(['a'], 'a.pdf')];

      fixture.debugElement.query(By.directive(UploadZone)).componentInstance.files.emit(files);

      expect(uploads.add).toHaveBeenCalledWith('b1', files);
    });

    it('puts an uploaded document at the top, once, and only in its own bot’s list', async () => {
      list.mockReturnValue(of(page([doc()])));
      const { fixture, el } = await render();

      const fresh = doc({ id: 'new', fileName: 'Fresh.pdf', status: 'Uploaded' });
      uploads.uploaded.next(fresh);
      uploads.uploaded.next(fresh);
      uploads.uploaded.next(doc({ id: 'other', botId: 'b2', fileName: 'Other bot.pdf' }));
      await fixture.whenStable();

      expect(rows(el).map((r) => cells(r)[0])).toEqual(['Fresh.pdf', 'Employee handbook.pdf']);
      expect(cells(rows(el)[0])[4]).toBe('Queued');
    });

    it('shows the first upload in place of the empty page', async () => {
      list.mockReturnValue(of(page([])));
      const { fixture, el } = await render();

      uploads.uploaded.next(doc({ id: 'first', fileName: 'First.pdf', status: 'Uploaded' }));
      await fixture.whenStable();

      expect(el.textContent).not.toContain('Upload your first document');
      expect(rows(el).map((r) => cells(r)[0])).toEqual(['First.pdf']);
    });

    it('lists a web page added from the dialog', async () => {
      list.mockReturnValue(of(page([doc()])));
      const { fixture, el } = await render();

      const dialog = fixture.debugElement.query(By.directive(AddUrlDialog));
      expect(dialog.componentInstance.botId()).toBe('b1');
      dialog.componentInstance.added.emit(
        doc({
          id: 'web',
          fileName: 'acme.test/help',
          sourceType: 'Url',
          sourceUrl: 'https://acme.test/help',
          status: 'Uploaded',
        }),
      );
      await fixture.whenStable();

      expect(cells(rows(el)[0])[0]).toContain('acme.test/help');
    });
  });

  describe('live updates', () => {
    it('connects when the page opens', async () => {
      list.mockReturnValue(of(page([])));
      await render();

      expect(live.start).toHaveBeenCalledTimes(1);
    });

    it('changes a status the moment the server reports it', async () => {
      list.mockReturnValue(of(page([doc({ status: 'Uploaded', pageCount: 0, chunkCount: 0 })])));
      const { fixture, el } = await render();
      expect(cells(rows(el)[0])[4]).toBe('Queued');

      live.changed.next(doc({ status: 'Embedding', pageCount: 0, chunkCount: 0 }));
      await fixture.whenStable();
      expect(cells(rows(el)[0])[4]).toBe('Indexing');

      live.changed.next(doc({ status: 'Ready', pageCount: 6, chunkCount: 14 }));
      await fixture.whenStable();
      expect(cells(rows(el)[0]).slice(2, 5)).toEqual(['6', '14', 'Ready']);
      expect(rows(el)).toHaveLength(1);
    });

    it('shows a document someone else added, but not another bot’s', async () => {
      list.mockReturnValue(of(page([doc()])));
      const { fixture, el } = await render();

      live.changed.next(doc({ id: 'theirs', fileName: 'Their upload.pdf', status: 'Uploaded' }));
      live.changed.next(doc({ id: 'hr', botId: 'b2', fileName: 'HR only.pdf' }));
      await fixture.whenStable();

      expect(rows(el).map((r) => cells(r)[0])).toEqual([
        'Their upload.pdf',
        'Employee handbook.pdf',
      ]);
    });

    it('removes a deleted document', async () => {
      list.mockReturnValue(of(page([doc(), doc({ id: 'd2', fileName: 'Old.pdf' })])));
      const { fixture, el } = await render();

      live.removed.next({ id: 'd2', botId: 'b1' });
      live.removed.next({ id: 'unknown', botId: 'b1' });
      await fixture.whenStable();

      expect(rows(el).map((r) => cells(r)[0])).toEqual(['Employee handbook.pdf']);
    });

    it('refreshes the list in place after a reconnect, without the loading placeholders', async () => {
      const refreshed = new Subject<PagedResult<CitraDocument>>();
      list
        .mockReturnValueOnce(of(page([doc({ status: 'Embedding' })])))
        .mockReturnValueOnce(refreshed);
      const { fixture, el } = await render();

      live.resynced.next();
      await fixture.whenStable();
      expect(list).toHaveBeenCalledTimes(2);
      expect(el.querySelector('[aria-busy="true"]')).toBeNull();
      expect(rows(el)).toHaveLength(1);

      refreshed.next(page([doc({ status: 'Ready' }), doc({ id: 'd2', fileName: 'Missed.pdf' })]));
      await fixture.whenStable();
      expect(rows(el).map((r) => cells(r)[4])).toEqual(['Ready', 'Ready']);
    });

    it('keeps the list when a refresh after a reconnect fails', async () => {
      list
        .mockReturnValueOnce(of(page([doc()])))
        .mockReturnValueOnce(throwError(() => new Error('offline')));
      const { fixture, el } = await render();

      live.resynced.next();
      await fixture.whenStable();

      expect(el.textContent).not.toContain("Couldn't load the documents");
      expect(rows(el)).toHaveLength(1);
    });

    it('says when live updates are paused', async () => {
      list.mockReturnValue(of(page([doc()])));
      live.state.set('connecting');
      const { fixture, el } = await render();
      expect(el.querySelector('.live-paused')).toBeNull(); // not while connecting the first time

      live.state.set('reconnecting');
      await fixture.whenStable();
      expect(el.querySelector('.live-paused')?.textContent).toContain('Live updates paused');

      live.state.set('connected');
      await fixture.whenStable();
      expect(el.querySelector('.live-paused')).toBeNull();
    });
  });

  describe('failed documents, deleting, search, filter and paging', () => {
    beforeEach(() => {
      // jsdom has no matchMedia; PrimeNG dialogs ask it about the screen size.
      window.matchMedia ??= ((query: string) =>
        ({
          matches: false,
          media: query,
          addEventListener: () => undefined,
          removeEventListener: () => undefined,
        }) as unknown as MediaQueryList) as typeof window.matchMedia;
    });

    afterEach(() => document.querySelectorAll('.p-dialog-mask').forEach((mask) => mask.remove()));

    const failedDoc = (status: CitraDocument['status'] = 'Failed') =>
      doc({
        id: 'bad',
        fileName: 'scan.pdf',
        status,
        errorMessage: 'This PDF is a scanned image.',
      });

    const buttonIn = (row: Element, label: string) =>
      [...row.querySelectorAll<HTMLButtonElement>('p-button button')].find(
        (b) => b.getAttribute('aria-label') === label,
      );

    const selectStatus = (
      fixture: { debugElement: import('@angular/core').DebugElement },
      value: string,
    ) =>
      fixture.debugElement.query(By.directive(Select)).triggerEventHandler('ngModelChange', value);

    it('explains why a document failed, also to keyboard and screen-reader users', async () => {
      list.mockReturnValue(of(page([failedDoc()])));
      const { fixture, el } = await render();

      const reason = el.querySelector<HTMLElement>('.failed-reason')!;
      expect(reason.getAttribute('aria-label')).toBe('Failed: This PDF is a scanned image.');
      expect(reason.tabIndex).toBe(0);
      const tooltips = fixture.debugElement.queryAll(By.directive(Tooltip));
      expect(tooltips.some((d) => d.nativeElement === reason)).toBe(true);
    });

    it('retries a failed document and shows it queued again', async () => {
      const answer = new Subject<CitraDocument>();
      reprocess.mockReturnValue(answer);
      list.mockReturnValue(of(page([failedDoc(), doc()])));
      const { fixture, el } = await render();
      const [failedRow, readyRow] = rows(el);
      expect(buttonIn(readyRow, 'Retry Employee handbook.pdf')).toBeUndefined();

      buttonIn(failedRow, 'Retry scan.pdf')!.click();
      await fixture.whenStable();
      expect(reprocess).toHaveBeenCalledWith('bad');
      expect(buttonIn(rows(el)[0], 'Delete scan.pdf')!.disabled).toBe(true);

      answer.next(failedDoc('Uploaded'));
      answer.complete();
      await fixture.whenStable();
      expect(cells(rows(el)[0])[4]).toBe('Queued');
      expect(buttonIn(rows(el)[0], 'Retry scan.pdf')).toBeUndefined();
      expect(buttonIn(rows(el)[0], 'Delete scan.pdf')!.disabled).toBe(false);
    });

    it('gives viewers no retry or delete buttons', async () => {
      canEdit.set(false);
      list.mockReturnValue(of(page([failedDoc()])));
      const { el } = await render();

      expect(el.querySelectorAll('tbody p-button')).toHaveLength(0);
      expect(el.querySelector('.failed-reason')).not.toBeNull();
    });

    it('deletes a document only after confirming', async () => {
      remove.mockReturnValue(of(undefined));
      list.mockReturnValue(of(page([doc(), doc({ id: 'd2', fileName: 'Old.pdf' })])));
      const { fixture, el } = await render();

      buttonIn(rows(el)[1], 'Delete Old.pdf')!.click();
      await fixture.whenStable();
      expect(document.body.textContent).toContain('Delete document?');
      expect(document.body.textContent).toContain(
        '"Old.pdf" and everything the bot learned from it',
      );

      const dialogButton = (label: string) =>
        [...document.querySelectorAll<HTMLButtonElement>('.p-confirmdialog button')].find((b) =>
          b.textContent?.includes(label),
        )!;
      dialogButton('Cancel').click();
      await fixture.whenStable();
      expect(remove).not.toHaveBeenCalled();

      buttonIn(rows(el)[1], 'Delete Old.pdf')!.click();
      await fixture.whenStable();
      dialogButton('Delete').click();
      await fixture.whenStable();

      expect(remove).toHaveBeenCalledWith('d2');
      expect(rows(el).map((r) => cells(r)[0])).toEqual(['Employee handbook.pdf']);
      expect(success).toHaveBeenCalledWith('Document deleted', '"Old.pdf" was removed.');
    });

    it('searches by name after a short pause, from the first page', async () => {
      list.mockReturnValue(of(page([doc()])));
      const { fixture, el } = await render();
      const search = el.querySelector<HTMLInputElement>('input[type="search"]')!;

      // Typed like a person: a key every 100 ms, then a pause.
      for (const text of ['h', 'ha', '  hand  ']) {
        search.value = text;
        search.dispatchEvent(new Event('input'));
        await new Promise((resolve) => setTimeout(resolve, 100));
      }
      await new Promise((resolve) => setTimeout(resolve, 400));
      await fixture.whenStable();

      expect(list).toHaveBeenCalledTimes(2); // the first load, then one search (not one per key)
      expect(list).toHaveBeenLastCalledWith('b1', {
        page: 1,
        pageSize: 20,
        status: null,
        search: 'hand',
      });
    });

    it('filters by status, from the first page', async () => {
      list.mockReturnValue(of({ ...page([doc()]), totalCount: 45, totalPages: 3 }));
      const { fixture } = await render();
      fixture.debugElement
        .query(By.directive(Paginator))
        .triggerEventHandler('onPageChange', { page: 2, rows: 20 });
      await fixture.whenStable();

      selectStatus(fixture, 'Failed');
      await fixture.whenStable();

      expect(list).toHaveBeenLastCalledWith('b1', {
        page: 1,
        pageSize: 20,
        status: 'Failed',
        search: '',
      });
    });

    it('pages through a long list, keeping the rows visible while the next page loads', async () => {
      const second = new Subject<PagedResult<CitraDocument>>();
      list
        .mockReturnValueOnce(of({ ...page([doc()]), totalCount: 45, totalPages: 3 }))
        .mockReturnValueOnce(second);
      const { fixture, el } = await render();
      const paginator = fixture.debugElement.query(By.directive(Paginator));
      expect(paginator).not.toBeNull();

      paginator.triggerEventHandler('onPageChange', { page: 1, rows: 20, first: 20 });
      await fixture.whenStable();
      expect(list).toHaveBeenLastCalledWith('b1', {
        page: 2,
        pageSize: 20,
        status: null,
        search: '',
      });
      expect(el.querySelector('.table-card')?.getAttribute('aria-busy')).toBe('true');
      expect(rows(el)).toHaveLength(1);

      second.next({
        ...page([doc({ id: 'p2', fileName: 'Page two.pdf' })]),
        page: 2,
        totalCount: 45,
      });
      await fixture.whenStable();
      expect(rows(el).map((r) => cells(r)[0])).toEqual(['Page two.pdf']);
      expect(el.querySelector('.table-card')?.getAttribute('aria-busy')).toBe('false');
    });

    it('shows no paginator for a short list', async () => {
      list.mockReturnValue(of(page([doc()])));
      const { fixture } = await render();

      expect(fixture.debugElement.query(By.directive(Paginator))).toBeNull();
    });

    it('says when nothing matches, and clears the filters', async () => {
      list.mockReturnValueOnce(of(page([doc()]))).mockReturnValue(of(page([])));
      const { fixture, el } = await render();
      selectStatus(fixture, 'Failed');
      await fixture.whenStable();

      expect(el.textContent).toContain('No documents match');
      expect(el.textContent).not.toContain('Upload your first document');

      [...el.querySelectorAll<HTMLButtonElement>('p-button button')]
        .find((b) => b.textContent?.includes('Clear filters'))!
        .click();
      await fixture.whenStable();
      expect(list).toHaveBeenLastCalledWith('b1', {
        page: 1,
        pageSize: 20,
        status: null,
        search: '',
      });
    });

    it('starts another bot from its first page, without the previous filter', async () => {
      list.mockReturnValue(of({ ...page([doc()]), totalCount: 45, totalPages: 3 }));
      const { fixture } = await render();
      selectStatus(fixture, 'Ready');
      await fixture.whenStable();
      fixture.debugElement
        .query(By.directive(Paginator))
        .triggerEventHandler('onPageChange', { page: 2, rows: 20 });
      await fixture.whenStable();

      selectedId.set('b2');
      await fixture.whenStable();

      expect(list).toHaveBeenLastCalledWith('b2', {
        page: 1,
        pageSize: 20,
        status: null,
        search: '',
      });
    });

    it('does not add a live document the current filter or page would not show', async () => {
      list.mockReturnValue(
        of({ ...page([doc({ status: 'Failed' })]), totalCount: 45, totalPages: 3 }),
      );
      const { fixture, el } = await render();
      selectStatus(fixture, 'Failed');
      await fixture.whenStable();

      live.changed.next(doc({ id: 'new-ready', fileName: 'Ready one.pdf', status: 'Ready' }));
      await fixture.whenStable();
      expect(rows(el).map((r) => cells(r)[0])).not.toContain('Ready one.pdf');
      expect(el.querySelector('.list-count')?.textContent).toContain('45 documents');

      fixture.debugElement
        .query(By.directive(Paginator))
        .triggerEventHandler('onPageChange', { page: 1, rows: 20 });
      await fixture.whenStable();
      live.changed.next(doc({ id: 'new-failed', fileName: 'Failed one.pdf', status: 'Failed' }));
      await fixture.whenStable();
      expect(rows(el).map((r) => cells(r)[0])).not.toContain('Failed one.pdf'); // it belongs on page 1
      expect(el.querySelector('.list-count')?.textContent).toContain('46 documents');
    });

    it('refills the page after a deletion when later pages have more', async () => {
      list.mockReturnValue(
        of({
          ...page([doc(), doc({ id: 'd2', fileName: 'Old.pdf' })]),
          totalCount: 30,
          totalPages: 2,
        }),
      );
      const { fixture } = await render();

      live.removed.next({ id: 'd2', botId: 'b1' });
      await fixture.whenStable();

      expect(list).toHaveBeenCalledTimes(2);
    });

    it('goes back a page when the last document of a later page is deleted', async () => {
      list
        .mockReturnValueOnce(of({ ...page([doc()]), totalCount: 21, totalPages: 2 }))
        .mockReturnValueOnce(
          of({ ...page([doc({ id: 'last', fileName: 'Last.pdf' })]), page: 2, totalCount: 21 }),
        )
        .mockReturnValue(of({ ...page([doc()]), totalCount: 20, totalPages: 1 }));
      const { fixture } = await render();
      fixture.debugElement
        .query(By.directive(Paginator))
        .triggerEventHandler('onPageChange', { page: 1, rows: 20 });
      await fixture.whenStable();

      live.removed.next({ id: 'last', botId: 'b1' });
      await fixture.whenStable();

      expect(list).toHaveBeenLastCalledWith('b1', {
        page: 1,
        pageSize: 20,
        status: null,
        search: '',
      });
    });
  });

  describe('document details', () => {
    const drawerDocument = (fixture: { debugElement: import('@angular/core').DebugElement }) =>
      fixture.debugElement.query(By.directive(DocumentDrawer)).componentInstance.document();

    it('opens a document’s details from its name, and closes them', async () => {
      list.mockReturnValue(of(page([doc(), doc({ id: 'd2', fileName: 'Old.pdf' })])));
      const { fixture, el } = await render();
      expect(drawerDocument(fixture)).toBeNull();

      const name = rows(el)[1].querySelector<HTMLButtonElement>('button.doc-title')!;
      expect(name.getAttribute('aria-label')).toBe('Show details of Old.pdf');
      name.click();
      await fixture.whenStable();
      expect(drawerDocument(fixture)?.id).toBe('d2');

      fixture.debugElement.query(By.directive(DocumentDrawer)).componentInstance.closed.emit();
      await fixture.whenStable();
      expect(drawerDocument(fixture)).toBeNull();
    });

    it('keeps the open details up to date with live changes', async () => {
      list.mockReturnValue(of(page([doc({ status: 'Embedding', pageCount: 0, chunkCount: 0 })])));
      const { fixture, el } = await render();
      rows(el)[0].querySelector<HTMLButtonElement>('button.doc-title')!.click();
      await fixture.whenStable();

      live.changed.next(doc({ status: 'Ready', chunkCount: 14 }));
      await fixture.whenStable();

      expect(drawerDocument(fixture)).toMatchObject({ status: 'Ready', chunkCount: 14 });
    });

    it('closes the details of a document deleted meanwhile', async () => {
      list.mockReturnValue(of(page([doc(), doc({ id: 'd2', fileName: 'Old.pdf' })])));
      const { fixture, el } = await render();
      rows(el)[1].querySelector<HTMLButtonElement>('button.doc-title')!.click();
      await fixture.whenStable();

      live.removed.next({ id: 'd2', botId: 'b1' });
      await fixture.whenStable();

      expect(drawerDocument(fixture)).toBeNull();
    });
  });
});
