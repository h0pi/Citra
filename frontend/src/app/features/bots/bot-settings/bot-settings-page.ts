import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { BotsApi } from '@core/bots/bots-api';
import { CurrentBot } from '@core/bots/current-bot';
import { HasUnsavedChanges } from '@core/guards/unsaved-changes.guard';
import { BOT_LIMITS, Bot } from '@core/models/bot.models';
import { toFormError } from '@core/models/problem-details';
import { NotificationService } from '@core/services/notification.service';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { SkeletonModule } from 'primeng/skeleton';
import { TabsModule } from 'primeng/tabs';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { forkJoin } from 'rxjs';

import { DeleteBotDialog } from '../delete-bot-dialog/delete-bot-dialog';
import { EmbedPanel } from '../embed-panel/embed-panel';
import { SearchPlayground } from '../search-playground/search-playground';

const HEX_COLOR = /^#[0-9A-Fa-f]{6}$/;
/** Only the domain, as the backend checks it: "acme.com", not "https://acme.com/page". */
const DOMAIN = /^([A-Za-z0-9-]+\.)*[A-Za-z0-9-]+$/;

/** Friendly names for the model dropdown; unknown ids are shown as they are. */
const MODEL_LABELS: Record<string, string> = {
  'claude-opus-5-5': 'Claude Opus 5.5 · most capable, highest cost',
  'claude-sonnet-5-5': 'Claude Sonnet 5.5 · balanced quality and cost',
  'claude-haiku-4-5': 'Claude Haiku 4.5 · fastest, lowest cost',
};

interface ModelOption {
  label: string;
  value: string | null;
}

type Field =
  | 'name'
  | 'description'
  | 'systemPrompt'
  | 'welcomeMessage'
  | 'starterQuestions'
  | 'widgetColor'
  | 'allowedDomains'
  | 'dailyMessageLimit';

/** Starter questions are typed one per line; empty lines do not count. */
const lines = (text: string) =>
  text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);

function starterQuestionsValidator(control: AbstractControl<string>): ValidationErrors | null {
  const questions = lines(control.value);
  if (questions.length > BOT_LIMITS.starterQuestions) {
    return { starterQuestions: `Use at most ${BOT_LIMITS.starterQuestions} questions.` };
  }
  if (questions.some((q) => q.length > BOT_LIMITS.starterQuestion)) {
    return { starterQuestions: `Keep each question to ${BOT_LIMITS.starterQuestion} characters.` };
  }
  return null;
}

function allowedDomainsValidator(control: AbstractControl<string>): ValidationErrors | null {
  const domains = lines(control.value);
  if (domains.length > BOT_LIMITS.allowedDomains) {
    return { allowedDomains: `Use at most ${BOT_LIMITS.allowedDomains} websites.` };
  }
  if (domains.some((d) => !DOMAIN.test(d))) {
    return { allowedDomains: 'Use domain names like acme.com, without https:// or a path.' };
  }
  return null;
}

function dailyMessageLimitValidator(
  control: AbstractControl<number | null>,
): ValidationErrors | null {
  const limit = control.value;
  return limit === null || (Number.isInteger(limit) && limit >= 0)
    ? null
    : { dailyMessageLimit: 'Use a whole number, or leave it empty.' };
}

/**
 * Edit one bot: name, instructions, model, answer style and widget color. Owners and admins also get the search
 * playground, to check what the bot finds for a question, and the Embed tab with the website snippet.
 */
