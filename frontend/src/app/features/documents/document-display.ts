import {
  CitraDocument,
  DocumentSourceType,
  DocumentStatus,
  DocumentStatusFilter,
} from '@core/models/document.models';

/** How a status looks in the table: a short word, a badge colour, and whether work is going on. */
export interface StatusBadge {
  label: string;
  severity: 'secondary' | 'info' | 'success' | 'danger';
  busy: boolean;
}

const STATUS_BADGES: Record<DocumentStatus, StatusBadge> = {
  Uploaded: { label: 'Queued', severity: 'secondary', busy: false },
  Extracting: { label: 'Reading', severity: 'info', busy: true },
  Chunking: { label: 'Splitting', severity: 'info', busy: true },
  Embedding: { label: 'Indexing', severity: 'info', busy: true },
  Ready: { label: 'Ready', severity: 'success', busy: false },
  Failed: { label: 'Failed', severity: 'danger', busy: false },
};

export function statusBadge(status: DocumentStatus): StatusBadge {
  return STATUS_BADGES[status];
}

const TYPE_ICONS: Record<DocumentSourceType, { icon: string; label: string }> = {
  Pdf: { icon: 'pi-file-pdf', label: 'PDF' },
  Docx: { icon: 'pi-file-word', label: 'Word document' },
  Text: { icon: 'pi-file', label: 'Text file' },
  Markdown: { icon: 'pi-file-edit', label: 'Markdown file' },
  Url: { icon: 'pi-globe', label: 'Web page' },
};

export function typeIcon(sourceType: DocumentSourceType): { icon: string; label: string } {
  return TYPE_ICONS[sourceType];
}

/** "812 B", "12.4 KB", "3.1 MB"; web pages have no file size: "—". */
export function fileSize(document: Pick<CitraDocument, 'sizeBytes' | 'sourceType'>): string {
  const bytes = document.sizeBytes;
  if (document.sourceType === 'Url' || bytes <= 0) {
    return '—';
  }
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const kb = bytes / 1024;
  if (kb < 1024) {
    return `${kb.toFixed(1)} KB`;
  }
  return `${(kb / 1024).toFixed(1)} MB`;
}

/**
 * Pages only exist for PDF and Word files; nothing to show for the others or before processing ("—").
 */
export function pageCount(document: Pick<CitraDocument, 'pageCount' | 'status'>): string {
  return document.status === 'Ready' && document.pageCount > 0 ? String(document.pageCount) : '—';
}

/** Chunks exist once a document is Ready. */
export function chunkCount(document: Pick<CitraDocument, 'chunkCount' | 'status'>): string {
  return document.status === 'Ready' ? String(document.chunkCount) : '—';
}

/** Which statuses each filter choice shows (the API's DocumentStatusFilter groups). */
const FILTER_STATUSES: Record<DocumentStatusFilter, DocumentStatus[]> = {
  Queued: ['Uploaded'],
  Processing: ['Extracting', 'Chunking', 'Embedding'],
  Ready: ['Ready'],
  Failed: ['Failed'],
};

/** The status filter choices, as the list's dropdown shows them. */
export const STATUS_FILTER_OPTIONS: { label: string; value: DocumentStatusFilter }[] = [
  { label: 'Queued', value: 'Queued' },
  { label: 'Processing', value: 'Processing' },
  { label: 'Ready', value: 'Ready' },
  { label: 'Failed', value: 'Failed' },
];

/**
 * Whether a document belongs in the list under the current filters — the same rule as the API (status group,
 * and a case-insensitive part of the name), so a live update never adds a row the API would not list.
 */
export function matchesFilters(
  document: Pick<CitraDocument, 'status' | 'fileName'>,
  status: DocumentStatusFilter | null,
  search: string,
): boolean {
  if (status && !FILTER_STATUSES[status].includes(document.status)) {
    return false;
  }
  const term = search.trim().toLowerCase();
  return !term || document.fileName.toLowerCase().includes(term);
}
