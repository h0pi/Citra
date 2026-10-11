import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AnalyticsApi } from '@core/analytics/analytics-api';
import { CurrentBot } from '@core/bots/current-bot';
import { AuthService } from '@core/auth/auth.service';
import {
  AnalyticsPeriod,
  DashboardStats,
  DayCount,
  FeedbackSummary,
  UnansweredQuestion,
} from '@core/models/analytics.models';
import { BotSummary } from '@core/models/bot.models';
import { By } from '@angular/platform-browser';
import { Observable, of, throwError } from 'rxjs';

import { DashboardPage, lastDays } from './dashboard-page';
import { ConversationsBrowser } from './conversations-browser/conversations-browser';
import { FeedbackSummaryCard } from './feedback-summary/feedback-summary';
import { KnowledgeGaps } from './knowledge-gaps/knowledge-gaps';
import { QuestionsChart } from './questions-chart/questions-chart';
import { TranscriptDialog } from './transcript-dialog/transcript-dialog';

const stats: DashboardStats = {
  conversations: 1234,
  questions: 5678,
  answeredPercent: 87.5,
  averageLatencyMs: 2345,
  totalCostUsd: 0.0351,
};

describe('DashboardPage', () => {
  const statsCall = vi.fn<(period: AnalyticsPeriod) => Observable<DashboardStats>>();
  const perDayCall = vi.fn<(period: AnalyticsPeriod) => Observable<DayCount[]>>();
  const days: DayCount[] = [{ date: '2026-10-10', questions: 3 }];
  const gapsCall = vi.fn<(period: AnalyticsPeriod) => Observable<UnansweredQuestion[]>>();
  const gaps: UnansweredQuestion[] = [
    {
      conversationId: 'c1',
      botId: 'b1',
      question: 'Parking?',
      channel: 'Widget',
      askedAt: '2026-10-10T09:00:00Z',
    },
  ];
  const canEdit = signal(true);
  const feedbackCall = vi.fn<(period: AnalyticsPeriod) => Observable<FeedbackSummary>>();
  const feedback: FeedbackSummary = { positive: 2, negative: 1, badAnswers: [] };

  beforeEach(() => {
    vi.clearAllMocks();
    statsCall.mockReturnValue(of(stats));
    perDayCall.mockReturnValue(of(days));
    gapsCall.mockReturnValue(of(gaps));
    feedbackCall.mockReturnValue(of(feedback));
    canEdit.set(true);
    TestBed.configureTestingModule({
      imports: [DashboardPage],
      providers: [
        {
          provide: AnalyticsApi,
          useValue: {
            stats: statsCall,
            messagesPerDay: perDayCall,
            unansweredQuestions: gapsCall,
            feedback: feedbackCall,
            conversations: () =>
              of({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
            conversation: () => of(),
          },
        },
        { provide: AuthService, useValue: { canEdit } },
        {
          provide: CurrentBot,
          useValue: { bots: signal([{ id: 'b1', name: 'Support' }] as BotSummary[]) },
        },
      ],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(DashboardPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const cards = (el: HTMLElement) =>
    [...el.querySelectorAll('.kpi')].map((k) => [
      k.querySelector('.label')?.textContent?.trim(),
      k.querySelector('.value')?.textContent?.trim(),
    ]);

  it('shows the headline numbers of the last 30 days for all bots', async () => {
    const { el } = await render();

    expect(statsCall).toHaveBeenCalledWith({ ...lastDays(30), botId: null });
    expect(cards(el)).toEqual([
      ['Chats', '1,234'],
      ['Questions', '5,678'],
      ['Answered', '87.5%'],
      ['Avg. answer time', '2.3 s'],
      ['AI cost', '$0.0351'],
    ]);
  });

  it('loads again for another period or bot', async () => {
    const { fixture } = await render();
    const page = fixture.componentInstance as unknown as {
      days: { set(v: number): void };
      botId: { set(v: string | null): void };
    };

    page.days.set(7);
    page.botId.set('b1');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(statsCall).toHaveBeenLastCalledWith({ ...lastDays(7), botId: 'b1' });
    expect(perDayCall).toHaveBeenLastCalledWith({ ...lastDays(7), botId: 'b1' });
    expect(gapsCall).toHaveBeenLastCalledWith({ ...lastDays(7), botId: 'b1' });
    expect(feedbackCall).toHaveBeenLastCalledWith({ ...lastDays(7), botId: 'b1' });
  });

  it('offers a retry when loading fails', async () => {
    statsCall.mockReturnValueOnce(throwError(() => new Error('offline')));
    const { fixture, el } = await render();

    expect(el.textContent).toContain("Couldn't load the stats.");
    el.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(statsCall).toHaveBeenCalledTimes(2);
    expect(cards(el)[0]).toEqual(['Chats', '1,234']);
  });

  it('shows questions per day in a chart, with its own retry', async () => {
    perDayCall.mockReturnValueOnce(throwError(() => new Error('offline')));
    const { fixture, el } = await render();

    expect(el.textContent).toContain("Couldn't load the chart.");
    el.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    const chart = fixture.debugElement.query(By.directive(QuestionsChart));
    expect((chart.componentInstance as QuestionsChart).days()).toEqual(days);
  });

  it('lists the knowledge gaps and opens a chat read-only', async () => {
    const { fixture } = await render();
    const table = () =>
      fixture.debugElement.query(By.directive(KnowledgeGaps)).componentInstance as KnowledgeGaps;
    const dialog = () =>
      fixture.debugElement.query(By.directive(TranscriptDialog))
        .componentInstance as TranscriptDialog;

    expect(table().gaps()).toEqual(gaps);
    expect(table().canView()).toBe(true);
    expect(dialog().conversationId()).toBeNull();

    table().view.emit('c1');
    fixture.detectChanges();
    expect(dialog().conversationId()).toBe('c1');

    dialog().closed.emit();
    fixture.detectChanges();
    expect(dialog().conversationId()).toBeNull();
  });

  it('lets only owners and admins open chats', async () => {
    canEdit.set(false);
    const { fixture } = await render();

    const table = fixture.debugElement.query(By.directive(KnowledgeGaps));
    expect((table.componentInstance as KnowledgeGaps).canView()).toBe(false);
    const card = fixture.debugElement.query(By.directive(FeedbackSummaryCard));
    expect((card.componentInstance as FeedbackSummaryCard).canView()).toBe(false);
    expect(fixture.debugElement.query(By.directive(ConversationsBrowser))).toBeNull();
  });

  it('offers a retry when the knowledge gaps fail to load', async () => {
    gapsCall.mockReturnValueOnce(throwError(() => new Error('offline')));
    const { fixture, el } = await render();

    expect(el.textContent).toContain("Couldn't load the knowledge gaps.");
    [...el.querySelectorAll<HTMLButtonElement>('.load-error button')].at(-1)!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(gapsCall).toHaveBeenCalledTimes(2);
    expect(fixture.debugElement.query(By.directive(KnowledgeGaps))).not.toBeNull();
  });

  it('shows the feedback, whose bad answers open their chats, with its own retry', async () => {
    feedbackCall.mockReturnValueOnce(throwError(() => new Error('offline')));
    const { fixture, el } = await render();

    expect(el.textContent).toContain("Couldn't load the feedback.");
    [...el.querySelectorAll<HTMLButtonElement>('.load-error button')].at(-1)!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    const card = fixture.debugElement.query(By.directive(FeedbackSummaryCard))
      .componentInstance as FeedbackSummaryCard;
    expect(card.summary()).toEqual(feedback);
    expect(card.canView()).toBe(true);
    card.view.emit('c7');
    fixture.detectChanges();
    const dialog = fixture.debugElement.query(By.directive(TranscriptDialog))
      .componentInstance as TranscriptDialog;
    expect(dialog.conversationId()).toBe('c7');
  });

  it('lets owners and admins browse the chats of the chosen bot', async () => {
    const { fixture } = await render();
    const browser = () =>
      fixture.debugElement.query(By.directive(ConversationsBrowser))
        .componentInstance as ConversationsBrowser;
    expect(browser().botId()).toBeNull();

    (fixture.componentInstance as unknown as { botId: { set(v: string): void } }).botId.set('b1');
    fixture.detectChanges();
    expect(browser().botId()).toBe('b1');

    browser().view.emit('c9');
    fixture.detectChanges();
    const dialog = fixture.debugElement.query(By.directive(TranscriptDialog))
      .componentInstance as TranscriptDialog;
    expect(dialog.conversationId()).toBe('c9');
  });

  it('counts the last days in UTC, today included', () => {
    expect(lastDays(7, new Date('2026-10-10T23:30:00Z'))).toEqual({
      from: '2026-10-04',
      to: '2026-10-10',
    });
  });
});
