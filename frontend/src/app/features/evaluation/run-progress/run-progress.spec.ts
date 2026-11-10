import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EvaluationApi } from '@core/evaluation/evaluation-api';
import { EvalRun } from '@core/models/evaluation.models';
import { of } from 'rxjs';

import { RUN_POLL_MS, RunProgress } from './run-progress';

const started: EvalRun = {
  id: 'r1',
  evalSetId: 's1',
  botId: 'b1',
  status: 'Pending',
  model: 'claude-sonnet-5-5',
  notes: null,
  totalQuestions: 4,
  answeredQuestions: 0,
  correctCount: 0,
  partialCount: 0,
  retrievalHitCount: 0,
  score: null,
  totalCostUsd: 0,
  error: null,
  createdAt: '',
  startedAt: null,
  completedAt: null,
};

describe('RunProgress', () => {
  const run = vi.fn();

  beforeEach(() => {
    vi.useFakeTimers();
    run.mockReset();
    TestBed.configureTestingModule({
      imports: [RunProgress],
      providers: [provideRouter([]), { provide: EvaluationApi, useValue: { run } }],
    });
  });

  afterEach(() => vi.useRealTimers());

  async function render() {
    const fixture = TestBed.createComponent(RunProgress);
    fixture.componentRef.setInput('run', started);
    const finished: EvalRun[] = [];
    fixture.componentInstance.finished.subscribe((r) => finished.push(r));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tick = async () => {
      await vi.advanceTimersByTimeAsync(RUN_POLL_MS);
      fixture.detectChanges();
    };
    return { el, finished, tick, fixture };
  }

  const text = (el: HTMLElement) =>
    el.querySelector('.run')?.textContent?.replace(/\s+/g, ' ').trim();

  it('follows the run until it is completed, then shows its score', async () => {
    run
      .mockReturnValueOnce(of({ ...started, status: 'Running', answeredQuestions: 1 }))
      .mockReturnValueOnce(
        of({
          ...started,
          status: 'Completed',
          answeredQuestions: 4,
          correctCount: 2,
          partialCount: 1,
          score: 62.5,
          totalCostUsd: 0.0012,
        }),
      );
    const { el, finished, tick } = await render();

    expect(text(el)).toBe('Waiting to start… 0 of 4');
    await tick();
    expect(text(el)).toBe('Answering questions… 1 of 4');
    expect(finished).toEqual([]);
    await tick();
    expect(text(el)).toBe('62.5% 2 correct, 1 partly correct of 4 · $0.0012 View results');
    expect(el.querySelector('a')?.getAttribute('href')).toBe('/evaluation/runs/r1');
    expect(finished.map((r) => r.status)).toEqual(['Completed']);

    await tick();
    expect(run).toHaveBeenCalledTimes(2); // no more checks once it is done
  });

  it('starts a newer run from its own state, not the previous result', async () => {
    run
      .mockReturnValueOnce(of({ ...started, status: 'Completed', answeredQuestions: 4, score: 50 }))
      .mockReturnValue(of({ ...started, id: 'r2', status: 'Running', answeredQuestions: 1 }));
    const { el, finished, tick, fixture } = await render();
    await tick();
    expect(finished.length).toBe(1);

    fixture.componentRef.setInput('run', { ...started, id: 'r2' });
    fixture.detectChanges();
    expect(text(el)).toBe('Waiting to start… 0 of 4');
    await tick();
    expect(text(el)).toBe('Answering questions… 1 of 4');
    expect(finished.length).toBe(1);
  });

  it('says why a run failed', async () => {
    run.mockReturnValueOnce(
      of({ ...started, status: 'Failed', error: 'The AI service failed: Overloaded' }),
    );
    const { el, finished, tick } = await render();

    await tick();

    expect(el.textContent).toContain('The run failed: The AI service failed: Overloaded');
    expect(finished.map((r) => r.status)).toEqual(['Failed']);
  });
});
