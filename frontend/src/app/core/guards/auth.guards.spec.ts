import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService } from '@core/auth/auth.service';

import { authChildGuard, authGuard, guestGuard } from './auth.guards';

@Component({ template: 'page', changeDetection: ChangeDetectionStrategy.OnPush })
class DummyPage {}

describe('auth guards', () => {
  const loggedIn = signal(false);

  beforeEach(() => {
    loggedIn.set(false);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { isAuthenticated: loggedIn.asReadonly() } },
        provideRouter([
          { path: 'login', component: DummyPage, canActivate: [guestGuard] },
          { path: 'register', component: DummyPage, canActivate: [guestGuard] },
          {
            path: '',
            canActivate: [authGuard],
            canActivateChild: [authChildGuard],
            children: [
              { path: 'dashboard', component: DummyPage },
              { path: 'documents', component: DummyPage },
            ],
          },
        ]),
      ],
    });
  });

  // Angular allows one harness per test, so it is created on first use and reused.
  let harness: RouterTestingHarness | undefined;
  beforeEach(() => (harness = undefined));

  async function navigate(url: string): Promise<string> {
    harness ??= await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    return TestBed.inject(Router).url;
  }

  it('sends logged-out users to login and remembers where they were going', async () => {
    expect(await navigate('/documents')).toBe('/login?returnUrl=%2Fdocuments');
  });

  it('keeps query strings in the remembered address', async () => {
    expect(await navigate('/documents?status=Ready')).toBe(
      '/login?returnUrl=%2Fdocuments%3Fstatus%3DReady',
    );
  });

  it('lets logged-in users open app pages', async () => {
    loggedIn.set(true);
    expect(await navigate('/documents')).toBe('/documents');
  });

  it('sends logged-in users away from login and register', async () => {
    loggedIn.set(true);
    expect(await navigate('/login')).toBe('/dashboard');
    expect(await navigate('/register')).toBe('/dashboard');
  });

  it('lets logged-out users open login and register', async () => {
    expect(await navigate('/login')).toBe('/login');
    expect(await navigate('/register')).toBe('/register');
  });

  it('checks again when moving between app pages (e.g. after the session ended)', async () => {
    loggedIn.set(true);
    expect(await navigate('/dashboard')).toBe('/dashboard');

    loggedIn.set(false);
    expect(await navigate('/documents')).toBe('/login?returnUrl=%2Fdocuments');
  });
});
