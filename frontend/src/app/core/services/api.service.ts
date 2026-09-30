import { HttpClient, HttpContext, HttpEvent, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';

/** Query string values; null/undefined/empty-string entries are skipped. */
export type QueryParams = Record<string, string | number | boolean | null | undefined>;

/**
 * Per-request options. `context` carries flags for the interceptors,
 * e.g. `new HttpContext().set(SKIP_ERROR_TOAST, true)` when the caller shows the error itself.
 */
export interface RequestOptions {
  context?: HttpContext;
}

/**
 * The single place that talks HTTP to the Citra API.
 * Feature services call e.g. `api.get<Bot[]>('/api/bots')` and never build URLs themselves,
 * so the base URL, auth token (interceptor) and error handling (interceptor) are consistent everywhere.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl.replace(/\/+$/, '');

  get<T>(path: string, params?: QueryParams, options?: RequestOptions): Observable<T> {
    return this.http.get<T>(this.url(path), {
      params: toHttpParams(params),
      context: options?.context,
    });
  }

  post<T>(path: string, body?: unknown, options?: RequestOptions): Observable<T> {
    return this.http.post<T>(this.url(path), body ?? null, { context: options?.context });
  }

  put<T>(path: string, body?: unknown, options?: RequestOptions): Observable<T> {
    return this.http.put<T>(this.url(path), body ?? null, { context: options?.context });
  }

  patch<T>(path: string, body?: unknown, options?: RequestOptions): Observable<T> {
    return this.http.patch<T>(this.url(path), body ?? null, { context: options?.context });
  }

  delete<T = void>(path: string, options?: RequestOptions): Observable<T> {
    return this.http.delete<T>(this.url(path), { context: options?.context });
  }

  /**
   * Multipart upload with progress events (HttpEventType.UploadProgress / Response),
   * used for document uploads so the UI can show a progress bar.
   */
  upload<T>(path: string, formData: FormData, options?: RequestOptions): Observable<HttpEvent<T>> {
    return this.http.post<T>(this.url(path), formData, {
      reportProgress: true,
      observe: 'events',
      context: options?.context,
    });
  }

  /** Download a file (e.g. the original PDF for the citation viewer). */
  getBlob(path: string): Observable<Blob> {
    return this.http.get(this.url(path), { responseType: 'blob' });
  }

  /** Absolute URL for an API path; also useful for SignalR and SSE connections. */
  url(path: string): string {
    return `${this.baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
  }
}

function toHttpParams(params?: QueryParams): HttpParams | undefined {
  if (!params) {
    return undefined;
  }
  let httpParams = new HttpParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      httpParams = httpParams.set(key, String(value));
    }
  }
  return httpParams;
}
