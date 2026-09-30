/** Where a document came from (same names as the API). */
export type DocumentSourceType = 'Pdf' | 'Docx' | 'Text' | 'Markdown' | 'Url';

/** Processing pipeline: Uploaded → Extracting → Chunking → Embedding → Ready (or Failed at any step). */
export type DocumentStatus =
  'Uploaded' | 'Extracting' | 'Chunking' | 'Embedding' | 'Ready' | 'Failed';

/** Status groups the list can be filtered by (GET …/documents?status=). */
export type DocumentStatusFilter = 'Queued' | 'Processing' | 'Ready' | 'Failed';

/** A document and its processing state (API DocumentDto). */
export interface CitraDocument {
  id: string;
  botId: string;
  /** File name, or the page title for web pages. */
  fileName: string;
  contentType: string;
  /** 0 for web pages until fetched. */
  sizeBytes: number;
  sourceType: DocumentSourceType;
  sourceUrl: string | null;
  status: DocumentStatus;
  /** Why processing failed, written for the user. */
  errorMessage: string | null;
  /** 0 for sources without pages (text files, web pages) and before processing. */
  pageCount: number;
  chunkCount: number;
  createdAt: string;
  updatedAt: string;
  processedAt: string | null;
}

/** One page of a longer list (API PagedResult). */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface DocumentListQuery {
  page?: number;
  pageSize?: number;
  status?: DocumentStatusFilter | null;
  search?: string | null;
}

/** One searchable piece of a document (API ChunkDto), as the detail drawer previews it. */
export interface DocumentChunk {
  id: string;
  /** Position in the document: 0, 1, 2, … */
  index: number;
  /** 1-based page for PDF/Word; null for text files and web pages. */
  pageNumber: number | null;
  /** Section path, e.g. "Vacation › Carry-over". */
  heading: string | null;
  content: string;
  tokenCount: number;
  charStart: number;
  charEnd: number;
}
