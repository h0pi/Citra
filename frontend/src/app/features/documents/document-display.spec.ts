import { DocumentSourceType, DocumentStatus } from '@core/models/document.models';

import {
  STATUS_FILTER_OPTIONS,
  chunkCount,
  fileSize,
  matchesFilters,
  pageCount,
  statusBadge,
  typeIcon,
} from './document-display';

describe('document display', () => {
  it('names every status and marks the working ones busy', () => {
    const statuses: DocumentStatus[] = [
      'Uploaded',
      'Extracting',
      'Chunking',
      'Embedding',
      'Ready',
      'Failed',
    ];

    expect(statuses.map((s) => statusBadge(s).label)).toEqual([
      'Queued',
      'Reading',
      'Splitting',
      'Indexing',
      'Ready',
      'Failed',
    ]);
    expect(statuses.filter((s) => statusBadge(s).busy)).toEqual([
      'Extracting',
      'Chunking',
      'Embedding',
    ]);
    expect(statusBadge('Ready').severity).toBe('success');
    expect(statusBadge('Failed').severity).toBe('danger');
  });

  it('has an icon and a spoken name for every type', () => {
    const types: DocumentSourceType[] = ['Pdf', 'Docx', 'Text', 'Markdown', 'Url'];

    for (const type of types) {
      expect(typeIcon(type).icon).toMatch(/^pi-/);
      expect(typeIcon(type).label.length).toBeGreaterThan(0);
    }
    expect(typeIcon('Url').icon).toBe('pi-globe');
  });

  it('formats file sizes, and shows none for web pages', () => {
    expect(fileSize({ sizeBytes: 812, sourceType: 'Text' })).toBe('812 B');
    expect(fileSize({ sizeBytes: 1024, sourceType: 'Pdf' })).toBe('1.0 KB');
    expect(fileSize({ sizeBytes: 12_700, sourceType: 'Pdf' })).toBe('12.4 KB');
    expect(fileSize({ sizeBytes: 3_250_000, sourceType: 'Docx' })).toBe('3.1 MB');
    expect(fileSize({ sizeBytes: 0, sourceType: 'Url' })).toBe('—');
  });

  it('shows pages and chunks only once a document is ready', () => {
    expect(pageCount({ pageCount: 12, status: 'Ready' })).toBe('12');
    expect(pageCount({ pageCount: 0, status: 'Ready' })).toBe('—');
    expect(pageCount({ pageCount: 12, status: 'Embedding' })).toBe('—');
    expect(chunkCount({ chunkCount: 34, status: 'Ready' })).toBe('34');
    expect(chunkCount({ chunkCount: 0, status: 'Failed' })).toBe('—');
  });

  it('matches the API’s status groups and name search', () => {
    const doc = (status: DocumentStatus, fileName = 'Employee Handbook.pdf') => ({
      status,
      fileName,
    });

    expect(matchesFilters(doc('Uploaded'), 'Queued', '')).toBe(true);
    for (const status of ['Extracting', 'Chunking', 'Embedding'] as DocumentStatus[]) {
      expect(matchesFilters(doc(status), 'Processing', '')).toBe(true);
      expect(matchesFilters(doc(status), 'Ready', '')).toBe(false);
    }
    expect(matchesFilters(doc('Ready'), 'Ready', '')).toBe(true);
    expect(matchesFilters(doc('Failed'), 'Failed', '')).toBe(true);
    expect(matchesFilters(doc('Failed'), 'Queued', '')).toBe(false);
    expect(matchesFilters(doc('Ready'), null, '')).toBe(true);

    expect(matchesFilters(doc('Ready'), null, '  handBOOK ')).toBe(true);
    expect(matchesFilters(doc('Ready'), null, 'policy')).toBe(false);
    expect(matchesFilters(doc('Ready'), 'Failed', 'handbook')).toBe(false);
  });

  it('offers every status group as a filter', () => {
    expect(STATUS_FILTER_OPTIONS.map((o) => o.value)).toEqual([
      'Queued',
      'Processing',
      'Ready',
      'Failed',
    ]);
  });
});
