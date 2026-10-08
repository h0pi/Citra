import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { BotsApi } from '@core/bots/bots-api';
import { CurrentBot } from '@core/bots/current-bot';
import { Bot, BotSummary } from '@core/models/bot.models';
import { NotificationService } from '@core/services/notification.service';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { TagModule } from 'primeng/tag';

import { CreateBotDialog } from './create-bot-dialog/create-bot-dialog';

/** The company's bots as cards: name, description, public/private, document and message counts. */
@Component({
  selector: 'app-bots-page',
  imports: [
    PageHeader,
    EmptyState,
    CreateBotDialog,
    RouterLink,
    DatePipe,
    ButtonModule,
    SkeletonModule,
    TagModule,
  ],
  templateUrl: './bots-page.html',
  styleUrl: './bots-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BotsPage {
  private readonly botsApi = inject(BotsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly notifications = inject(NotificationService);
  private readonly currentBot = inject(CurrentBot);

  /** Only Owner and Admin see the create button (the API enforces the same rule). */
  protected readonly canEdit = inject(AuthService).canEdit;
  protected readonly showCreate = signal(false);

  protected readonly bots = signal<BotSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Placeholder cards shown while loading. */
  protected readonly skeletons = [1, 2, 3];

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.botsApi
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (bots) => {
          this.bots.set(bots);
          this.loading.set(false);
        },
        // The error interceptor already shows a toast; the page itself offers a retry.
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  /** Adds the new bot to the list right away (no reload), keeping the list sorted by name. */
  protected onCreated(bot: Bot): void {
    const summary: BotSummary = {
      id: bot.id,
      name: bot.name,
      slug: bot.slug,
      description: bot.description,
      isPublic: bot.isPublic,
      widgetColor: bot.widgetColor,
      starterQuestions: [],
      documentCount: 0,
      messageCount: 0,
      createdAt: bot.createdAt,
      updatedAt: bot.updatedAt,
    };
    this.bots.update((bots) =>
      [...bots, summary].sort((a, b) =>
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
      ),
    );
    // A new bot is usually the one the user wants to work on next (upload documents to it).
    this.currentBot.refresh(bot.id);
    this.notifications.success('Bot created', `"${bot.name}" is ready for documents.`);
  }

  /** "1 document", "3 documents". */
  protected plural(count: number, word: string): string {
    return `${count} ${word}${count === 1 ? '' : 's'}`;
  }
}
