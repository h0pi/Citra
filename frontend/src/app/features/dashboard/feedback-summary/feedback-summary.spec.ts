import { TestBed } from '@angular/core/testing';
import { FeedbackSummary } from '@core/models/analytics.models';

import { FeedbackSummaryCard } from './feedback-summary';

const summary: FeedbackSummary = {
  positive: 3,
  negative: 1,
  badAnswers: [
    {
      conversationId: 'c1',
      botId: 'b1',
      question: 'Parking?',
      answer: 'Park anywhere.',
      answeredAt: '2026-10-10T09:30:00',
    },
  ],
};

describe('FeedbackSummaryCard', () => {
  async function render(value: FeedbackSummary, canView = true) {
    const fixture = TestBed.createComponent(FeedbackSummaryCard);
    fixture.componentRef.setInput('summary', value);
    fixture.componentRef.setInput('canView', canView);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim();
  const ratings = (el: HTMLElement) => [...el.querySelectorAll('.ratings > *')].map(text);

  it('shows the ratings and the badly rated answers, which open their chats', async () => {
    const { fixture, el } = await render(summary);
    const opened: string[] = [];
    fixture.componentInstance.view.subscribe((id) => opened.push(id));

    expect(ratings(el)).toEqual(['3 good', '1 bad', '75% good']);
    expect(text(el.querySelector('.question'))).toBe('Parking?');
    expect(text(el.querySelector('.answer'))).toBe('Park anywhere.');
    expect(text(el.querySelector('.meta'))).toBe('Oct 10, 09:30 View conversation');
    el.querySelector<HTMLButtonElement>('.meta button')!.click();
    expect(opened).toEqual(['c1']);
  });

  it('has no chat links for viewers', async () => {
    const { el } = await render(summary, false);

    expect(el.querySelector('.meta button')).toBeNull();
  });

  it('shows no share and an empty note without ratings', async () => {
    const { el } = await render({ positive: 0, negative: 0, badAnswers: [] });

    expect(ratings(el)).toEqual(['0 good', '0 bad']);
    expect(el.textContent).toContain('No badly rated answers in this period.');
  });
});
