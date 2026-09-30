import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DocumentsApi } from '@core/documents/documents-api';
import { CitraDocument, DocumentChunk } from '@core/models/document.models';
import { ButtonModule } from 'primeng/button';
import { DrawerModule } from 'primeng/drawer';
import { SkeletonModule } from 'primeng/skeleton';
import { TagModule } from 'primeng/tag';
import { EMPTY, Subject, catchError, distinctUntilChanged, map, merge, switchMap, tap } from 'rxjs';

import { chunkCount, fileSize, pageCount, statusBadge, typeIcon } from '../document-display';

/** Chunks loaded per "Load more". */
export const CHUNKS_PAGE_SIZE = 20;

/**
 * A document's details in a side panel: what it is, where it came from, how processing went, and the chunks it
 * was split into — exactly the text the bot searches, so people can check that nothing is garbled or missing.
 * Follows the document it is given: when a live update turns it Ready, its chunks appear.
 */
@Component({
  selector: 'app-document-drawer',
  imports: [DatePipe, ButtonModule, DrawerModule, SkeletonModule, TagModule],
  templateUrl: './document-drawer.html',
  styleUrl: './document-drawer.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentDrawer {
  private readonly documentsApi = inject(DocumentsApi);

  /** The document shown; null closes the drawer. */
  readonly document = input<CitraDocument | null>(null);
  /** The user closed the drawer. */
  readonly closed = output<void>();

  protected readonly visible = computed(() => this.document() !== null);

  protected readonly chunks = signal<DocumentChunk[]>([]);
  protected readonly totalChunks = signal(0);
  protected readonly chunkState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly loadingMore = signal(false);
  /** Chunks shown in full; the others are clipped to a few lines. */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected readonly hasMore = computed(() => this.chunks().length < this.totalChunks());

  protected readonly statusBadge = statusBadge;
  protected readonly typeIcon = typeIcon;
  protected readonly fileSize = fileSize;
  protected readonly pageCount = pageCount;
  protected readonly chunkCount = chunkCount;
  protected readonly skeletons = [1, 2, 3];

  private readonly more = new Subject<void>();
  private readonly retryLoad = new Subject<void>();

  constructor() {
    // (Re)load from the first chunk when another document is shown, or when this one becomes Ready or is
    // processed again (its chunks change). A newer load cancels an older one.
    const version = computed(() => {
      const document = this.document();
      return document ? `${document.id}|${document.status}|${document.processedAt}` : null;
    });

    merge(
      toObservable(version).pipe(distinctUntilChanged()),
      this.retryLoad.pipe(map(() => version())),
    )
      .pipe(
        switchMap(() => {
          this.chunks.set([]);
          this.totalChunks.set(0);
          this.expanded.set(new Set());
          const document = this.document();
          if (!document || document.status !== 'Ready') {
            this.chunkState.set('idle');
            return EMPTY;
          }
          this.chunkState.set('loading');
          return this.load(document.id, 1).pipe(
            tap((page) => {
              this.chunks.set(page.items);
              this.totalChunks.set(page.totalCount);
              this.chunkState.set('ready');
            }),
            catchError(() => {
              this.chunkState.set('error');
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();

    // "Load more": the next page of the same document (ignored while one is loading).
    this.more
      .pipe(
        switchMap(() => {
          const document = this.document();
          if (!document || this.loadingMore()) {
            return EMPTY;
          }
          this.loadingMore.set(true);
          const nextPage = Math.floor(this.chunks().length / CHUNKS_PAGE_SIZE) + 1;
          return this.load(document.id, nextPage).pipe(
            tap((page) => {
              // Skip any chunk already shown (e.g. counts changed between pages).
              const shown = new Set(this.chunks().map((c) => c.id));
              this.chunks.update((chunks) => [
                ...chunks,
                ...page.items.filter((c) => !shown.has(c.id)),
              ]);
              this.totalChunks.set(page.totalCount);
              this.loadingMore.set(false);
            }),
            catchError(() => {
              this.loadingMore.set(false);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  protected loadMore(): void {
    this.more.next();
  }

  protected retry(): void {
    this.retryLoad.next();
  }

  protected toggle(chunk: DocumentChunk): void {
    this.expanded.update((ids) => {
      const next = new Set(ids);
      if (next.has(chunk.id)) {
        next.delete(chunk.id);
      } else {
        next.add(chunk.id);
      }
      return next;
    });
  }

  protected isExpanded(chunk: DocumentChunk): boolean {
    return this.expanded().has(chunk.id);
  }

  /** Long chunks are clipped until opened; short ones need no button. */
  protected isLong(chunk: DocumentChunk): boolean {
    return chunk.content.length > 400 || chunk.content.split('\n').length > 6;
  }

  protected onVisibleChange(visible: boolean): void {
    if (!visible) {
      this.closed.emit();
    }
  }

  private load(documentId: string, page: number) {
    return this.documentsApi.chunks(documentId, page, CHUNKS_PAGE_SIZE);
  }
}
