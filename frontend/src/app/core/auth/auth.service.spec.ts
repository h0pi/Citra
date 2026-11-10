import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthResponse } from '@core/models/auth.models';
import { environment } from '@env/environment';

import { AuthService } from './auth.service';
import { TokenStorage } from './token-storage';

const api = environment.apiUrl;

function authResponse(suffix: string): AuthResponse {
  return {
    accessToken: `access-${suffix}`,
    accessTokenExpiresAt: '2030-01-01T00:00:00Z',
    refreshToken: `refresh-${suffix}`,
    refreshTokenExpiresAt: '2030-01-14T00:00:00Z',
    user: {
      id: 'u1',
      email: 'ammar@acme.com',
      displayName: 'Ammar',
      role: 'Owner',
      tenantId: 't1',
      tenantName: 'Acme',
    },
  };
}

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  let storage: TokenStorage;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    storage = TestBed.inject(TokenStorage);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  function login(suffix = '1'): void {
    auth.login({ email: 'ammar@acme.com', password: 'citra2026pass' }).subscribe();
    http.expectOne(`${api}/api/auth/login`).flush(authResponse(suffix));
  }

  it('starts logged out', () => {
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.getAccessToken()).toBeNull();
  });

  it('login stores the session and sets the current user', () => {
    login();

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.currentUser()?.tenantName).toBe('Acme');
    expect(auth.getAccessToken()).toBe('access-1');
    expect(storage.read()?.refreshToken).toBe('refresh-1');
  });

  it('a failed login leaves the user logged out', () => {
    let failed = false;
    auth.login({ email: 'x@y.com', password: 'wrong' }).subscribe({ error: () => (failed = true) });
    http
      .expectOne(`${api}/api/auth/login`)
      .flush({ detail: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
    expect(storage.read()).toBeNull();
  });

  it('the demo login starts a read-only session without a password', () => {
    expect(auth.isDemo()).toBe(false);
    auth.demo().subscribe();
    const request = http.expectOne(`${api}/api/auth/demo`);
    expect(request.request.method).toBe('POST');
    const response = authResponse('1');
    request.flush({ ...response, user: { ...response.user, role: 'Demo' } });

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.isDemo()).toBe(true);
    expect(auth.canEdit()).toBe(false);
  });

  it('register starts a session like login', () => {
    auth
      .register({
        companyName: 'Acme',
        displayName: 'Ammar',
        email: 'ammar@acme.com',
        password: 'citra2026pass',
      })
      .subscribe();
    http.expectOne(`${api}/api/auth/register`).flush(authResponse('r'));

    expect(auth.currentUser()?.email).toBe('ammar@acme.com');
    expect(auth.getAccessToken()).toBe('access-r');
  });

  it('parallel refreshes share ONE request (a reused refresh token would end the session)', () => {
    login();
    const results: string[] = [];

    auth.refreshAccessToken().subscribe((t) => results.push(t));
    auth.refreshAccessToken().subscribe((t) => results.push(t));
    auth.refreshAccessToken().subscribe((t) => results.push(t));

    const requests = http.match(`${api}/api/auth/refresh`);
    expect(requests.length).toBe(1);
    expect(requests[0].request.body).toEqual({ refreshToken: 'refresh-1' });
    requests[0].flush(authResponse('2'));

    expect(results).toEqual(['access-2', 'access-2', 'access-2']);
    expect(storage.read()?.refreshToken).toBe('refresh-2');
  });

  it('after a refresh completes, the next one sends the NEW refresh token', () => {
    login();
    auth.refreshAccessToken().subscribe();
    http.expectOne(`${api}/api/auth/refresh`).flush(authResponse('2'));

    auth.refreshAccessToken().subscribe();
    const second = http.expectOne(`${api}/api/auth/refresh`);
    expect(second.request.body).toEqual({ refreshToken: 'refresh-2' });
    second.flush(authResponse('3'));
  });

  it('a failed refresh logs out and goes to the login page', () => {
    login();
    auth.refreshAccessToken().subscribe({ error: () => undefined });
    http
      .expectOne(`${api}/api/auth/refresh`)
      .flush({ detail: 'Your session has expired.' }, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isAuthenticated()).toBe(false);
    expect(storage.read()).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('logout clears the session and revokes it on the server', () => {
    login();
    auth.logout();

    expect(auth.isAuthenticated()).toBe(false);
    expect(storage.read()).toBeNull();
    const request = http.expectOne(`${api}/api/auth/logout`);
    expect(request.request.body).toEqual({ refreshToken: 'refresh-1' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('initialize restores a saved session and confirms it with /me', async () => {
    storage.save(authResponse('saved'));

    const done = auth.initialize();
    expect(auth.currentUser()?.email).toBe('ammar@acme.com'); // instant, from storage
    http
      .expectOne(`${api}/api/auth/me`)
      .flush({ ...authResponse('saved').user, displayName: 'Ammar H.' });
    await done;

    expect(auth.currentUser()?.displayName).toBe('Ammar H.'); // refreshed from the server
  });

  it('initialize logs out if the saved session is no longer valid', async () => {
    storage.save(authResponse('saved'));

    const done = auth.initialize();
    http.expectOne(`${api}/api/auth/me`).flush(null, { status: 401, statusText: 'Unauthorized' });
    await done;

    expect(auth.isAuthenticated()).toBe(false);
    expect(storage.read()).toBeNull();
  });

  it('initialize does nothing without a saved session', async () => {
    await auth.initialize();
    http.expectNone(`${api}/api/auth/me`);
    expect(auth.isAuthenticated()).toBe(false);
  });
});
