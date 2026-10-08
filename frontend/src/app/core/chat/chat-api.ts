import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import { Conversation, ConversationSummary, MessageFeedback } from '@core/models/chat.models';
import { PagedResult } from '@core/models/document.models';
import { ApiService } from '@core/services/api.service';
import { Observable } from 'rxjs';

/** The chat page shows its own error states (with "Try again"), so failed loads need no toast. */
const PAGE_REQUEST = { context: new HttpContext().set(SKIP_ERROR_TOAST, true) };

/** Chats per page in the list. */
export const CONVERSATIONS_PAGE_SIZE = 30;

/** HTTP calls for the chat page: the user's chats with a bot, and one chat with its messages. */
@Injectable({ providedIn: 'root' })
export class ChatApi {
  private readonly api = inject(ApiService);

  /** The user's chats with a bot, most recently active first. */
  conversations(
    botId: string,
    page = 1,
    pageSize = CONVERSATIONS_PAGE_SIZE,
  ): Observable<PagedResult<ConversationSummary>> {
    return this.api.get<PagedResult<ConversationSummary>>(
      `/api/bots/${encodeURIComponent(botId)}/conversations`,
      { page, pageSize },
      PAGE_REQUEST,
    );
  }

  /** One chat with all its messages and their sources. */
  conversation(id: string): Observable<Conversation> {
    return this.api.get<Conversation>(
      `/api/conversations/${encodeURIComponent(id)}`,
      undefined,
      PAGE_REQUEST,
    );
  }

  rename(id: string, title: string): Observable<void> {
    return this.api.put<void>(`/api/conversations/${encodeURIComponent(id)}/title`, { title });
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/api/conversations/${encodeURIComponent(id)}`);
  }

  /** Rates an answer (None removes the rating). */
  rate(messageId: string, feedback: MessageFeedback): Observable<void> {
    return this.api.put<void>(`/api/messages/${encodeURIComponent(messageId)}/feedback`, {
      feedback,
    });
  }
}
