import {
  HttpContextToken,
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpStatusCode,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { isProblemDetails } from '@core/models/problem-details';
import { NotificationService } from '@core/services/notification.service';
import { catchError, throwError } from 'rxjs';

/**
 * Set on a request when the caller shows the error itself (e.g. a form with field errors):
 * `api.post(url, body, ...)` → `new HttpContext().set(SKIP_ERROR_TOAST, true)`.
 */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

/**
 * Shows a toast for every failed API call, using the API's ProblemDetails message when available.
 * The error is re-thrown, so callers can still react (stop a spinner, keep a dialog open, ...).
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notify = inject(NotificationService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && !req.context.get(SKIP_ERROR_TOAST)) {
        const message = toUserMessage(error);
        if (message) {
          notify.error(message.summary, message.detail);
        }
      }
      return throwError(() => error);
    }),
  );
};

interface UserMessage {
  summary: string;
  detail?: string;
}

/** Turns an HTTP error into a short, human message. Returns null when no toast should be shown. */
export function toUserMessage(error: HttpErrorResponse): UserMessage | null {
  // 401 is handled by the auth interceptor (token refresh / redirect to login) — no toast.
  if (error.status === HttpStatusCode.Unauthorized) {
    return null;
  }

  // Status 0 = the request never got a response (API down, no internet, CORS, certificate).
  if (error.status === 0) {
    return {
      summary: 'Cannot reach the server',
      detail: 'Check your internet connection and try again.',
    };
  }

  const problem = isProblemDetails(error.error) ? error.error : null;

  // Validation errors: show the first field message (forms can show all of them inline).
  if (error.status === HttpStatusCode.BadRequest && problem?.errors) {
    const first = Object.values(problem.errors).flat()[0];
    return { summary: problem.title ?? 'Please check your input', detail: first };
  }

  if (error.status >= 500) {
    return {
      summary: 'Something went wrong',
      detail: problem?.traceId
        ? `Please try again. If it keeps happening, mention reference ${problem.traceId}.`
        : 'Please try again in a moment.',
    };
  }

  return {
    summary: problem?.title ?? defaultSummary(error.status),
    detail: problem?.detail,
  };
}

function defaultSummary(status: number): string {
  switch (status) {
    case HttpStatusCode.Forbidden:
      return 'You do not have permission to do this';
    case HttpStatusCode.NotFound:
      return 'Not found';
    case HttpStatusCode.Conflict:
      return 'This conflicts with existing data';
    case HttpStatusCode.PayloadTooLarge:
      return 'The file is too large';
    case HttpStatusCode.TooManyRequests:
      return 'Too many requests — please slow down';
    default:
      return 'Request failed';
  }
}
