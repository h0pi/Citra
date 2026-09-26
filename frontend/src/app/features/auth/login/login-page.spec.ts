import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { CurrentUser } from '@core/models/auth.models';
import { Observable, of, throwError } from 'rxjs';

import { LoginPage } from './login-page';

describe('LoginPage', () => {
  let login: ReturnType<typeof vi.fn<(r: unknown) => Observable<CurrentUser>>>;
  let router: Router;

  beforeEach(async () => {
    login = vi.fn();
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [provideRouter([]), { provide: AuthService, useValue: { login } }],
    }).compileComponents();
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  async function render(returnUrl?: string) {
    const fixture = TestBed.createComponent(LoginPage);
    if (returnUrl) {
      fixture.componentRef.setInput('returnUrl', returnUrl);
    }
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function fill(el: HTMLElement, email: string, password: string): void {
    const emailInput = el.querySelector<HTMLInputElement>('#email')!;
    const passwordInput = el.querySelector<HTMLInputElement>('#password')!;
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
  }

  async function submit(fixture: Awaited<ReturnType<typeof render>>['fixture']) {
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('shows validation messages and does not call the API for an empty form', async () => {
    const { fixture, el } = await render();
    await submit(fixture);

    expect(login).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Email is required.');
    expect(el.textContent).toContain('Password is required.');
  });

  it('rejects an invalid email format', async () => {
    const { fixture, el } = await render();
    fill(el, 'not-an-email', 'secret123');
    await submit(fixture);

    expect(login).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Enter a valid email address.');
  });

  it('logs in and goes to the dashboard', async () => {
    login.mockReturnValue(of({} as CurrentUser));
    const { fixture, el } = await render();
    fill(el, 'ammar@acme.com', 'citra2026pass');
    await submit(fixture);

    expect(login).toHaveBeenCalledWith({ email: 'ammar@acme.com', password: 'citra2026pass' });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('returns to the page the user wanted to open', async () => {
    login.mockReturnValue(of({} as CurrentUser));
    const { fixture, el } = await render('/documents');
    fill(el, 'ammar@acme.com', 'citra2026pass');
    await submit(fixture);

    expect(router.navigateByUrl).toHaveBeenCalledWith('/documents');
  });

  it('ignores an external return URL (no open redirect)', async () => {
    login.mockReturnValue(of({} as CurrentUser));
    const { fixture, el } = await render('https://evil.example.com');
    fill(el, 'ammar@acme.com', 'citra2026pass');
    await submit(fixture);

    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('shows the API message for wrong credentials', async () => {
    login.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 401,
            error: { status: 401, detail: 'Invalid email or password.' },
          }),
      ),
    );
    const { fixture, el } = await render();
    fill(el, 'ammar@acme.com', 'wrong-pass');
    await submit(fixture);

    expect(el.textContent).toContain('Invalid email or password.');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('explains when the server is unreachable', async () => {
    login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { fixture, el } = await render();
    fill(el, 'ammar@acme.com', 'citra2026pass');
    await submit(fixture);

    expect(el.textContent).toContain('Cannot reach the server');
  });
});
