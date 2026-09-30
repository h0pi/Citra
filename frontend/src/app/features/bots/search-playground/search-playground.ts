import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { BotsApi } from '@core/bots/bots-api';
import { BotSearchResult, SEARCH_LIMITS, SearchResult } from '@core/models/bot.models';
import { isProblemDetails, toFormError } from '@core/models/problem-details';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { ProgressBarModule } from 'primeng/progressbar';
import { SelectModule } from 'primeng/select';
import { SkeletonModule } from 'primeng/skeleton';
import { TagModule } from 'primeng/tag';
import { EMPTY, Subject, catchError, switchMap, tap } from 'rxjs';

/** Choices for "how many results". */
export const TOP_K_OPTIONS = [3, 5, 8, 10, 20] as const;
export const DEFAULT_TOP_K = 8;

interface SearchRequest {
  botId: string;
  query: string;
  topK: number;
}

/**
 * Search playground (owners and admins): type a question and see exactly which passages the bot would answer
 * from, and how each one scored in the meaning search, the keyword search and combined. For checking that the
 * right documents are found before people start chatting, and for spotting documents that need fixing.
 */
@Component({
  selector: 'app-search-playground',
  imports: [
    FormsModule,
    ButtonModule,
    InputTextModule,
    MessageModule,
    ProgressBarModule,
    SelectModule,
    SkeletonModule,
    TagModule,
  ],
  templateUrl: './search-playground.html',
  styleUrl: './search-playground.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchPlayground {
  private readonly botsApi = inject(BotsApi);

  /** The bot searched. Changing it clears the results (they belong to the previous bot). */
  readonly botId = input.required<string>();

  protected readonly limits = SEARCH_LIMITS;
  protected readonly topKOptions = TOP_K_OPTIONS.map((k) => ({ label: `${k} results`, value: k }));
  protected readonly skeletons = [1, 2, 3];

  protected readonly query = signal('');
  protected readonly topK = signal<number>(DEFAULT_TOP_K);
  protected readonly state = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly result = signal<BotSearchResult | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  /** Passages shown in full; the others are clipped to a few lines. */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected readonly canSearch = computed(() => {
    const query = this.query();
    return query.trim().length > 0 && query.length <= SEARCH_LIMITS.query;
  });

  /** A search to run, or null to cancel the running one (the bot changed). The newest always wins. */
  private readonly searches = new Subject<SearchRequest | null>();
  private lastRequest: SearchRequest | null = null;

  constructor() {
    this.searches
      .pipe(
        switchMap((request) => {
          if (!request) {
            return EMPTY;
          }
          this.state.set('loading');
          this.errorMessage.set(null);
          return this.botsApi.search(request.botId, request.query, request.topK).pipe(
            tap((result) => {
              this.result.set(result);
              this.expanded.set(new Set());
              this.state.set('ready');
            }),
            catchError((error: unknown) => {
              this.errorMessage.set(errorText(error));
              this.state.set('error');
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();

    // Another bot: its results would be misleading, so start over (a running search is cancelled; the old
    // result and error stay hidden until the next search replaces them).
    effect(() => {
      this.botId();
      untracked(() => {
        this.searches.next(null);
        this.state.set('idle');
      });
    });
  }

  protected search(): void {
    if (!this.canSearch()) {
      return;
    }
    this.lastRequest = { botId: this.botId(), query: this.query().trim(), topK: this.topK() };
    this.searches.next(this.lastRequest);
  }

  protected retry(): void {
    if (this.lastRequest) {
      this.searches.next(this.lastRequest);
    }
  }

  protected toggle(result: SearchResult): void {
    this.expanded.update((ids) => {
      const next = new Set(ids);
      if (next.has(result.chunkId)) {
        next.delete(result.chunkId);
      } else {
        next.add(result.chunkId);
      }
      return next;
    });
  }

  protected isExpanded(result: SearchResult): boolean {
    return this.expanded().has(result.chunkId);
  }

  /** Long passages are clipped until opened; short ones need no button. */
  protected isLong(result: SearchResult): boolean {
    return result.content.length > 400 || result.content.split('\n').length > 6;
  }

  /** A score with a fixed number of decimals ("–" when there is none). */
  protected fixed(value: number | null, digits: number): string {
    return value === null ? '–' : value.toFixed(digits);
  }

  /** Similarity as a 0–100 bar (negative similarities show as empty). */
  protected similarityPercent(result: SearchResult): number {
    return Math.round(Math.max(0, result.similarity ?? 0) * 100);
  }
}

/** A readable reason: the AI service being down says so; a refused question shows the server's reason. */
function errorText(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.status === 503) {
    const problem = isProblemDetails(error.error) ? error.error : null;
    return (
      [problem?.title, problem?.detail].filter(Boolean).join(' ') ||
      'The AI service is not available right now. Please try again in a minute.'
    );
  }
  const { message, fieldErrors } = toFormError(error, 'The search failed. Please try again.');
  return Object.values(fieldErrors)[0]?.[0] ?? message;
}
