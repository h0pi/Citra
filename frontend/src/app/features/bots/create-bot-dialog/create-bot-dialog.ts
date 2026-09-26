import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  model,
  output,
  signal,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { BotsApi } from '@core/bots/bots-api';
import { BOT_LIMITS, Bot } from '@core/models/bot.models';
import { toFormError } from '@core/models/problem-details';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TextareaModule } from 'primeng/textarea';

type Field = 'name' | 'description';

/**
 * "New bot" dialog: asks only for a name and an optional description.
 * Everything else (instructions, model, widget) starts with defaults and is changed in the bot's settings.
 */
@Component({
  selector: 'app-create-bot-dialog',
  imports: [
    ReactiveFormsModule,
    DialogModule,
    ButtonModule,
    InputTextModule,
    TextareaModule,
    MessageModule,
  ],
  templateUrl: './create-bot-dialog.html',
  styleUrl: './create-bot-dialog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateBotDialog {
  private readonly botsApi = inject(BotsApi);

  /** Two-way bound: `[(visible)]="showCreate"`. */
  readonly visible = model(false);
  /** Emits the new bot after the server saved it. */
  readonly created = output<Bot>();

  protected readonly limits = BOT_LIMITS;

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(BOT_LIMITS.name)]],
    description: ['', [Validators.maxLength(BOT_LIMITS.description)]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    // Every time the dialog opens it starts empty (not with the previous bot's text or errors).
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

    const { name, description } = this.form.getRawValue();
    this.botsApi.create({ name: name.trim(), description: description.trim() || null }).subscribe({
      next: (bot) => {
        this.submitting.set(false);
        this.created.emit(bot);
        this.visible.set(false);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        const { message, fieldErrors } = toFormError(error);
        this.errorMessage.set(message);
        for (const [field, messages] of Object.entries(fieldErrors)) {
          const control = this.form.get(field);
          if (control && messages.length > 0) {
            control.setErrors({ ...control.errors, server: messages[0] });
            control.markAsTouched();
          }
        }
      },
    });
  }

  protected cancel(): void {
    this.visible.set(false);
  }

  protected showError(field: Field): boolean {
    const control = this.form.controls[field];
    return control.invalid && control.touched;
  }

  protected errorFor(field: Field): string {
    const errors = this.form.controls[field].errors ?? {};
    if (errors['server']) return errors['server'] as string;
    if (errors['required']) return 'Give your bot a name.';
    if (errors['maxlength']) return `Use at most ${BOT_LIMITS[field]} characters.`;
    return 'Please check this field.';
  }
}
