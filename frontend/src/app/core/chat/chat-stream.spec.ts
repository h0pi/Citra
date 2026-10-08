import { TestBed } from '@angular/core/testing';
import { AuthService } from '@core/auth/auth.service';
import { ChatStreamEvent } from '@core/models/chat.models';
import { environment } from '@env/environment';
import { Observable, of, throwError } from 'rxjs';

import {
  CONNECTION_LOST_MESSAGE,
  ChatStream,
  ChatStreamError,
  FETCH,
  INCOMPLETE_MESSAGE,
  SESSION_ENDED_MESSAGE,
  UNAVAILABLE_MESSAGE,
  UNREACHABLE_MESSAGE,
} from './chat-stream';

const source = {
  number: 1,
  chunkId: 'c1',
  documentId: 'd1',
  documentName: 'Handbook.pdf',
  pageNumber: 3,
  heading: null,
  content: 'Returns are accepted within 30 days.',
  charStart: 0,
  charEnd: 36,
  score: 0.03,
};

function sse(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

const START = sse('start', { conversationId: 'conv', questionId: 'q', searchQuery: 'returns' });
const CITATIONS = sse('citations', { sources: [source] });
const DONE = sse('done', {
  messageId: 'm',
  content: 'Within 30 days [1].',
  wasAnswered: true,
  citedSources: [1],
});

/** A text/event-stream response delivering these pieces. */
function streamed(...pieces: string[]): Response {
  const encoder = new TextEncoder();
  return new Response(
    new ReadableStream<Uint8Array>({
      start(controller) {
        pieces.forEach((piece) => controller.enqueue(encoder.encode(piece)));
        controller.close();
      },
    }),
    { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
  );
}

function problem(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

/** Everything the observable did, once it has finished. */
function collect(source$: Observable<ChatStreamEvent>) {
  return new Promise<{ events: ChatStreamEvent[]; error?: ChatStreamError; completed: boolean }>(
    (resolve) => {
      const events: ChatStreamEvent[] = [];
      source$.subscribe({
        next: (event) => events.push(event),
        error: (error: ChatStreamError) => resolve({ events, error, completed: false }),
        complete: () => resolve({ events, completed: true }),
      });
    },
  );
}

describe('ChatStream', () => {
  let fetchMock: ReturnType<typeof vi.fn<typeof fetch>>;
  let auth: {
    getAccessToken: ReturnType<typeof vi.fn>;
    refreshAccessToken: ReturnType<typeof vi.fn>;
  };
  let stream: ChatStream;

  beforeEach(() => {
    fetchMock = vi.fn<typeof fetch>();
    auth = {
      getAccessToken: vi.fn(() => 'token-1'),
      refreshAccessToken: vi.fn(() => of('token-2')),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: FETCH, useValue: fetchMock },
        { provide: AuthService, useValue: auth },
      ],
    });
    stream = TestBed.inject(ChatStream);
  });

  function request(call = 0): { url: string; init: RequestInit; headers: Record<string, string> } {
    const [url, init] = fetchMock.mock.calls[call] as [string, RequestInit];
    return { url, init, headers: init.headers as Record<string, string> };
  }

  it('posts the question with the access token and streams the answer’s events in order', async () => {
    fetchMock.mockResolvedValue(
      streamed(
        START,
        CITATIONS,
        sse('token', { text: 'Within ' }),
        sse('token', { text: '30 days' }),
        DONE,
      ),
    );

    const result = await collect(stream.ask('bot/1', 'What about returns?'));

    const { url, init, headers } = request();
    expect(url).toBe(`${environment.apiUrl}/api/bots/bot%2F1/chat`);
    expect(init.method).toBe('POST');
    expect(headers).toEqual({
      'Content-Type': 'application/json',
      Accept: 'text/event-stream, application/problem+json',
      Authorization: 'Bearer token-1',
    });
    expect(JSON.parse(init.body as string)).toEqual({ question: 'What about returns?' });
    expect(init.signal).toBeInstanceOf(AbortSignal);

    expect(result.completed).toBe(true);
    expect(result.events).toEqual([
      { type: 'start', conversationId: 'conv', questionId: 'q', searchQuery: 'returns' },
      { type: 'citations', sources: [source] },
      { type: 'token', text: 'Within ' },
      { type: 'token', text: '30 days' },
      {
        type: 'done',
        messageId: 'm',
        content: 'Within 30 days [1].',
        wasAnswered: true,
        citedSources: [1],
      },
    ]);
  });

  it('continues a chat when given its id', async () => {
    fetchMock.mockResolvedValue(streamed(START, CITATIONS, DONE));

    await collect(stream.ask('b1', 'And exchanges?', 'conv'));
    expect(request().init.signal!.aborted).toBe(false); // nothing to cancel once finished

    expect(JSON.parse(request().init.body as string)).toEqual({
      conversationId: 'conv',
      question: 'And exchanges?',
    });
  });

  it('starts a new chat when the id is null or empty', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(streamed(START, CITATIONS, DONE)));

    await collect(stream.ask('b1', 'Hi', null));
    await collect(stream.ask('b1', 'Hi', ''));

    expect(request(0).init.body).toBe('{"question":"Hi"}');
    expect(request(1).init.body).toBe('{"question":"Hi"}');
  });

  it('sends no Authorization header when not signed in, and does not try to refresh', async () => {
    auth.getAccessToken.mockReturnValue(null);
    fetchMock.mockResolvedValue(problem(401, { title: 'Unauthorized', status: 401 }));

    const result = await collect(stream.ask('b1', 'Hi'));

    expect(request().headers).not.toHaveProperty('Authorization');
    expect(auth.refreshAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.error?.status).toBe(401);
  });

  it('refreshes an expired token once and asks again with the new one', async () => {
    fetchMock
      .mockResolvedValueOnce(problem(401, { title: 'Unauthorized', status: 401 }))
      .mockResolvedValueOnce(streamed(START, CITATIONS, DONE));

    const result = await collect(stream.ask('b1', 'Hi'));

    expect(auth.refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(request(1).headers['Authorization']).toBe('Bearer token-2');
    expect(request(1).init.body).toBe(request(0).init.body);
    expect(result.completed).toBe(true);
    expect(result.events.map((e) => e.type)).toEqual(['start', 'citations', 'done']);
  });

  it('does not refresh a second time when the new token is refused too', async () => {
    fetchMock.mockResolvedValue(problem(401, { title: 'Unauthorized', status: 401 }));

    const result = await collect(stream.ask('b1', 'Hi'));

    expect(auth.refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.error).toBeInstanceOf(ChatStreamError);
    expect(result.error?.status).toBe(401);
  });

  it('says the session ended when the token cannot be refreshed', async () => {
    auth.refreshAccessToken.mockReturnValue(throwError(() => new Error('refresh failed')));
    fetchMock.mockResolvedValue(problem(401, { title: 'Unauthorized', status: 401 }));

    const result = await collect(stream.ask('b1', 'Hi'));

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.error?.message).toBe(SESSION_ENDED_MESSAGE);
    expect(result.error?.status).toBe(401);
  });

  it('shows the reason a question was refused', async () => {
    fetchMock.mockResolvedValue(
      problem(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { question: ['Question must be 4000 characters or fewer.'] },
      }),
    );
    let result = await collect(stream.ask('b1', 'x'));
    expect(result.error?.message).toBe('Question must be 4000 characters or fewer.');
    expect(result.error?.status).toBe(400);

    fetchMock.mockResolvedValue(
      problem(404, {
        title: 'Resource not found.',
        status: 404,
        detail: "Bot 'b1' was not found.",
      }),
    );
    result = await collect(stream.ask('b1', 'x'));
    expect(result.error?.message).toBe("Bot 'b1' was not found.");
    expect(result.error?.status).toBe(404);
    expect(result.events).toEqual([]);
  });

  it('explains that the AI service is down (503), with the server’s words when it gave them', async () => {
    fetchMock.mockResolvedValue(
      problem(503, { title: 'Service unavailable.', status: 503, detail: 'Try again soon.' }),
    );
    let result = await collect(stream.ask('b1', 'x'));
    expect(result.error?.message).toBe('Service unavailable. Try again soon.');
    expect(result.error?.status).toBe(503);

    fetchMock.mockResolvedValue(new Response('<html>Bad gateway</html>', { status: 503 }));
    result = await collect(stream.ask('b1', 'x'));
    expect(result.error?.message).toBe(UNAVAILABLE_MESSAGE);
  });

  it('hides server details of other 5xx errors', async () => {
    fetchMock.mockResolvedValue(
      problem(500, { title: 'Server error', status: 500, detail: 'NullReferenceException at …' }),
    );

    const result = await collect(stream.ask('b1', 'x'));

    expect(result.error?.message).toBe('The question could not be answered. Please try again.');
    expect(result.error?.status).toBe(500);
  });

  it('says the server cannot be reached when the request fails', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await collect(stream.ask('b1', 'x'));

    expect(result.error).toBeInstanceOf(ChatStreamError);
    expect(result.error?.message).toBe(UNREACHABLE_MESSAGE);
    expect(result.error?.status).toBe(0);
  });

  it('fails with the server’s message after the tokens so far when answering breaks', async () => {
    fetchMock.mockResolvedValue(
      streamed(
        START,
        sse('token', { text: 'Partial' }),
        sse('error', { message: 'Something went wrong while answering. Please try again.' }),
        sse('token', { text: 'ignored' }),
      ),
    );

    const result = await collect(stream.ask('b1', 'x'));

    expect(result.events.map((e) => e.type)).toEqual(['start', 'token']);
    expect(result.error?.message).toBe('Something went wrong while answering. Please try again.');
    expect(result.error?.status).toBeNull();
  });

  it('uses the standard message for an error event without one', async () => {
    fetchMock.mockResolvedValue(streamed(START, sse('error', {})));

    const result = await collect(stream.ask('b1', 'x'));

    expect(result.error?.message).toBe(UNAVAILABLE_MESSAGE);
  });

  it('fails when the answer ends without being completed', async () => {
    fetchMock.mockResolvedValue(streamed(START, sse('token', { text: 'Half an ans' })));

    const result = await collect(stream.ask('b1', 'x'));

    expect(result.events).toHaveLength(2);
    expect(result.error?.message).toBe(INCOMPLETE_MESSAGE);
    expect(result.error?.status).toBeNull();
  });

  it('fails on an empty body and on damaged event data', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 200 }));
    expect((await collect(stream.ask('b1', 'x'))).error?.message).toBe(INCOMPLETE_MESSAGE);

    fetchMock.mockResolvedValue(streamed(START, 'event: token\ndata: {"text":\n\n', DONE));
    const damaged = await collect(stream.ask('b1', 'x'));
    expect(damaged.events.map((e) => e.type)).toEqual(['start']);
    expect(damaged.error?.message).toBe(INCOMPLETE_MESSAGE);
    expect(request(1).init.signal!.aborted).toBe(true); // the rest of a broken answer is not wanted

    fetchMock.mockResolvedValue(streamed(START, 'event: token\ndata: 42\n\n', DONE));
    expect((await collect(stream.ask('b1', 'x'))).error?.message).toBe(INCOMPLETE_MESSAGE);
  });

  it('ignores events it does not know', async () => {
    fetchMock.mockResolvedValue(streamed(START, sse('ping', { at: 1 }), 'data: {}\n\n', DONE));

    const result = await collect(stream.ask('b1', 'x'));

    expect(result.completed).toBe(true);
    expect(result.events.map((e) => e.type)).toEqual(['start', 'done']);
  });

  it('says the connection was lost when the body breaks midway', async () => {
    const encoder = new TextEncoder();
    let sent = false;
    fetchMock.mockResolvedValue(
      new Response(
        new ReadableStream<Uint8Array>({
          pull(controller) {
            if (!sent) {
              sent = true;
              controller.enqueue(encoder.encode(START));
            } else {
              controller.error(new TypeError('network error'));
            }
          },
        }),
        { status: 200 },
      ),
    );

    const result = await collect(stream.ask('b1', 'x'));

    expect(result.events.map((e) => e.type)).toEqual(['start']);
    expect(result.error?.message).toBe(CONNECTION_LOST_MESSAGE);
    expect(result.error?.status).toBeNull();
  });

  it('does nothing until subscribed, and asks again for each subscription', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(streamed(START, CITATIONS, DONE)));
    const answer$ = stream.ask('b1', 'x');
    expect(fetchMock).not.toHaveBeenCalled();

    await collect(answer$);
    await collect(answer$);

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('cancels the request on unsubscribe, without reporting an error', async () => {
    let controller!: ReadableStreamDefaultController<Uint8Array>;
    fetchMock.mockImplementation((_url, init) => {
      const signal = init!.signal!;
      const body = new ReadableStream<Uint8Array>({
        start(c) {
          controller = c;
          c.enqueue(new TextEncoder().encode(START));
        },
      });
      // Like the browser: an aborted request fails the body read.
      signal.addEventListener('abort', () =>
        controller.error(new DOMException('The user aborted a request.', 'AbortError')),
      );
      return Promise.resolve(new Response(body, { status: 200 }));
    });
    const next = vi.fn();
    const error = vi.fn();
    const complete = vi.fn();

    const subscription = stream.ask('b1', 'x').subscribe({ next, error, complete });
    await vi.waitFor(() => expect(next).toHaveBeenCalledTimes(1));
    subscription.unsubscribe();
    await new Promise((resolve) => setTimeout(resolve, 10));

    expect(request().init.signal!.aborted).toBe(true);
    expect(error).not.toHaveBeenCalled();
    expect(complete).not.toHaveBeenCalled();
  });

  it('cancels a request that has not been answered yet, without reporting an error', async () => {
    fetchMock.mockImplementation(
      (_url, init) =>
        new Promise((_resolve, reject) =>
          init!.signal!.addEventListener('abort', () =>
            reject(new DOMException('The user aborted a request.', 'AbortError')),
          ),
        ),
    );
    const error = vi.fn();

    const subscription = stream.ask('b1', 'x').subscribe({ error });
    subscription.unsubscribe();
    await new Promise((resolve) => setTimeout(resolve, 10));

    expect(request().init.signal!.aborted).toBe(true);
    expect(error).not.toHaveBeenCalled();
  });
});
