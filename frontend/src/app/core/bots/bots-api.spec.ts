import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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
});
