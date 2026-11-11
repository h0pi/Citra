import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { AuthResponse } from '@core/models/auth.models';
import { environment } from '@env/environment';

import { authInterceptor } from './auth.interceptor';

const api = environment.apiUrl;

function authResponse(suffix: string): AuthResponse {
  return {
    accessToken: `access-${suffix}`,
    accessTokenExpiresAt: '2030-01-01T00:00:00Z',
    refreshToken: `refresh-${suffix}`,
    refreshTokenExpiresAt: '2030-01-14T00:00:00Z',
    user: {
      id: 'u1',
      email: 'a@acme.com',
      displayName: 'A',
      role: 'Owner',
      tenantId: 't1',
      tenantName: 'Acme',
    },
  };
}

/** Lets pending async work run (a refresh first waits for the cross-tab lock). */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => controller.verify());

  function logIn(): void {
    auth.login({ email: 'a@acme.com', password: 'x' }).subscribe();
    controller.expectOne(`${api}/api/auth/login`).flush(authResponse('1'));
  }

  const unauthorized = { status: 401, statusText: 'Unauthorized' };

  it('adds the access token to API requests', () => {
    logIn();
    http.get(`${api}/api/bots`).subscribe();

    const req = controller.expectOne(`${api}/api/bots`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer access-1');
    req.flush([]);
  });

  it('never sends the token to other websites', () => {
    logIn();
    http.get('https://other-site.example.com/data').subscribe();

    const req = controller.expectOne('https://other-site.example.com/data');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('sends no token when logged out', () => {
    http.get(`${api}/api/bots`).subscribe({ error: () => undefined });

    const req = controller.expectOne(`${api}/api/bots`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush(null, unauthorized);
    controller.expectNone(`${api}/api/auth/refresh`);
  });

  it('on 401, refreshes the token and retries the request once', async () => {
    logIn();
    let result: unknown;
    http.get(`${api}/api/bots`).subscribe((r) => (result = r));

    controller.expectOne(`${api}/api/bots`).flush(null, unauthorized);
    await settle();
    controller.expectOne(`${api}/api/auth/refresh`).flush(authResponse('2'));
    await settle();

    const retry = controller.expectOne(`${api}/api/bots`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
    retry.flush(['bot']);
    expect(result).toEqual(['bot']);
  });

  it('parallel 401s share ONE refresh, then every request is retried', async () => {
    logIn();
    const results: unknown[] = [];
    http.get(`${api}/api/bots`).subscribe((r) => results.push(r));
    http.get(`${api}/api/documents`).subscribe((r) => results.push(r));

    controller.expectOne(`${api}/api/bots`).flush(null, unauthorized);
    controller.expectOne(`${api}/api/documents`).flush(null, unauthorized);
    await settle();

    const refreshes = controller.match(`${api}/api/auth/refresh`);
    expect(refreshes.length).toBe(1);
    refreshes[0].flush(authResponse('2'));
    await settle();

    controller.expectOne(`${api}/api/bots`).flush('bots');
    controller.expectOne(`${api}/api/documents`).flush('docs');
    expect(results).toEqual(['bots', 'docs']);
  });

  it('does not refresh when login itself returns 401 (wrong password)', () => {
    logIn();
    auth.login({ email: 'a@acme.com', password: 'wrong' }).subscribe({ error: () => undefined });

    controller.expectOne(`${api}/api/auth/login`).flush(null, unauthorized);
    controller.expectNone(`${api}/api/auth/refresh`);
  });

  it('retries at most once: a second 401 is returned to the caller', async () => {
    logIn();
    let failed = false;
    http.get(`${api}/api/bots`).subscribe({ error: () => (failed = true) });

    controller.expectOne(`${api}/api/bots`).flush(null, unauthorized);
    await settle();
    controller.expectOne(`${api}/api/auth/refresh`).flush(authResponse('2'));
    await settle();
    controller.expectOne(`${api}/api/bots`).flush(null, unauthorized);
    await settle();

    controller.expectNone(`${api}/api/auth/refresh`);
    expect(failed).toBe(true);
  });

  it('when the refresh fails, the request fails and the user is sent to login', async () => {
    logIn();
    let failed = false;
    http.get(`${api}/api/bots`).subscribe({ error: () => (failed = true) });

    controller.expectOne(`${api}/api/bots`).flush(null, unauthorized);
    await settle();
    controller.expectOne(`${api}/api/auth/refresh`).flush(null, unauthorized);
    await settle();

    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});
