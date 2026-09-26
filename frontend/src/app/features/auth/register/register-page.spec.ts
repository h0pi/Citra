import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { CurrentUser } from '@core/models/auth.models';
import { Observable, of, throwError } from 'rxjs';

import { RegisterPage } from './register-page';

describe('RegisterPage', () => {
  let register: ReturnType<typeof vi.fn<(r: unknown) => Observable<CurrentUser>>>;
  let router: Router;

  beforeEach(async () => {
    register = vi.fn();
    await TestBed.configureTestingModule({
      imports: [RegisterPage],
      providers: [provideRouter([]), { provide: AuthService, useValue: { register } }],
    }).compileComponents();
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  async function render() {
    const fixture = TestBed.createComponent(RegisterPage);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function type(el: HTMLElement, id: string, value: string): void {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function fillValid(el: HTMLElement, password = 'citra2026pass'): void {
    type(el, 'companyName', 'Acme Logistics');
    type(el, 'displayName', 'Ammar');
    type(el, 'email', 'ammar@acme.com');
    type(el, 'password', password);
  }

  async function submit(fixture: Awaited<ReturnType<typeof render>>['fixture']) {
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('requires every field and does not call the API', async () => {
    const { fixture, el } = await render();
    await submit(fixture);

    expect(register).not.toHaveBeenCalled();
    expect(el.querySelectorAll('.field-error').length).toBe(4);
  });

  it('checks the password rules in the browser', async () => {
    const { fixture, el } = await render();
    fillValid(el, 'short1');
    await submit(fixture);
    expect(el.textContent).toContain('Use at least 8 characters.');

    type(el, 'password', 'onlyletters');
    await fixture.whenStable();
    expect(el.textContent).toContain('Include at least one number.');
    expect(register).not.toHaveBeenCalled();
  });

  it('creates the account and opens the dashboard', async () => {
    register.mockReturnValue(of({} as CurrentUser));
    const { fixture, el } = await render();
    fillValid(el);
    await submit(fixture);

    expect(register).toHaveBeenCalledWith({
      companyName: 'Acme Logistics',
      displayName: 'Ammar',
      email: 'ammar@acme.com',
      password: 'citra2026pass',
    });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('shows a server field error under the matching input, and clears it on edit', async () => {
    register.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              status: 400,
              title: 'One or more validation errors occurred.',
              errors: { email: ['An account with this email already exists.'] },
            },
          }),
      ),
    );
    const { fixture, el } = await render();
    fillValid(el);
    await submit(fixture);

    const emailField = el.querySelector('#email')!.closest('.field')!;
    expect(emailField.textContent).toContain('An account with this email already exists.');
    expect(el.textContent).toContain('Please fix the highlighted fields.');

    type(el, 'email', 'other@acme.com');
    await fixture.whenStable();
    expect(emailField.textContent).not.toContain('already exists');
  });
});
