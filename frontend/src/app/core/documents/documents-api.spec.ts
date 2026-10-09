import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import { SKIP_LOADING } from '@core/interceptors/loading.interceptor';
import { environment } from '@env/environment';

import { DocumentsApi } from './documents-api';

describe('DocumentsApi', () => {
  let api: DocumentsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(DocumentsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists a bot’s documents with GET, without empty filters', () => {
    api.list('b1').subscribe();

    const request = http.expectOne(`${environment.apiUrl}/api/bots/b1/documents`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys()).toEqual([]);
  });

  it('sends paging, status and a trimmed search', () => {
    api.list('b1', { page: 2, pageSize: 50, status: 'Failed', search: '  handbook ' }).subscribe();

    const request = http.expectOne((r) => r.url === `${environment.apiUrl}/api/bots/b1/documents`);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');
    expect(request.request.params.get('status')).toBe('Failed');
    expect(request.request.params.get('search')).toBe('handbook');
  });

  it('escapes the bot id in the address', () => {
    api.list('a/b').subscribe();

    http.expectOne(`${environment.apiUrl}/api/bots/a%2Fb/documents`);
  });

  it('uploads a file as the form field "file", reporting progress, without toast or loading bar', () => {
    const file = new File(['%PDF-1.4'], 'Handbook.pdf', { type: 'application/pdf' });
    api.upload('b1', file).subscribe();

    const request = http.expectOne(`${environment.apiUrl}/api/bots/b1/documents`);
    expect(request.request.method).toBe('POST');
    expect(request.request.reportProgress).toBe(true);
    const body = request.request.body as FormData;
    expect((body.get('file') as File).name).toBe('Handbook.pdf');
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
    expect(request.request.context.get(SKIP_LOADING)).toBe(true);
  });

  it('adds a web page with POST …/documents/url, errors shown by the dialog', () => {
    api.addUrl('b1', 'https://acme.test/help').subscribe();

    const request = http.expectOne(`${environment.apiUrl}/api/bots/b1/documents/url`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ url: 'https://acme.test/help' });
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
  });

  it('retries a document with POST …/reprocess and deletes it with DELETE', () => {
    api.reprocess('d1').subscribe();
    api.delete('d2').subscribe();

    expect(http.expectOne(`${environment.apiUrl}/api/documents/d1/reprocess`).request.method).toBe(
      'POST',
    );
    expect(http.expectOne(`${environment.apiUrl}/api/documents/d2`).request.method).toBe('DELETE');
  });

  it('loads one document and its original file', () => {
    api.get('d/1').subscribe();
    api.file('d/1').subscribe();

    expect(http.expectOne(`${environment.apiUrl}/api/documents/d%2F1`).request.method).toBe('GET');
    const file = http.expectOne(`${environment.apiUrl}/api/documents/d%2F1/file`);
    expect(file.request.responseType).toBe('blob');
  });

  it('loads a document’s chunks a page at a time', () => {
    api.chunks('d1', 2, 20).subscribe();

    const request = http.expectOne(
      (r) => r.url === `${environment.apiUrl}/api/documents/d1/chunks`,
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
  });
});
