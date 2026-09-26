import {
  HttpContextToken,
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpRequest,
  HttpStatusCode,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '@core/auth/auth.service';
import { environment } from '@env/environment';
import { catchError, switchMap, throwError } from 'rxjs';

/** Marks a request that was already retried after a token refresh (never retry twice). */
const RETRIED_AFTER_REFRESH = new HttpContextToken<boolean>(() => false);

/** Endpoints where a 401 means "wrong credentials / invalid token", not "access token expired". */
const AUTH_ENDPOINTS = [
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/refresh',
  '/api/auth/logout',
];

const apiBase = environment.apiUrl.replace(/\/+$/, '');

/**
 * Adds the access token to requests for the Citra API (never to other sites), and when the API says
 * 401 because the token expired, refreshes it (one shared refresh for all waiting requests) and retries
 * the request once. If the refresh fails, AuthService logs the user out and opens the login page.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isCitraApi(req.url)) {
    return next(req);
  }

  const auth = inject(AuthService);
  const token = auth.getAccessToken();

  return next(token ? withToken(req, token) : req).pipe(
    catchError((error: unknown) => {
      const canRefresh =
        error instanceof HttpErrorResponse &&
        error.status === HttpStatusCode.Unauthorized &&
        token !== null &&
        !isAuthEndpoint(req.url) &&
        !req.context.get(RETRIED_AFTER_REFRESH);

      if (!canRefresh) {
        return throwError(() => error);
      }

      return auth.refreshAccessToken().pipe(
        switchMap((newToken) =>
          next(
            withToken(req, newToken).clone({
              context: req.context.set(RETRIED_AFTER_REFRESH, true),
            }),
          ),
        ),
      );
    }),
  );
};

function withToken(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

/** Only our own API gets the token: with a base URL in dev, same-origin /api paths in production. */
function isCitraApi(url: string): boolean {
  return apiBase ? url.startsWith(`${apiBase}/`) : url.startsWith('/api/');
}

function isAuthEndpoint(url: string): boolean {
  const path = apiBase && url.startsWith(apiBase) ? url.slice(apiBase.length) : url;
  return AUTH_ENDPOINTS.some((endpoint) => path.startsWith(endpoint));
}
