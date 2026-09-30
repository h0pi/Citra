import { TestBed } from '@angular/core/testing';
import { AuthService } from '@core/auth/auth.service';
import { CitraDocument } from '@core/models/document.models';
import { environment } from '@env/environment';
import { of } from 'rxjs';

import {
  DOCUMENTS_HUB_CONNECTION,
  DocumentUpdates,
  DocumentsHubConnection,
  RemovedDocument,
  expiresSoon,
} from './document-updates';

/** A token whose "exp" is `secondsLeft` from now (only the payload matters here). */
function token(secondsLeft: number): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + secondsLeft }))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
  return `header.${payload}.signature`;
}

/** Stands in for a SignalR connection: records handlers, lets the test decide how start() ends. */
class FakeConnection implements DocumentsHubConnection {
  readonly handlers = new Map<string, (...args: unknown[]) => void>();
  reconnecting?: () => void;
  reconnected?: () => void;
  closed?: () => void;
  startResult: Promise<void> = Promise.resolve();
  stopped = false;

  constructor(
    readonly url: string,
    readonly accessToken: () => Promise<string>,
  ) {}

  on(name: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(name, handler);
  }
  onreconnecting(callback: () => void): void {
    this.reconnecting = callback;
  }
  onreconnected(callback: () => void): void {
    this.reconnected = callback;
  }
  onclose(callback: () => void): void {
    this.closed = callback;
  }
  start(): Promise<void> {
    return this.startResult;
  }
  stop(): Promise<void> {
    this.stopped = true;
    return Promise.resolve();
  }
}

describe('DocumentUpdates', () => {
  let connections: FakeConnection[];
  let nextStart: () => Promise<void>;
  const auth = { getAccessToken: vi.fn<() => string | null>(), refreshAccessToken: vi.fn() };

  beforeEach(() => {
    vi.useFakeTimers();
    connections = [];
    nextStart = () => Promise.resolve();
    auth.getAccessToken.mockReset();
    auth.refreshAccessToken.mockReset();
    TestBed.configureTestingModule({
      providers: [
        DocumentUpdates,
        { provide: AuthService, useValue: auth },
        {
          provide: DOCUMENTS_HUB_CONNECTION,
          useValue: (url: string, accessToken: () => Promise<string>) => {
            const connection = new FakeConnection(url, accessToken);
            connection.startResult = nextStart();
            connections.push(connection);
            return connection;
          },
        },
      ],
    });
  });

  afterEach(() => vi.useRealTimers());

  async function started() {
    const updates = TestBed.inject(DocumentUpdates);
    updates.start();
    await vi.advanceTimersByTimeAsync(0);
    return updates;
  }

  it('connects to the documents hub and passes on changes and removals', async () => {
    const updates = await started();
    const changed: CitraDocument[] = [];
    const removed: RemovedDocument[] = [];
    updates.changed.subscribe((d) => changed.push(d));
    updates.removed.subscribe((r) => removed.push(r));

    expect(connections).toHaveLength(1);
    expect(connections[0].url).toBe(`${environment.apiUrl}/hubs/documents`);
    expect(updates.state()).toBe('connected');

    const document = { id: 'd1', botId: 'b1', status: 'Ready' } as CitraDocument;
    connections[0].handlers.get('DocumentChanged')!(document);
    connections[0].handlers.get('DocumentRemoved')!({ id: 'd2', botId: 'b1' });

    expect(changed).toEqual([document]);
    expect(removed).toEqual([{ id: 'd2', botId: 'b1' }]);
  });

  it('does not ask for a refresh on the first connection (the page has just loaded its list)', async () => {
    const updates = TestBed.inject(DocumentUpdates);
    let resyncs = 0;
    updates.resynced.subscribe(() => resyncs++);

    updates.start();
    await vi.advanceTimersByTimeAsync(0);

    expect(updates.state()).toBe('connected');
    expect(resyncs).toBe(0);
  });

  it('starts only once', async () => {
    const updates = await started();

    updates.start();

    expect(connections).toHaveLength(1);
  });

  it('reports a short interruption and asks for a refresh once it is back', async () => {
    const updates = await started();
    let resyncs = 0;
    updates.resynced.subscribe(() => resyncs++);

    connections[0].reconnecting!();
    expect(updates.state()).toBe('reconnecting');

    connections[0].reconnected!();
    expect(updates.state()).toBe('connected');
    expect(resyncs).toBe(1);
  });

  it('starts over when the connection closes for good (e.g. the server ended it), then refreshes', async () => {
    const updates = await started();
    let resyncs = 0;
    updates.resynced.subscribe(() => resyncs++);

    connections[0].closed!();
    expect(updates.state()).toBe('offline');

    await vi.advanceTimersByTimeAsync(DocumentUpdates.RestartDelayMs);
    expect(connections).toHaveLength(2);
    expect(updates.state()).toBe('connected');
    expect(resyncs).toBe(1);
  });

  it('keeps trying when the first connection fails', async () => {
    nextStart = () => Promise.reject(new Error('API down'));
    const updates = await started();
    expect(updates.state()).toBe('offline');

    nextStart = () => Promise.resolve();
    await vi.advanceTimersByTimeAsync(DocumentUpdates.RestartDelayMs);

    expect(connections).toHaveLength(2);
    expect(updates.state()).toBe('connected');
  });

  it('closes the connection when the page is left, and does not start again', async () => {
    await started();

    TestBed.resetTestingModule();
    await vi.advanceTimersByTimeAsync(DocumentUpdates.RestartDelayMs * 2);

    expect(connections[0].stopped).toBe(true);
    expect(connections).toHaveLength(1);
  });

  it('connects with the current token, or renews one about to expire', async () => {
    await started();
    const accessToken = connections[0].accessToken;

    const fresh = token(3600);
    auth.getAccessToken.mockReturnValue(fresh);
    await expect(accessToken()).resolves.toBe(fresh);
    expect(auth.refreshAccessToken).not.toHaveBeenCalled();

    auth.getAccessToken.mockReturnValue(token(30));
    auth.refreshAccessToken.mockReturnValue(of('renewed'));
    await expect(accessToken()).resolves.toBe('renewed');

    auth.getAccessToken.mockReturnValue(null);
    await expect(accessToken()).resolves.toBe('renewed');
  });

  it('reads token expiry, treating unreadable tokens as expired', () => {
    expect(expiresSoon(token(3600))).toBe(false);
    expect(expiresSoon(token(59))).toBe(true);
    expect(expiresSoon('not-a-token')).toBe(true);
    expect(expiresSoon(`h.${btoa('{}')}.s`)).toBe(true);
  });
});
