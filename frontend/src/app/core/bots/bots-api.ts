import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import {
  Bot,
  BotSearchResult,
  BotSummary,
  CreateBotRequest,
  UpdateBotRequest,
} from '@core/models/bot.models';
import { ApiService, RequestOptions } from '@core/services/api.service';
import { Observable } from 'rxjs';

/**
 * Create, update, delete and search are sent from forms/dialogs, which show errors themselves instead of a toast.
 */
const FORM_REQUEST = { context: new HttpContext().set(SKIP_ERROR_TOAST, true) };

/** HTTP calls for /api/bots. Used by the bots pages and the "current bot" selector. */
@Injectable({ providedIn: 'root' })
export class BotsApi {
  private readonly api = inject(ApiService);

  list(options?: RequestOptions): Observable<BotSummary[]> {
    return this.api.get<BotSummary[]>('/api/bots', undefined, options);
  }

  get(id: string): Observable<Bot> {
    return this.api.get<Bot>(`/api/bots/${encodeURIComponent(id)}`);
  }

  /** Claude models a bot can be switched to (for the settings dropdown). */
  models(): Observable<string[]> {
    return this.api.get<string[]>('/api/bots/models');
  }

  create(request: CreateBotRequest): Observable<Bot> {
    return this.api.post<Bot>('/api/bots', request, FORM_REQUEST);
  }

  update(id: string, request: UpdateBotRequest): Observable<Bot> {
    return this.api.put<Bot>(`/api/bots/${encodeURIComponent(id)}`, request, FORM_REQUEST);
  }

  delete(id: string): Observable<void> {
    return this.api.delete(`/api/bots/${encodeURIComponent(id)}`, FORM_REQUEST);
  }

  /** Search playground (owners and admins): the passages the bot would use for a question, with scores. */
  search(id: string, query: string, topK: number): Observable<BotSearchResult> {
    return this.api.get<BotSearchResult>(
      `/api/bots/${encodeURIComponent(id)}/search`,
      { q: query.trim(), topK },
      FORM_REQUEST,
    );
  }
}
