import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { DocumentsApi } from '@core/documents/documents-api';
import { CitraDocument } from '@core/models/document.models';
import { toFormError } from '@core/models/problem-details';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';

/** Same limit as the API (Document.SourceUrlMaxLength). */
export const URL_MAX_LENGTH = 2048;

/**
 * "https://" is added when the address has no scheme ("acme.com/help" → "https://acme.com/help");
 * null if it is not a plain http(s) web address (other schemes like mailto: or ftp:, or a user name or
 * password inside the address).
 */
export function normalizeUrl(value: string): string | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }
  // "mailto:…", "javascript:…", "ftp://…" name a scheme; "acme.com:8080/x" is a host with a port.
  const scheme = /^([a-z][a-z0-9+.-]*):(?!\d)/i.exec(trimmed)?.[1]?.toLowerCase();
  if (scheme && scheme !== 'http' && scheme !== 'https') {
    return null;
  }
  const withScheme = scheme ? trimmed : `https://${trimmed}`;
  try {
    const url = new URL(withScheme);
    const isWeb = url.protocol === 'http:' || url.protocol === 'https:';
    return isWeb && url.hostname.includes('.') && !url.username && !url.password
      ? url.toString()
      : null;
  } catch {
    return null;
  }
}

function webAddress(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim() && !normalizeUrl(control.value) ? { webAddress: true } : null;
}

/**
 * "Add web page" dialog: the address of a public page the bot should answer from. The API downloads and reads
 * it in the background (and refuses private or unsafe addresses).
 */
@Component({
  selector: 'app-add-url-dialog',
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, MessageModule],
  templateUrl: './add-url-dialog.html',
  styles: `
    .dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--citra-space-4);
    }
    .intro {
      margin: 0;
      font-size: var(--citra-text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddUrlDialog {
  private readonly documentsApi = inject(DocumentsApi);

  /** Two-way bound: `[(visible)]="showAddUrl"`. */
  readonly visible = model(false);
  /** The bot the page is added to. */
  readonly botId = input.required<string>();
  /** Emits the new document after the server saved it. */
  readonly added = output<CitraDocument>();

  protected readonly maxLength = URL_MAX_LENGTH;

  protected readonly form = inject(NonNullableFormBuilder).group({
    url: ['', [Validators.required, Validators.maxLength(URL_MAX_LENGTH), webAddress]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    // Every time the dialog opens it starts empty.
    effect(() => {
      if (this.visible()) {
        this.form.reset();
        this.errorMessage.set(null);
        this.submitting.set(false);
      }
    });
  }

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

    const url = normalizeUrl(this.form.getRawValue().url)!;
    this.documentsApi.addUrl(this.botId(), url).subscribe({
      next: (document) => {
        this.submitting.set(false);
        this.added.emit(document);
        this.visible.set(false);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        const { message, fieldErrors } = toFormError(error);
        const urlError = fieldErrors['url']?.[0];
        if (urlError) {
          const control = this.form.controls.url;
          control.setErrors({ ...control.errors, server: urlError });
          control.markAsTouched();
        } else {
          this.errorMessage.set(message);
        }
      },
    });
  }

  protected cancel(): void {
    this.visible.set(false);
  }

  protected showError(): boolean {
    const control = this.form.controls.url;
    return control.invalid && control.touched;
  }

  protected errorText(): string {
    const errors = this.form.controls.url.errors ?? {};
    if (errors['server']) return errors['server'] as string;
    if (errors['required']) return 'Enter a web address.';
    if (errors['maxlength']) return 'This web address is too long.';
    return 'Enter a full web address, e.g. https://example.com/help.';
  }
}
