import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { DEFAULT_AFTER_LOGIN } from '@core/auth/return-url';
import { toFormError } from '@core/models/problem-details';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';

import { AuthLayout } from '../auth-layout/auth-layout';

/** Same limits as the backend (RegisterCommandValidator, Identity password rules). */
export const PASSWORD_MIN_LENGTH = 8;
const NAME_MAX_LENGTH = 100;
const EMAIL_MAX_LENGTH = 256;
const PASSWORD_MAX_LENGTH = 128;

/** Identity requires at least one digit; checked here too so the user sees it instantly. */
function containsDigit(control: AbstractControl<string>): ValidationErrors | null {
  return /\d/.test(control.value) ? null : { digit: true };
}

type Field = 'companyName' | 'displayName' | 'email' | 'password';

@Component({
  selector: 'app-register-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AuthLayout,
    ButtonModule,
    InputTextModule,
    PasswordModule,
    MessageModule,
  ],
  templateUrl: './register-page.html',
  styleUrl: '../auth-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly passwordMinLength = PASSWORD_MIN_LENGTH;

  protected readonly form = inject(NonNullableFormBuilder).group({
    companyName: ['', [Validators.required, Validators.maxLength(NAME_MAX_LENGTH)]],
    displayName: ['', [Validators.required, Validators.maxLength(NAME_MAX_LENGTH)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(EMAIL_MAX_LENGTH)]],
    password: [
      '',
      [
        Validators.required,
        Validators.minLength(PASSWORD_MIN_LENGTH),
        Validators.maxLength(PASSWORD_MAX_LENGTH),
        containsDigit,
      ],
    ],
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

    this.auth.register(this.form.getRawValue()).subscribe({
      next: () => void this.router.navigateByUrl(DEFAULT_AFTER_LOGIN),
      error: (error: unknown) => {
        this.submitting.set(false);
        const { message, fieldErrors } = toFormError(error);
        this.errorMessage.set(message);
        this.applyServerErrors(fieldErrors);
      },
    });
  }

  protected showError(field: Field): boolean {
    const control = this.form.controls[field];
    return control.invalid && control.touched;
  }

  /** The first message to show under a field (client rules first, then the server's). */
  protected errorFor(field: Field): string {
    const errors = this.form.controls[field].errors ?? {};
    if (errors['server']) return errors['server'] as string;
    if (errors['required']) return 'This field is required.';
    if (errors['email']) return 'Enter a valid email address.';
    if (errors['maxlength']) return 'This is too long.';
    if (errors['minlength']) return `Use at least ${PASSWORD_MIN_LENGTH} characters.`;
    if (errors['digit']) return 'Include at least one number.';
    return 'Please check this field.';
  }

  /**
   * Puts the server's per-field messages (e.g. "email already exists") under the matching inputs.
   * Angular clears them automatically as soon as the user edits the field.
   */
  private applyServerErrors(fieldErrors: Record<string, string[]>): void {
    for (const [field, messages] of Object.entries(fieldErrors)) {
      const control = this.form.get(field);
      if (control && messages.length > 0) {
        control.setErrors({ ...control.errors, server: messages[0] });
        control.markAsTouched();
      }
    }
  }
}