@Component({
  selector: 'app-bot-settings-page',
  imports: [
    NgTemplateOutlet,
    ReactiveFormsModule,
    RouterLink,
    PageHeader,
    EmptyState,
    DeleteBotDialog,
    EmbedPanel,
    SearchPlayground,
    ButtonModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    SkeletonModule,
    TabsModule,
    TextareaModule,
    ToggleSwitchModule,
  ],
  templateUrl: './bot-settings-page.html',
  styleUrl: './bot-settings-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class BotSettingsPage implements HasUnsavedChanges {
  private readonly botsApi = inject(BotsApi);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly currentBot = inject(CurrentBot);

  /** From the route: /bots/:id. */
  readonly id = input.required<string>();

  protected readonly canEdit = inject(AuthService).canEdit;
  protected readonly limits = BOT_LIMITS;

  protected readonly bot = signal<Bot | null>(null);
  protected readonly modelOptions = signal<ModelOption[]>([]);
  protected readonly state = signal<'loading' | 'ready' | 'not-found' | 'error'>('loading');
  protected readonly saving = signal(false);
  protected readonly showDelete = signal(false);
  /** Open tab (owners and admins): the settings form, the search playground or the embed snippet. */
  protected readonly tab = signal<string | number | undefined>('settings');
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(BOT_LIMITS.name)]],
    description: ['', [Validators.maxLength(BOT_LIMITS.description)]],
    welcomeMessage: ['', [Validators.required, Validators.maxLength(BOT_LIMITS.welcomeMessage)]],
    starterQuestions: ['', [starterQuestionsValidator]],
    systemPrompt: ['', [Validators.maxLength(BOT_LIMITS.systemPrompt)]],
    model: [null as string | null],
    widgetColor: ['', [Validators.required, Validators.pattern(HEX_COLOR)]],
    isPublic: [false],
    allowedDomains: ['', [allowedDomainsValidator]],
    dailyMessageLimit: [null as number | null, [dailyMessageLimitValidator]],
  });

  // Signals from the form, so the template (OnPush, zoneless) updates as the user types.
  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.value });
  private readonly formStatus = toSignal(this.form.statusChanges, {
    initialValue: this.form.status,
  });

  protected readonly systemPromptLength = computed(
    () => this.formValue().systemPrompt?.length ?? 0,
  );
  /** Live widget preview; falls back to the saved color while the typed hex is incomplete. */
  protected readonly previewColor = computed(() => {
    const color = this.formValue().widgetColor ?? '';
    return HEX_COLOR.test(color) ? color : (this.bot()?.widgetColor ?? '#1F5F5B');
  });
  protected readonly dirty = computed(() => {
    this.formValue();
    this.formStatus();
    return this.form.dirty;
  });

  constructor() {
    // Loads again if the id in the URL changes (e.g. switching bots with the top-bar selector).
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  protected load(id: string): void {
    this.state.set('loading');
    this.errorMessage.set(null);
    forkJoin({ bot: this.botsApi.get(id), models: this.botsApi.models() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ bot, models }) => {
          this.modelOptions.set([
            { label: 'Default (recommended)', value: null },
            ...models.map((m) => ({ label: MODEL_LABELS[m] ?? m, value: m })),
          ]);
          this.showBot(bot);
          this.state.set('ready');
        },
        error: (error: unknown) => {
          this.state.set(
            error instanceof HttpErrorResponse && error.status === 404 ? 'not-found' : 'error',
          );
        },
      });
  }

  protected save(): void {
    const bot = this.bot();
    if (!bot || !this.canEdit()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    this.botsApi
      .update(bot.id, {
        name: value.name.trim(),
        description: value.description.trim() || null,
        systemPrompt: value.systemPrompt.trim() || null,
        welcomeMessage: value.welcomeMessage.trim(),
        model: value.model,
        widgetColor: value.widgetColor.toUpperCase(),
        starterQuestions: lines(value.starterQuestions),
        isPublic: value.isPublic,
        allowedDomains: lines(value.allowedDomains),
        dailyMessageLimit: value.dailyMessageLimit,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.showBot(saved);
          this.currentBot.refresh(); // new name/color in the top-bar selector
          this.notifications.success('Settings saved');
        },
        error: (error: unknown) => {
          this.saving.set(false);
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

  /** The bot is gone: back to the list (without the "unsaved changes" question). */
  protected onDeleted(): void {
    const name = this.bot()?.name ?? 'The bot';
    this.form.markAsPristine();
    this.currentBot.refresh(); // it may have been the current bot
    this.notifications.success('Bot deleted', `"${name}" and all its data were removed.`);
    void this.router.navigateByUrl('/bots');
  }

  /** From the color picker: updates the hex field too, and counts as an edit. */
  protected pickColor(color: string): void {
    const control = this.form.controls.widgetColor;
    control.setValue(color.toUpperCase());
    control.markAsDirty();
  }

  /** Throws away edits and shows the last saved values again. */
  protected discard(): void {
    const bot = this.bot();
    if (bot) {
      this.showBot(bot);
    }
  }

  hasUnsavedChanges(): boolean {
    return this.canEdit() && this.form.dirty;
  }

  /** Closing or reloading the browser tab with unsaved edits: let the browser ask first. */
  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  protected showError(field: Field): boolean {
    const control = this.form.controls[field];
    return control.invalid && control.touched;
  }

  protected errorFor(field: Field): string {
    const errors = this.form.controls[field].errors ?? {};
    if (errors['server']) return errors['server'] as string;
    if (errors['required']) return 'This field is required.';
    if (errors['pattern']) return 'Use a hex color like #1F5F5B.';
    if (typeof errors[field] === 'string') return errors[field]; // the field's own validator
    if (errors['maxlength']) {
      return `Use at most ${(errors['maxlength'] as { requiredLength: number }).requiredLength} characters.`;
    }
    return 'Please check this field.';
  }

  /** Fills the form with a bot's saved values and marks it as unchanged. */
  private showBot(bot: Bot): void {
    this.bot.set(bot);
    this.errorMessage.set(null);
    this.form.reset({
      name: bot.name,
      description: bot.description ?? '',
      welcomeMessage: bot.welcomeMessage,
      starterQuestions: bot.starterQuestions.join('\n'),
      systemPrompt: bot.systemPrompt ?? '',
      model: bot.model,
      widgetColor: bot.widgetColor,
      isPublic: bot.isPublic,
      allowedDomains: bot.allowedDomains.join('\n'),
      dailyMessageLimit: bot.dailyMessageLimit,
    });
    if (this.canEdit()) {
      this.form.enable();
    } else {
      this.form.disable();
    }
  }
}
