import { HttpContext } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { AuthService } from '@core/auth/auth.service';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import { SKIP_LOADING } from '@core/interceptors/loading.interceptor';
import { BotSummary } from '@core/models/bot.models';
import { Subscription } from 'rxjs';

import { BotsApi } from './bots-api';

/** Background request: the selector shows its own state, so no toast and no loading bar. */
const SILENT = { context: new HttpContext().set(SKIP_ERROR_TOAST, true).set(SKIP_LOADING, true) };

const STORAGE_PREFIX = 'citra.currentBot.';

/**
 * The bot the user is working on, chosen in the top bar. Documents, Chat and Evaluation show data
 * for this bot. The choice is remembered per user between visits; if that bot no longer exists,
 * the first bot is chosen instead.
 */
@Injectable({ providedIn: 'root' })
export class CurrentBot {
  private readonly auth = inject(AuthService);
  private readonly botsApi = inject(BotsApi);

  private readonly _bots = signal<BotSummary[]>([]);
  private readonly _selectedId = signal<string | null>(null);
  private readonly _status = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  private request: Subscription | null = null;

  /** Only the user id matters: a token refresh (new user object, same user) must not reload. */
  private readonly userId = computed(() => this.auth.currentUser()?.id ?? null);

  readonly bots = this._bots.asReadonly();
  readonly status = this._status.asReadonly();
  readonly selectedId = this._selectedId.asReadonly();
  readonly bot = computed(() => this._bots().find((b) => b.id === this._selectedId()) ?? null);

  constructor() {
    // Log in → load this user's bots; log out → forget everything.
    effect(() => {
      const userId = this.userId();
      untracked(() => (userId ? this.refresh() : this.clear()));
    });
  }

  /**
   * Reloads the bot list, e.g. after a bot was created, renamed or deleted.
   * `select` makes that bot current once loaded (used right after creating one).
   */
  refresh(select?: string): void {
    this.request?.unsubscribe();
    this._status.set(this._bots().length > 0 ? 'ready' : 'loading');

    this.request = this.botsApi.list(SILENT).subscribe({
      next: (bots) => {
        this._bots.set(bots);
        const preferred = select ?? this._selectedId() ?? this.readStored();
        const chosen = bots.find((b) => b.id === preferred) ?? bots[0] ?? null;
        this.setSelected(chosen?.id ?? null);
        this._status.set('ready');
      },
      error: () => this._status.set('error'),
    });
  }

  select(id: string): void {
    if (this._bots().some((b) => b.id === id)) {
      this.setSelected(id);
    }
  }

  private clear(): void {
    this.request?.unsubscribe();
    this.request = null;
    this._bots.set([]);
    this._selectedId.set(null);
    this._status.set('idle');
  }

  private setSelected(id: string | null): void {
    this._selectedId.set(id);
    const key = this.storageKey();
    if (!key) {
      return;
    }
    try {
      if (id) {
        localStorage.setItem(key, id);
      } else {
        localStorage.removeItem(key);
      }
    } catch {
      // Storage unavailable (private mode): the choice lasts for this visit only.
    }
  }

  private readStored(): string | null {
    const key = this.storageKey();
    try {
      return key ? localStorage.getItem(key) : null;
    } catch {
      return null;
    }
  }

  /** Per user, so two people sharing a browser each keep their own choice. */
  private storageKey(): string | null {
    const userId = this.userId();
    return userId ? STORAGE_PREFIX + userId : null;
  }
}
