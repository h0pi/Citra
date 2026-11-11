import { HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import { SKIP_LOADING } from '@core/interceptors/loading.interceptor';
import { AuthResponse, CurrentUser, LoginRequest, RegisterRequest } from '@core/models/auth.models';
import { ApiService } from '@core/services/api.service';
import {
  Observable,
  catchError,
  defer,
  finalize,
  firstValueFrom,
  map,
  of,
  shareReplay,
  throwError,
} from 'rxjs';

import { SESSION_KEY, StoredSession, TokenStorage } from './token-storage';

/** Only one tab at a time may refresh: two tabs sending the same refresh token would end the session. */
const REFRESH_LOCK = 'citra.refresh';

/** Login/register show their own errors in the form, so no global toast for them. */
const FORM_REQUEST = { context: new HttpContext().set(SKIP_ERROR_TOAST, true) };

/** Background calls (refresh, logout): no toast, no loading bar. */
const SILENT_REQUEST = {
  context: new HttpContext().set(SKIP_ERROR_TOAST, true).set(SKIP_LOADING, true),
};

/**
 * Login state for the whole app: who is logged in (a signal the UI reacts to), the tokens,
 * and the operations login / register / refresh / logout.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly storage = inject(TokenStorage);
  private readonly router = inject(Router);

  private readonly _currentUser = signal<CurrentUser | null>(null);
  private accessToken: string | null = null;
  private refreshToken: string | null = null;

  /** The one refresh request in flight, shared by everyone who needs a new token at the same moment. */
  private refreshInFlight$: Observable<string> | null = null;

  readonly currentUser = this._currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this._currentUser() !== null);

  /**
   * Owner and Admin may create, change and delete bots and documents; Viewer and Demo only look.
   * Used to hide edit buttons. The API enforces the same rule (403), so this is convenience, not security.
   */
  /** Logged in with "Try the demo": a read-only look at the demo company. */
  readonly isDemo = computed(() => this._currentUser()?.role === 'Demo');

  readonly canEdit = computed(() => {
    const role = this._currentUser()?.role;
    return role === 'Owner' || role === 'Admin';
  });

  constructor() {
    // Another tab logged in, refreshed or logged out: follow it, so no tab keeps an outdated refresh token
    // (sending one would look like a stolen token and end the session in every tab).
    window.addEventListener('storage', (event) => {
      if (event.key !== SESSION_KEY && event.key !== null) {
        return;
      }

      const saved = this.storage.read();
      if (saved) {
        this.adopt(saved);
      } else if (this._currentUser()) {
        this.clearSession();
        void this.router.navigate(['/login']);
      }
    });
  }

  /** Current access token for the auth interceptor (null when logged out). */
  getAccessToken(): string | null {
    return this.accessToken;
  }

  /**
   * Runs once at app start: restores the saved session (instant UI), then confirms it with the server.
   * If the session is no longer valid (expired, revoked, account deleted), the user ends up logged out.
   */
  async initialize(): Promise<void> {
    const saved = this.storage.read();
    if (!saved) {
      return;
    }

    this.accessToken = saved.accessToken;
    this.refreshToken = saved.refreshToken;
    this._currentUser.set(saved.user);

    try {
      // The auth interceptor transparently refreshes an expired access token for this call.
      const user = await firstValueFrom(
        this.api.get<CurrentUser>('/api/auth/me', undefined, SILENT_REQUEST),
      );
      this._currentUser.set(user);
      this.storage.updateUser(user);
    } catch {
      this.clearSession();
    }
  }

  login(request: LoginRequest): Observable<CurrentUser> {
    return this.api
      .post<AuthResponse>('/api/auth/login', request, FORM_REQUEST)
      .pipe(map((response) => this.startSession(response)));
  }

  /** "Try demo": logs in as the demo company's read-only demo user, without a password. */
  demo(): Observable<CurrentUser> {
    return this.api
      .post<AuthResponse>('/api/auth/demo', undefined, FORM_REQUEST)
      .pipe(map((response) => this.startSession(response)));
  }

  register(request: RegisterRequest): Observable<CurrentUser> {
    return this.api
      .post<AuthResponse>('/api/auth/register', request, FORM_REQUEST)
      .pipe(map((response) => this.startSession(response)));
  }

  /**
   * Gets a new access token using the refresh token. Concurrent callers share ONE request: the backend
   * treats a refresh token used twice as stolen and ends the session, so parallel refreshes must never happen.
   * On failure the user is logged out.
   */
  refreshAccessToken(): Observable<string> {
    if (!this.refreshToken) {
      return throwError(() => new Error('Not logged in.'));
    }

    this.refreshInFlight$ ??= defer(() => withRefreshLock(() => this.refreshOnce())).pipe(
      catchError((error: unknown) => {
        this.endSession();
        return throwError(() => error);
      }),
      finalize(() => (this.refreshInFlight$ = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.refreshInFlight$;
  }

  /**
   * One refresh, while holding the cross-tab lock. Another tab may have refreshed just before: then its new tokens
   * (already saved) are used instead of sending the now outdated refresh token.
   */
  private async refreshOnce(): Promise<string> {
    const saved = this.storage.read();
    if (saved && saved.refreshToken !== this.refreshToken) {
      this.adopt(saved);
      return saved.accessToken;
    }

    const response = await firstValueFrom(
      this.api.post<AuthResponse>(
        '/api/auth/refresh',
        { refreshToken: this.refreshToken },
        SILENT_REQUEST,
      ),
    );
    this.startSession(response);
    return response.accessToken;
  }

  /** Ends the session on the server (best effort) and locally, then goes to the login page. */
  logout(): void {
    const token = this.refreshToken;
    this.endSession();

    if (token) {
      this.api
        .post<void>('/api/auth/logout', { refreshToken: token }, SILENT_REQUEST)
        .pipe(catchError(() => of(undefined)))
        .subscribe();
    }
  }

  /** Local logout + redirect to login (used on logout and when a session can't be refreshed). */
  private endSession(): void {
    this.clearSession();
    void this.router.navigate(['/login']);
  }

  private startSession(response: AuthResponse): CurrentUser {
    this.accessToken = response.accessToken;
    this.refreshToken = response.refreshToken;
    this.storage.save(response);
    this._currentUser.set(response.user);
    return response.user;
  }

  /** Uses a session another tab saved. */
  private adopt(saved: StoredSession): void {
    this.accessToken = saved.accessToken;
    this.refreshToken = saved.refreshToken;
    this._currentUser.set(saved.user);
  }

  private clearSession(): void {
    this.accessToken = null;
    this.refreshToken = null;
    this.storage.clear();
    this._currentUser.set(null);
  }
}

/** Runs `work` while no other tab of this app refreshes (Web Locks); directly where the browser has no locks. */
async function withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
  const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;
  return locks ? await locks.request(REFRESH_LOCK, work) : work();
}
