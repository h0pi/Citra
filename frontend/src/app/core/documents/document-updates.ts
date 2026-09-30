import { DestroyRef, Injectable, InjectionToken, inject, signal } from '@angular/core';
import { AuthService } from '@core/auth/auth.service';
import { CitraDocument } from '@core/models/document.models';
import { ApiService } from '@core/services/api.service';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';

/** A document deleted by someone (or with its bot): which one, and from which bot's list. */
export interface RemovedDocument {
  id: string;
  botId: string;
}

/** The parts of a SignalR connection this service uses (tests replace it with a fake). */
export type DocumentsHubConnection = Pick<
  HubConnection,
  'on' | 'onreconnecting' | 'onreconnected' | 'onclose' | 'start' | 'stop'
>;

/** Builds the connection to the API's documents hub. */
export const DOCUMENTS_HUB_CONNECTION = new InjectionToken<
  (url: string, accessToken: () => Promise<string>) => DocumentsHubConnection
>('DOCUMENTS_HUB_CONNECTION', {
  providedIn: 'root',
  factory: () => (url, accessToken) =>
    new HubConnectionBuilder()
      .withUrl(url, { accessTokenFactory: accessToken })
      // Short outages (Wi-Fi, API restart) are bridged by retrying: at once, then 2, 5, 10, then every 30 s.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
          [0, 2_000, 5_000, 10_000][previousRetryCount] ?? 30_000,
      })
      .configureLogging(LogLevel.Warning)
      .build(),
});

/** Hub path (Citra.Api DocumentsHub.Path). */
const HUB_PATH = '/hubs/documents';

/** A token this close to expiring is renewed before connecting (it must outlive the handshake). */
const TOKEN_MARGIN_MS = 60_000;

export type LiveState = 'connecting' | 'connected' | 'reconnecting' | 'offline';

/**
 * Live document updates from the API (SignalR): every change of a document of the user's company — status,
 * counts, name, error — arrives the moment it is saved, and deletions too. After an interruption the page is
 * told to reload its list, since changes made meanwhile were not sent. Provided by the documents page: the
 * connection lives only while the page is open.
 */
@Injectable()
export class DocumentUpdates {
  /** Wait before starting over after the connection closed for good (e.g. the server ended it). */
  static readonly RestartDelayMs = 5_000;

  private readonly auth = inject(AuthService);
  private readonly url = inject(ApiService).url(HUB_PATH);
  private readonly createConnection = inject(DOCUMENTS_HUB_CONNECTION);

  private connection: DocumentsHubConnection | null = null;
  private stopped = false;
  private everConnected = false;
  private restartTimer: ReturnType<typeof setTimeout> | null = null;

  private readonly _state = signal<LiveState>('connecting');
  readonly state = this._state.asReadonly();

  readonly changed = new Subject<CitraDocument>();
  readonly removed = new Subject<RemovedDocument>();
  /** The connection is back after an interruption: reload the list to catch up on missed changes. */
  readonly resynced = new Subject<void>();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  start(): void {
    if (this.connection || this.stopped) {
      return;
    }
    const connection = this.createConnection(this.url, () => this.freshAccessToken());
    connection.on('DocumentChanged', (document: CitraDocument) => this.changed.next(document));
    connection.on('DocumentRemoved', (document: RemovedDocument) => this.removed.next(document));
    connection.onreconnecting(() => this._state.set('reconnecting'));
    connection.onreconnected(() => {
      this._state.set('connected');
      this.resynced.next();
    });
    // Closed for good: automatic retries gave up, or the server ended it (e.g. the sign-in token expired).
    connection.onclose(() => {
      if (!this.stopped) {
        this.connection = null;
        this._state.set('offline');
        this.scheduleRestart();
      }
    });
    this.connection = connection;
    void this.connect(connection);
  }

  private async connect(connection: DocumentsHubConnection): Promise<void> {
    try {
      await connection.start();
    } catch {
      if (this.connection === connection && !this.stopped) {
        this.connection = null;
        this._state.set('offline');
        this.scheduleRestart();
      }
      return;
    }
    if (this.stopped) {
      return;
    }
    this._state.set('connected');
    if (this.everConnected) {
      this.resynced.next();
    }
    this.everConnected = true;
  }

  private scheduleRestart(): void {
    if (this.restartTimer === null) {
      this.restartTimer = setTimeout(() => {
        this.restartTimer = null;
        this.start();
      }, DocumentUpdates.RestartDelayMs);
    }
  }

  private stop(): void {
    this.stopped = true;
    if (this.restartTimer !== null) {
      clearTimeout(this.restartTimer);
    }
    void this.connection?.stop();
    this.connection = null;
  }

  /** The current access token, renewed first if it is about to expire (the hub checks it when connecting). */
  private async freshAccessToken(): Promise<string> {
    const token = this.auth.getAccessToken();
    if (token && !expiresSoon(token)) {
      return token;
    }
    return firstValueFrom(this.auth.refreshAccessToken());
  }
}

/** True if a JWT expires within the margin, or cannot be read. */
export function expiresSoon(token: string, now = Date.now()): boolean {
  try {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const { exp } = JSON.parse(atob(payload)) as { exp?: number };
    return typeof exp !== 'number' || exp * 1000 - now < TOKEN_MARGIN_MS;
  } catch {
    return true;
  }
}
