import { HttpContext, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import { SKIP_LOADING } from '@core/interceptors/loading.interceptor';
import {
  CitraDocument,
  DocumentChunk,
  DocumentListQuery,
  PagedResult,
} from '@core/models/document.models';
import { ApiService } from '@core/services/api.service';
import { Observable } from 'rxjs';

/** Sent from dialogs, which show errors themselves instead of a toast. */
const FORM_REQUEST = { context: new HttpContext().set(SKIP_ERROR_TOAST, true) };

/** Uploads show their own progress bar and error per file: no toast, no top loading bar. */
const UPLOAD_REQUEST = {
  context: new HttpContext().set(SKIP_ERROR_TOAST, true).set(SKIP_LOADING, true),
};

/** HTTP calls for a bot's documents (/api/bots/{botId}/documents and /api/documents/{id}). */
@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly api = inject(ApiService);

  /** A bot's documents, newest first, one page at a time. */
  list(botId: string, query: DocumentListQuery = {}): Observable<PagedResult<CitraDocument>> {
    return this.api.get<PagedResult<CitraDocument>>(this.botDocuments(botId), {
      page: query.page,
      pageSize: query.pageSize,
      status: query.status,
      search: query.search?.trim(),
    });
  }

  /**
   * Uploads one file (form field "file"). Emits progress events, then the response with the new document
   * (status Uploaded; it is processed in the background).
   */
  upload(botId: string, file: File): Observable<HttpEvent<CitraDocument>> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.api.upload<CitraDocument>(this.botDocuments(botId), form, UPLOAD_REQUEST);
  }

  /** Adds a public web page by its address; it is downloaded and read in the background. */
  addUrl(botId: string, url: string): Observable<CitraDocument> {
    return this.api.post<CitraDocument>(`${this.botDocuments(botId)}/url`, { url }, FORM_REQUEST);
  }

  /** A document's chunks in reading order, a page at a time. */
  chunks(id: string, page = 1, pageSize = 20): Observable<PagedResult<DocumentChunk>> {
    return this.api.get<PagedResult<DocumentChunk>>(
      `/api/documents/${encodeURIComponent(id)}/chunks`,
      { page, pageSize },
    );
  }

  /** Sends a Ready or Failed document through processing again (e.g. "Retry" after a failure). */
  reprocess(id: string): Observable<CitraDocument> {
    return this.api.post<CitraDocument>(`/api/documents/${encodeURIComponent(id)}/reprocess`);
  }

  get(id: string): Observable<CitraDocument> {
    return this.api.get<CitraDocument>(`/api/documents/${encodeURIComponent(id)}`);
  }

  /** The original file (e.g. a PDF for the citation viewer). */
  file(id: string): Observable<Blob> {
    return this.api.getBlob(`/api/documents/${encodeURIComponent(id)}/file`);
  }

  /** Deletes a document, its chunks and its stored file. */
  delete(id: string): Observable<void> {
    return this.api.delete(`/api/documents/${encodeURIComponent(id)}`);
  }

  private botDocuments(botId: string): string {
    return `/api/bots/${encodeURIComponent(botId)}/documents`;
  }
}
