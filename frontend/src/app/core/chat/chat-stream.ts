import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, InjectionToken, inject } from '@angular/core';
import { AuthService } from '@core/auth/auth.service';
import { ChatStreamEvent } from '@core/models/chat.models';
import { isProblemDetails, toFormError } from '@core/models/problem-details';
import { environment } from '@env/environment';
import { Observable, firstValueFrom } from 'rxjs';

import { readServerSentEvents } from './server-sent-events';

/** The browser's fetch (replaced in tests). */
export const FETCH = new InjectionToken<typeof fetch>('fetch', {
  factory: () => globalThis.fetch.bind(globalThis),
});

/** Why a question could not be answered, in words for the user. */
export class ChatStreamError extends Error {
  constructor(
    message: string,
    /** The HTTP status when the request was refused before answering; null for failures while answering. */
    readonly status: number | null,
  ) {
    super(message);
    this.name = 'ChatStreamError';
  }
}

export const UNAVAILABLE_MESSAGE =
  'The AI service is not available right now. Please try again in a minute.';
export const CONNECTION_LOST_MESSAGE =
  'The connection was lost while the answer was being written. Please try again.';
export const INCOMPLETE_MESSAGE = 'The answer ended before it was complete. Please try again.';
export const SESSION_ENDED_MESSAGE = 'Your session has ended. Please sign in again.';
export const UNREACHABLE_MESSAGE =
  'Cannot reach the server. Check your internet connection and try again.';

const apiBase = environment.apiUrl.replace(/\/+$/, '');

/**
 * Asks a question and streams the answer (POST /api/bots/{botId}/chat, server-sent events). HttpClient cannot
 * read a response while it arrives, so this uses fetch with a readable stream, adding the access token itself
 * (refreshed once if it expired, like the auth interceptor does). Unsubscribing cancels the request, which stops
 * the answer on the server too.
 */
@Injectable({ providedIn: 'root' })
export class ChatStream {
  private readonly auth = inject(AuthService);
  private readonly fetch = inject(FETCH);

  /**
   * The answer's events (start, citations, token…, done), then completes. Fails with a {@link ChatStreamError}
   * when the question is refused (e.g. 400, 404, 503) or answering fails midway (after the tokens so far).
   */
  ask(
    botId: string,
    question: string,
    conversationId?: string | null,
  ): Observable<ChatStreamEvent> {
    return new Observable<ChatStreamEvent>((subscriber) => {
      const abort = new AbortController();
      let completed = false;
      const url = `${apiBase}/api/bots/${encodeURIComponent(botId)}/chat`;
      const body = JSON.stringify(conversationId ? { conversationId, question } : { question });

      void (async () => {
        try {
          const response = await this.post(url, body, abort.signal);
          if (!response.ok) {
            throw await refused(response);
          }
          if (!response.body) {
            throw new ChatStreamError(INCOMPLETE_MESSAGE, null);
          }

          let finished = false;
          for await (const { event, data } of readEvents(response.body)) {
            const payload = parse(data);
            if (event === 'error') {
              throw new ChatStreamError(String(payload['message'] ?? UNAVAILABLE_MESSAGE), null);
            }
            if (
              event === 'start' ||
              event === 'citations' ||
              event === 'token' ||
              event === 'done'
            ) {
              subscriber.next({ type: event, ...payload } as ChatStreamEvent);
              finished = event === 'done';
            }
          }

          if (!finished) {
            throw new ChatStreamError(INCOMPLETE_MESSAGE, null);
          }
          completed = true;
          subscriber.complete();
        } catch (error: unknown) {
          if (!abort.signal.aborted) {
            subscriber.error(error instanceof ChatStreamError ? error : unreachable(error));
          }
        }
      })();

      // Cancels an answer still on its way, or what is left of a broken one; a complete answer has nothing left.
      return () => {
        if (!completed) {
          abort.abort();
        }
      };
    });
  }

  /** Sends the question with the access token; an expired token is refreshed and the question sent once more. */
  private async post(url: string, body: string, signal: AbortSignal): Promise<Response> {
    const send = (token: string | null) =>
      this.fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          // Errors before the answer starts come as problem details (JSON); without it they lose their reason.
          Accept: 'text/event-stream, application/problem+json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body,
        signal,
      });

    const token = this.auth.getAccessToken();
    const response = await send(token);
    if (response.status !== 401 || !token) {
      return response;
    }

    // If the refresh fails, AuthService ends the session (back to the login page).
    let fresh: string;
    try {
      fresh = await firstValueFrom(this.auth.refreshAccessToken());
    } catch {
      throw new ChatStreamError(SESSION_ENDED_MESSAGE, 401);
    }
    return send(fresh);
  }
}

/** Events of the body; a broken connection while reading is reported in the user's words. */
async function* readEvents(body: ReadableStream<Uint8Array>) {
  try {
    yield* readServerSentEvents(body);
  } catch (error: unknown) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    throw new ChatStreamError(CONNECTION_LOST_MESSAGE, null);
  }
}

/** An event's JSON data; anything else means the answer arrived damaged. */
function parse(data: string): Record<string, unknown> {
  try {
    const payload: unknown = JSON.parse(data);
    if (typeof payload === 'object' && payload !== null) {
      return payload as Record<string, unknown>;
    }
  } catch {
    // Reported below.
  }
  throw new ChatStreamError(INCOMPLETE_MESSAGE, null);
}

/** A refused question (4xx/5xx before answering) with the server's reason when it gave one. */
async function refused(response: Response): Promise<ChatStreamError> {
  let problem: unknown = null;
  try {
    problem = await response.json();
  } catch {
    // Not JSON (e.g. a proxy's HTML page): the status decides the message.
  }

  if (response.status === 503) {
    const details = isProblemDetails(problem)
      ? [problem.title, problem.detail].filter(Boolean).join(' ')
      : '';
    return new ChatStreamError(details || UNAVAILABLE_MESSAGE, 503);
  }

  const { message, fieldErrors } = toFormError(
    new HttpErrorResponse({ status: response.status, error: problem }),
    'The question could not be answered. Please try again.',
  );
  return new ChatStreamError(Object.values(fieldErrors)[0]?.[0] ?? message, response.status);
}

/** fetch itself failed (offline, server down, CORS): nothing reached the server. */
function unreachable(error: unknown): ChatStreamError {
  if (error instanceof ChatStreamError) {
    return error;
  }
  return new ChatStreamError(UNREACHABLE_MESSAGE, 0);
}
