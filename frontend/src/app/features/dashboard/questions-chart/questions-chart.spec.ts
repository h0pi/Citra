import { TestBed } from '@angular/core/testing';
import { DayCount } from '@core/models/analytics.models';

import { QuestionsChart } from './questions-chart';

describe('QuestionsChart', () => {
  async function render(days: DayCount[]) {
    const fixture = TestBed.createComponent(QuestionsChart);
    fixture.componentRef.setInput('days', days);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('draws one bar per day, the busiest day full height', async () => {
    const el = await render([
      { date: '2026-10-08', questions: 2 },
      { date: '2026-10-09', questions: 0 },
      { date: '2026-10-10', questions: 4 },
    ]);

    const bars = [...el.querySelectorAll<HTMLElement>('.bar')].map((b) => b.style.height);
    expect(bars).toEqual(['50%', '0%', '100%']);
    expect([...el.querySelectorAll('.sr-only')].map((s) => s.textContent?.trim())).toEqual([
      'Oct 8: 2',
      'Oct 9: 0',
      'Oct 10: 4',
    ]);
    expect(el.textContent).toContain('6 in total, busiest day 4');
    expect([...el.querySelectorAll('.axis span')].map((s) => s.textContent)).toEqual([
      'Oct 8',
      'Oct 10',
    ]);
  });

  it('handles a period without questions', async () => {
    const el = await render([{ date: '2026-10-10', questions: 0 }]);

    expect(el.querySelector<HTMLElement>('.bar')!.style.height).toBe('0%');
    expect(el.textContent).toContain('0 in total');
  });
});
