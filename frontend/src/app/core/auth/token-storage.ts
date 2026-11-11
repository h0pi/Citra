import { Injectable } from '@angular/core';
import { AuthResponse, CurrentUser } from '@core/models/auth.models';

/** The localStorage key; other tabs see changes to it as a 'storage' event. */
export const SESSION_KEY = 'citra.session';

/** What we keep in the browser between visits. */
export interface StoredSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  user: CurrentUser;
}

/**
 * The only code that reads/writes the session in localStorage.
 * All access is wrapped: storage can be unavailable (private mode, blocked site data),
 * in which case the app still works for the current visit.
 */
@Injectable({ providedIn: 'root' })
export class TokenStorage {
  read(): StoredSession | null {
    try {
      const raw = localStorage.getItem(SESSION_KEY);
      return raw ? (JSON.parse(raw) as StoredSession) : null;
    } catch {
      return null;
    }
  }

  save(response: AuthResponse): void {
    const session: StoredSession = {
      accessToken: response.accessToken,
      accessTokenExpiresAt: response.accessTokenExpiresAt,
      refreshToken: response.refreshToken,
      user: response.user,
    };
    try {
      localStorage.setItem(SESSION_KEY, JSON.stringify(session));
    } catch {
      // Ignore: the session still lives in memory for this visit.
    }
  }

  updateUser(user: CurrentUser): void {
    const session = this.read();
    if (session) {
      try {
        localStorage.setItem(SESSION_KEY, JSON.stringify({ ...session, user }));
      } catch {
        // Ignore.
      }
    }
  }

  clear(): void {
    try {
      localStorage.removeItem(SESSION_KEY);
    } catch {
      // Ignore.
    }
  }
}
