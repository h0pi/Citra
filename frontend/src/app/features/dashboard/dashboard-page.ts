import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AnalyticsApi } from '@core/analytics/analytics-api';
import { AuthService } from '@core/auth/auth.service';
import { CurrentBot } from '@core/bots/current-bot';
import { AnalyticsPeriod, DashboardStats } from '@core/models/analytics.models';
import { PageHeader } from '@shared/components/page-header/page-header';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { SkeletonModule } from 'primeng/skeleton';

import { ConversationsBrowser } from './conversations-browser/conversations-browser';
import { FeedbackSummaryCard } from './feedback-summary/feedback-summary';
import { KnowledgeGaps } from './knowledge-gaps/knowledge-gaps';
import { MostCited } from './most-cited/most-cited';
import { QuestionsChart } from './questions-chart/questions-chart';
import { TranscriptDialog } from './transcript-dialog/transcript-dialog';

export const PERIOD_OPTIONS = [
  { label: 'Last 7 days', value: 7 },
  { label: 'Last 30 days', value: 30 },
  { label: 'Last 90 days', value: 90 },
];

/** The last `days` days up to today, as UTC dates (the API counts in UTC days). */
export function lastDays(days: number, now = new Date()): { from: string; to: string } {
  const day = (offset: number) =>
    new Date(now.getTime() - offset * 86_400_000).toISOString().slice(0, 10);
  return { from: day(days - 1), to: day(0) };
}

/** Usage, answer quality and cost of the company's bots (or one bot) over a chosen period. */
@Component({
  selector: 'app-dashboard-page',
  imports: [
    CurrencyPipe,
    DecimalPipe,
    FormsModule,
    ConversationsBrowser,
    FeedbackSummaryCard,
    KnowledgeGaps,
    MostCited,
    PageHeader,
    QuestionsChart,
    RouterLink,
    TranscriptDialog,
    ButtonModule,
    MessageModule,
    SelectModule,
    SkeletonModule,
  ],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  private readonly analyticsApi = inject(AnalyticsApi);
  private readonly currentBot = inject(CurrentBot);

  /** Owners and admins may read the company's chats. */
  protected readonly canEdit = inject(AuthService).canEdit;
  protected readonly periodOptions = PERIOD_OPTIONS;
  /** The chat open in the read-only dialog. */
  protected readonly viewing = signal<string | null>(null);
  protected readonly days = signal(30);
  /** null = all bots. */
  protected readonly botId = signal<string | null>(null);
  protected readonly botOptions = computed(() => [
    { label: 'All bots', value: null },
    ...this.currentBot.bots().map((b) => ({ label: b.name, value: b.id as string | null })),
  ]);

  /** Who answered, for the summary sentence: the chosen bot, or all of them. */
  protected readonly subject = computed(
    () => this.currentBot.bots().find((b) => b.id === this.botId())?.name ?? 'Your bots',
  );

  private readonly period = computed<AnalyticsPeriod>(() => ({
    ...lastDays(this.days()),
    botId: this.botId(),
  }));

  protected readonly stats = rxResource({
    params: () => this.period(),
    stream: ({ params }) => this.analyticsApi.stats(params),
  });

  protected readonly perDay = rxResource({
    params: () => this.period(),
    stream: ({ params }) => this.analyticsApi.messagesPerDay(params),
  });

  protected readonly cited = rxResource({
    params: () => this.period(),
    stream: ({ params }) => this.analyticsApi.mostCitedDocuments(params),
  });

  protected readonly gaps = rxResource({
    params: () => this.period(),
    stream: ({ params }) => this.analyticsApi.unansweredQuestions(params),
  });

  protected readonly feedback = rxResource({
    params: () => this.period(),
    stream: ({ params }) => this.analyticsApi.feedback(params),
  });

  /** How many of the period's questions were answered from the documents. */
  protected answered(stats: DashboardStats): number {
    return Math.round((stats.questions * stats.answeredPercent) / 100);
  }
}
