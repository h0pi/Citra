import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { safeReturnUrl } from '@core/auth/return-url';
import { toFormError } from '@core/models/problem-details';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';

import { AuthLayout } from '../auth-layout/auth-layout';

@Component({
  selector: 'app-login-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AuthLayout,
    ButtonModule,
    InputTextModule,
    PasswordModule,
    MessageModule,
  ],
  templateUrl: './login-page.html',
  styleUrl: '../auth-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Where to continue after login (from ?returnUrl=..., set by the auth guard). */
  readonly returnUrl = input<string>();

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    this.auth.login(this.form.getRawValue()).subscribe({
      next: () => void this.router.navigateByUrl(safeReturnUrl(this.returnUrl())),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(toFormError(error).message);
      },
    });
  }

  /** Logs in as the read-only demo user; the server answers 404 when it has no demo. */
  protected tryDemo(): void {
    if (this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.errorMessage.set(null);

    this.auth.demo().subscribe({
      next: () => void this.router.navigateByUrl('/dashboard'),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(
          error instanceof HttpErrorResponse && error.status === 404
            ? 'The demo is not available right now.'
            : toFormError(error).message,
        );
      },
    });
  }

  /** True when a field was touched and is invalid, for showing its message. */
  protected showError(field: 'email' | 'password'): boolean {
    const control = this.form.controls[field];
    return control.invalid && control.touched;
  }
}
