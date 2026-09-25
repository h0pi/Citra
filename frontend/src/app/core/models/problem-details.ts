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
  /** Only on 400 validation errors: messages per field, e.g. { "Name": ["'Name' must not be empty."] }. */
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
