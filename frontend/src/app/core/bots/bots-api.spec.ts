import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import { environment } from '@env/environment';

import { BotsApi } from './bots-api';

describe('BotsApi', () => {
  let api: BotsApi;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api/bots`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(BotsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists, gets and loads models with GET', () => {
    api.list().subscribe();
    api.get('b1').subscribe();
    api.models().subscribe();

    expect(http.expectOne(base).request.method).toBe('GET');
    expect(http.expectOne(`${base}/b1`).request.method).toBe('GET');
    expect(http.expectOne(`${base}/models`).request.method).toBe('GET');
  });

  it('creates with POST, updates with PUT and deletes with DELETE', () => {
    api.create({ name: 'Support', description: null }).subscribe();
    api
      .update('b1', {
        name: 'Sales',
        description: null,
        systemPrompt: null,
        welcomeMessage: 'Hi',
        model: null,
        temperature: 0.2,
        widgetColor: '#6366F1',
      })
      .subscribe();
    api.delete('b1').subscribe();

    const create = http.expectOne((r) => r.method === 'POST' && r.url === base);
    expect(create.request.body).toEqual({ name: 'Support', description: null });
    const update = http.expectOne((r) => r.method === 'PUT' && r.url === `${base}/b1`);
    expect(update.request.body.name).toBe('Sales');
    http.expectOne((r) => r.method === 'DELETE' && r.url === `${base}/b1`);
  });

  it('escapes ids in the URL', () => {
    api.get('a/b').subscribe();
    http.expectOne(`${base}/a%2Fb`);
  });

  it('searches a bot with GET, a trimmed question and the number of results, showing errors itself', () => {
    api.search('b1', '  How many vacation days?  ', 5).subscribe();

    const request = http.expectOne((r) => r.url === `${base}/b1/search`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('q')).toBe('How many vacation days?');
    expect(request.request.params.get('topK')).toBe('5');
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
  });
});
