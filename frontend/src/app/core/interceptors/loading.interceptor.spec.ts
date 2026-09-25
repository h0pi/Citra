import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LoadingService } from '@core/services/loading.service';

import { SKIP_LOADING, loadingInterceptor } from './loading.interceptor';

describe('loadingInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let loading: LoadingService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([loadingInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    loading = TestBed.inject(LoadingService);
  });

  afterEach(() => controller.verify());

  it('is loading while a request is in flight, and stops after success', () => {
    http.get('/a').subscribe();
    expect(loading.isLoading()).toBe(true);

    controller.expectOne('/a').flush({});
    expect(loading.isLoading()).toBe(false);
  });

  it('stops after an error', () => {
    http.get('/a').subscribe({ error: () => undefined });
    controller.expectOne('/a').flush(null, { status: 500, statusText: 'Error' });
    expect(loading.isLoading()).toBe(false);
  });

  it('keeps loading until every parallel request is done', () => {
    http.get('/a').subscribe();
    http.get('/b').subscribe();

    controller.expectOne('/a').flush({});
    expect(loading.isLoading()).toBe(true);

    controller.expectOne('/b').flush({});
    expect(loading.isLoading()).toBe(false);
  });

  it('stops when the request is cancelled', () => {
    const subscription = http.get('/a').subscribe();
    expect(loading.isLoading()).toBe(true);

    subscription.unsubscribe();
    expect(loading.isLoading()).toBe(false);
    controller.expectOne('/a'); // request was made, then cancelled
  });

  it('ignores requests marked SKIP_LOADING', () => {
    http.get('/a', { context: new HttpContext().set(SKIP_LOADING, true) }).subscribe();
    expect(loading.isLoading()).toBe(false);
    controller.expectOne('/a').flush({});
  });
});
