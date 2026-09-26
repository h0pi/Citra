import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BotsApi } from '@core/bots/bots-api';
import { toFormError } from '@core/models/problem-details';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';

/**
 * Permanently deletes a bot. The user must type the bot's name first, because this also removes
 * every document, chat and evaluation of the bot and cannot be undone.
 */
@Component({
  selector: 'app-delete-bot-dialog',
  imports: [FormsModule, DialogModule, ButtonModule, InputTextModule, MessageModule],
  templateUrl: './delete-bot-dialog.html',
  styleUrl: './delete-bot-dialog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeleteBotDialog {
  private readonly botsApi = inject(BotsApi);

  readonly bot = input.required<{ id: string; name: string }>();
  /** Two-way bound: `[(visible)]="showDelete"`. */
  readonly visible = model(false);
  /** Emits after the bot is gone. */
  readonly deleted = output<void>();

  protected readonly typedName = signal('');
  protected readonly deleting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly confirmed = computed(() => this.typedName().trim() === this.bot().name);

  constructor() {
    effect(() => {
      if (this.visible()) {
        this.typedName.set('');
        this.errorMessage.set(null);
        this.deleting.set(false);
      }
    });
  }

  protected confirm(): void {
    if (!this.confirmed() || this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.errorMessage.set(null);

    this.botsApi.delete(this.bot().id).subscribe({
      next: () => this.finish(),
      error: (error: unknown) => {
        // Already deleted (e.g. in another tab): the result the user wanted.
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.finish();
          return;
        }
        this.deleting.set(false);
        this.errorMessage.set(
          toFormError(error, "Couldn't delete the bot. Please try again.").message,
        );
      },
    });
  }

  protected cancel(): void {
    this.visible.set(false);
  }

  private finish(): void {
    this.deleting.set(false);
    this.visible.set(false);
    this.deleted.emit();
  }
}
