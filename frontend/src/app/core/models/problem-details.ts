import { HttpErrorResponse } from '@angular/common/http';

/**
 * Error body returned by the Citra API (RFC 7807 ProblemDetails), e.g.
 * { "title": "Resource not found.", "status": 404, "detail": "Bot 'x' was not found.", "traceId": "..." }
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  /** Only on 400 validation errors: messages per field (camelCase), e.g. { "email": ["..."] }. */
  errors?: Record<string, string[]>;
}

/** True if a value looks like a ProblemDetails body. */
export function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('title' in value || 'detail' in value || 'status' in value)
  );
}

/** A form-friendly view of a failed request: one message for the form, plus messages per field. */
export interface FormError {
  message: string;
  fieldErrors: Record<string, string[]>;
}

/** Turns any HTTP error into a message suitable for showing in a form. */
export function toFormError(
  error: unknown,
  fallback = 'Something went wrong. Please try again.',
): FormError {
  if (!(error instanceof HttpErrorResponse)) {
    return { message: fallback, fieldErrors: {} };
  }

  if (error.status === 0) {
    return {
      message: 'Cannot reach the server. Check your internet connection and try again.',
      fieldErrors: {},
    };
  }

  const problem = isProblemDetails(error.error) ? error.error : null;
  const fieldErrors = problem?.errors ?? {};

  if (error.status >= 500) {
    return { message: fallback, fieldErrors: {} };
  }

  const message =
    problem?.detail ??
    (Object.keys(fieldErrors).length > 0 ? 'Please fix the highlighted fields.' : undefined) ??
    problem?.title ??
    fallback;

  return { message, fieldErrors };
}
