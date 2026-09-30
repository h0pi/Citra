import { DatePipe, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { CurrentBot } from '@core/bots/current-bot';
import { DocumentUpdates, RemovedDocument } from '@core/documents/document-updates';
import { DocumentsApi } from '@core/documents/documents-api';
import { CitraDocument, DocumentStatusFilter } from '@core/models/document.models';
import { NotificationService } from '@core/services/notification.service';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { PaginatorModule, PaginatorState } from 'primeng/paginator';
import { SelectModule } from 'primeng/select';
import { SkeletonModule } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import {
  EMPTY,
  Subject,
  catchError,
  combineLatest,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';

import { AddUrlDialog } from './add-url-dialog/add-url-dialog';
import { DocumentDrawer } from './document-drawer/document-drawer';
import {
  STATUS_FILTER_OPTIONS,
  chunkCount,
  fileSize,
  matchesFilters,
  pageCount,
  statusBadge,
  typeIcon,
} from './document-display';
import { DocumentUploads } from './upload/document-uploads';
import { UploadList } from './upload/upload-list';
import { UploadZone } from './upload/upload-zone';

type LoadResult = { ok: true; documents: CitraDocument[]; totalCount: number } | { ok: false };

/** Rows per page to choose from; the first one is also the smallest list that gets a paginator. */
export const PAGE_SIZES = [10, 20, 50];
const DEFAULT_PAGE_SIZE = 20;

/** Typing pauses this long before the list is searched (not one request per key). */
export const SEARCH_DEBOUNCE_MS = 300;

/** Same limit as the API (GetDocumentsQuery.SearchMaxLength). */
export const SEARCH_MAX_LENGTH = 200;

/**
 * The current bot's documents: name and type, size, pages, chunks, processing status and upload date; searchable,
 * filterable by status and paged. Follows the bot chosen in the top bar. Editors add files (drag & drop, several
 * at once, with progress) and web pages, retry failed ones and delete documents here.
 */
@Component({
  selector: 'app-documents-page',
  imports: [
    PageHeader,
    EmptyState,
    UploadZone,
    UploadList,
    AddUrlDialog,
    DocumentDrawer,
    RouterLink,
    DatePipe,
    NgTemplateOutlet,
    FormsModule,
    ButtonModule,
    ConfirmDialogModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    PaginatorModule,
    SelectModule,
    SkeletonModule,
    TableModule,
    TagModule,
    TooltipModule,
  ],
  templateUrl: './documents-page.html',
  styleUrl: './documents-page.scss',
  // Uploads and the live-update connection belong to this page: leaving it ends both.
  providers: [DocumentUploads, DocumentUpdates, ConfirmationService],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentsPage {
  private readonly documentsApi = inject(DocumentsApi);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notifications = inject(NotificationService);
  protected readonly currentBot = inject(CurrentBot);
  protected readonly uploads = inject(DocumentUploads);
  protected readonly live = inject(DocumentUpdates);

  /** Only Owner and Admin add, retry and delete documents (the API enforces the same rule). */
  protected readonly canEdit = inject(AuthService).canEdit;
  protected readonly showAddUrl = signal(false);

  /** The document whose details are open; its row keeps the drawer up to date (live status, counts). */
  protected readonly selectedId = signal<string | null>(null);
  protected readonly selectedDocument = computed(
    () => this.documents().find((d) => d.id === this.selectedId()) ?? null,
  );

  // ---- What is listed: the search box's text is applied after a short pause ----
  protected readonly searchText = signal('');
  private readonly search = signal('');
  protected readonly statusFilter = signal<DocumentStatusFilter | null>(null);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly filtering = computed(
    () => this.search() !== '' || this.statusFilter() !== null,
  );

  private readonly query = computed(() => ({
    botId: this.currentBot.selectedId(),
    page: this.page(),
    pageSize: this.pageSize(),
    status: this.statusFilter(),
    search: this.search(),
  }));

  /** quiet: refresh the list in place (after a reconnect or a deletion) instead of showing placeholders. */
  private readonly reload = new Subject<{ quiet: boolean }>();

  protected readonly documents = signal<CitraDocument[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  /** A new page or filter is loading while the current rows stay visible (dimmed). */
  protected readonly refreshing = signal(false);
  /** Documents being retried or deleted right now (their buttons are disabled). */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** Placeholder rows shown while loading. */
  protected readonly skeletonRows = [1, 2, 3, 4];

  protected readonly pageSizes = PAGE_SIZES;
  protected readonly statusOptions = STATUS_FILTER_OPTIONS;
  protected readonly searchMaxLength = SEARCH_MAX_LENGTH;
  protected readonly statusBadge = statusBadge;
  protected readonly typeIcon = typeIcon;
  protected readonly fileSize = fileSize;
  protected readonly pageCount = pageCount;
  protected readonly chunkCount = chunkCount;

  constructor() {
    // Another bot: start from its first page, without the previous bot's search and filter.
    effect(() => {
      this.currentBot.selectedId();
      untracked(() => {
        this.page.set(1);
        this.statusFilter.set(null);
        this.searchText.set('');
        this.search.set('');
      });
    });

    toObservable(this.searchText)
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        map((text) => text.trim()),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((term) => {
        if (term !== this.search()) {
          this.search.set(term);
          this.page.set(1);
        }
      });

    // Loads what the query asks for; a newer query (another bot, page or filter) cancels an older load.
    let shownBotId: string | null = null;
    combineLatest([toObservable(this.query), this.reload.pipe(startWith({ quiet: false }))])
      .pipe(
        switchMap(([query, { quiet }]) => {
          if (!query.botId) {
            return of(null);
          }
          // Same bot as on screen: keep its rows visible while the new page/filter loads.
          const keepRows = this.state() === 'ready' && query.botId === shownBotId;
          if (!keepRows) {
            this.state.set('loading');
          } else if (!quiet) {
            this.refreshing.set(true);
          }
          return this.documentsApi
            .list(query.botId, {
              page: query.page,
              pageSize: query.pageSize,
              status: query.status,
              search: query.search,
            })
            .pipe(
              map((result): LoadResult => ({
                ok: true,
                documents: result.items,
                totalCount: result.totalCount,
              })),
              // The error interceptor already shows a toast; the page itself offers a retry. A failed quiet
              // refresh keeps the list that is shown (the next update or reconnect refreshes it again).
              catchError(() => {
                this.refreshing.set(false);
                return quiet && keepRows ? EMPTY : of<LoadResult>({ ok: false });
              }),
              tap((result) => {
                if (result.ok) {
                  shownBotId = query.botId;
                }
              }),
            );
        }),
        tap((result) => {
          this.refreshing.set(false);
          if (result === null) {
            this.documents.set([]);
            this.totalCount.set(0);
          } else if (result.ok) {
            this.documents.set(result.documents);
            this.totalCount.set(result.totalCount);
            this.state.set('ready');
          } else {
            this.state.set('error');
          }
        }),
        takeUntilDestroyed(),
      )
      .subscribe();

    this.uploads.uploaded.pipe(takeUntilDestroyed()).subscribe((document) => this.show(document));

    // Live: statuses change by themselves (Queued → Reading → … → Ready), other users' uploads and deletions
    // appear, and after a connection drop the list is refreshed, since changes made meanwhile were not sent.
    this.live.changed.pipe(takeUntilDestroyed()).subscribe((document) => this.update(document));
    this.live.removed.pipe(takeUntilDestroyed()).subscribe((removed) => this.remove(removed));
    this.live.resynced
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.reload.next({ quiet: true }));
    this.live.start();
  }

  protected openDetails(document: CitraDocument): void {
    this.selectedId.set(document.id);
  }

  protected upload(files: File[]): void {
    const botId = this.currentBot.selectedId();
    if (botId) {
      this.uploads.add(botId, files);
    }
  }

  protected onStatusChange(status: DocumentStatusFilter | null): void {
    this.statusFilter.set(status);
    this.page.set(1);
  }

  protected onPage(event: PaginatorState): void {
    this.pageSize.set(event.rows ?? DEFAULT_PAGE_SIZE);
    this.page.set((event.page ?? 0) + 1);
  }

  protected clearFilters(): void {
    this.searchText.set('');
    this.search.set('');
    this.statusFilter.set(null);
    this.page.set(1);
  }

  /**
   * A document the server just accepted (an upload, a web page, or a live update about a new one): it is
   * counted if it matches the filters, and shown at the top of the first page (the list is newest first) —
   * unless the user has switched to another bot meanwhile, or it is already listed.
   */
  protected show(document: CitraDocument): void {
    if (document.botId !== this.currentBot.selectedId() || this.state() !== 'ready') {
      return;
    }
    if (this.documents().some((d) => d.id === document.id)) {
      return;
    }
    if (!matchesFilters(document, this.statusFilter(), this.search())) {
      return;
    }
    this.totalCount.update((count) => count + 1);
    if (this.page() === 1) {
      this.documents.update((documents) => [document, ...documents].slice(0, this.pageSize()));
    }
  }

  /** "Retry" on a failed document: it goes through processing again (live updates show the progress). */
  protected retryDocument(document: CitraDocument): void {
    this.setBusy(document.id, true);
    this.documentsApi.reprocess(document.id).subscribe({
      next: (queued) => {
        this.setBusy(document.id, false);
        this.replace(queued);
      },
      // The error interceptor shows the reason (e.g. it is already being processed).
      error: () => this.setBusy(document.id, false),
    });
  }

  protected confirmDelete(document: CitraDocument): void {
    this.confirmation.confirm({
      header: 'Delete document?',
      message: `"${document.fileName}" and everything the bot learned from it will be removed. This cannot be undone.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger', icon: 'pi pi-trash' },
      rejectButtonProps: { severity: 'secondary', text: true },
      defaultFocus: 'reject',
      accept: () => this.delete(document),
    });
  }

  protected isBusy(id: string): boolean {
    return this.busy().has(id);
  }

  private delete(document: CitraDocument): void {
    this.setBusy(document.id, true);
    this.documentsApi.delete(document.id).subscribe({
      next: () => {
        this.setBusy(document.id, false);
        this.remove({ id: document.id, botId: document.botId });
        this.notifications.success('Document deleted', `"${document.fileName}" was removed.`);
      },
      error: () => this.setBusy(document.id, false),
    });
  }

  /** A live update: the document's newest state replaces its row, or it is added if it is new here. */
  private update(document: CitraDocument): void {
    // Rows shown are always the current bot's, so replacing one needs no bot check; show() checks new ones.
    if (this.documents().some((d) => d.id === document.id)) {
      this.replace(document);
    } else {
      this.show(document);
    }
  }

  private replace(document: CitraDocument): void {
    this.documents.update((documents) =>
      documents.map((d) => (d.id === document.id ? document : d)),
    );
  }

  /**
   * A document is gone (deleted here, by someone else, or with its bot). If later pages hold more documents,
   * the page is refilled; if it was the last one on a later page, the page before is shown.
   */
  private remove(removed: RemovedDocument): void {
    if (!this.documents().some((d) => d.id === removed.id)) {
      return;
    }
    this.documents.update((documents) => documents.filter((d) => d.id !== removed.id));
    this.totalCount.update((count) => Math.max(0, count - 1));

    const shownBefore = (this.page() - 1) * this.pageSize();
    if (this.documents().length === 0 && this.page() > 1) {
      this.page.update((page) => page - 1);
    } else if (this.totalCount() > shownBefore + this.documents().length) {
      this.reload.next({ quiet: true });
    }
  }

  private setBusy(id: string, busy: boolean): void {
    this.busy.update((ids) => {
      const next = new Set(ids);
      if (busy) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  protected retry(): void {
    this.reload.next({ quiet: false });
  }
}
