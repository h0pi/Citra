import { TestBed } from '@angular/core/testing';
import { UnansweredQuestion } from '@core/models/analytics.models';

import { KnowledgeGaps } from './knowledge-gaps';

const gaps: UnansweredQuestion[] = [
  {
    conversationId: 'c1',
    botId: 'b1',
    question: 'Where do I park?',
    channel: 'Widget',
    askedAt: '2026-10-10T09:30:00',
  },
  {
    conversationId: 'c2',
    botId: 'b1',
    question: 'Is there a gym?',
    channel: 'Admin',
    askedAt: '2026-10-09T14:05:00',
  },
];

describe('KnowledgeGaps', () => {
  async function render(list: UnansweredQuestion[], canView = true) {
    const fixture = TestBed.createComponent(KnowledgeGaps);
    fixture.componentRef.setInput('gaps', list);
    fixture.componentRef.setInput('canView', canView);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const rows = (el: HTMLElement) =>
    [...el.querySelectorAll('tbody tr')].map((r) =>
      [...r.querySelectorAll('td')].map((c) => c.textContent?.trim()),
    );

  it('lists each question with where and when it was asked, and opens its chat', async () => {
    const { fixture, el } = await render(gaps);
    const opened: string[] = [];
    fixture.componentInstance.view.subscribe((id) => opened.push(id));

    expect(rows(el)).toEqual([
      ['Where do I park?', 'Website', 'Oct 10, 09:30', 'View conversation'],
      ['Is there a gym?', 'Admin app', 'Oct 9, 14:05', 'View conversation'],
    ]);
    el.querySelectorAll<HTMLButtonElement>('tbody button')[1].click();
    expect(opened).toEqual(['c2']);
  });

  it('has no chat links for viewers', async () => {
    const { el } = await render(gaps, false);

    expect(el.querySelector('tbody button')).toBeNull();
    expect(rows(el)[0]).toEqual(['Where do I park?', 'Website', 'Oct 10, 09:30']);
  });

  it('says when there are no gaps', async () => {
    const { el } = await render([]);

    expect(el.querySelector('table')).toBeNull();
    expect(el.textContent).toContain('No unanswered questions in this period.');
  });
});
