import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '@env/environment';

import { ApiService } from './api.service';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;
  const base = environment.apiUrl;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('prefixes paths with the API base URL', () => {
    expect(api.url('/api/bots')).toBe(`${base}/api/bots`);
    expect(api.url('api/bots')).toBe(`${base}/api/bots`);
  });

  it('GET sends query params and skips empty ones', () => {
    api.get('/api/documents', { page: 2, search: 'policy', status: null, tag: '' }).subscribe();

    const req = http.expectOne((r) => r.url === `${base}/api/documents`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('search')).toBe('policy');
    expect(req.request.params.has('status')).toBe(false);
    expect(req.request.params.has('tag')).toBe(false);
    req.flush([]);
  });

  it('POST sends the body and returns the response', () => {
    let result: { id: string } | undefined;
    api.post<{ id: string }>('/api/bots', { name: 'Support' }).subscribe((r) => (result = r));

    const req = http.expectOne(`${base}/api/bots`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Support' });
    req.flush({ id: 'abc' });

    expect(result).toEqual({ id: 'abc' });
  });

  it('DELETE calls the right URL', () => {
    api.delete('/api/bots/abc').subscribe();

    const req = http.expectOne(`${base}/api/bots/abc`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
