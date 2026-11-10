import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { EvaluationApi } from '@core/evaluation/evaluation-api';
import { EvalResult, EvalRun } from '@core/models/evaluation.models';
import { of, throwError } from 'rxjs';

import { RunResultsPage } from './run-results-page';

const run: EvalRun = {
  id: 'r1',
  evalSetId: 's1',
  botId: 'b1',
  status: 'Completed',
  model: 'claude-sonnet-5-5',
  notes: 'prompt v2',
  totalQuestions: 3,
  answeredQuestions: 3,
  correctCount: 1,
  partialCount: 1,
  retrievalHitCount: 1,
  score: 50,
  totalCostUsd: 0.0021,
  error: null,
  createdAt: '2026-11-10T09:30:00',
  startedAt: null,
  completedAt: null,
};

const result = (r: Partial<EvalResult>): EvalResult => ({
  questionId: 'q',
  question: 'Q?',
  expectedAnswer: 'A.',
  expectedDocumentId: null,
  actualAnswer: 'A.',
  grade: 'Correct',
  judgeReason: '100% of the expected answer’s words are in the answer.',
  retrievalHit: false,
  latencyMs: 900,
  costUsd: 0.0007,
  ...r,
});

describe('RunResultsPage', () => {
  const api = {
    run: vi.fn(() => of(run)),
    runs: vi.fn(() =>
      of([
        run,
        { ...run, id: 'r0', notes: 'v1', score: 25, createdAt: '2026-11-09T10:00:00' },
        { ...run, id: 'r9', status: 'Running' as const, score: null },
      ]),
    ),
    results: vi.fn(() =>
      of([
        result({
          questionId: 'q1',
          question: 'Vacation days?',
          expectedDocumentId: 'd1',
          retrievalHit: true,
        }),
        result({
          questionId: 'q2',
          question: 'Parking?',
          grade: 'Partial',
          expectedDocumentId: 'd1',
        }),
        result({ questionId: 'q3', question: 'Gym?', grade: 'Wrong', actualAnswer: 'No idea.' }),
      ]),
    ),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      imports: [RunResultsPage],
      providers: [provideRouter([]), { provide: EvaluationApi, useValue: api }],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(RunResultsPage);
    fixture.componentRef.setInput('id', 'r1');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim();

  it('shows the score, the counts, the documents found and the cost', async () => {
    const el = await render();

    expect(api.run).toHaveBeenCalledWith('r1');
    expect([...el.querySelectorAll('.tile .value')].map(text)).toEqual([
      '50%',
      '1 / 1 / 1',
      '1 of 2',
      '$0.0021',
    ]);
    expect(el.textContent).toContain('prompt v2');
  });

  it('lists every question with its grade, answer and reason', async () => {
    const el = await render();

    const items = [...el.querySelectorAll('.results > li')];
    expect(items.map((i) => text(i.querySelector('.question')))).toEqual([
      'Vacation days?',
      'Parking?',
      'Gym?',
    ]);
    expect(items.map((i) => text(i.querySelector('p-tag')))).toEqual([
      'Correct',
      'Partly correct',
      'Wrong',
    ]);
    expect(text(items[2].querySelector('.actual'))).toBe('No idea.');
    expect(items.map((i) => text(i.querySelector('.document')) ?? null)).toEqual([
      'Found',
      'Not found',
      null,
    ]);
  });

  it('compares with another completed run of the set', async () => {
    const el = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    expect(api.runs).toHaveBeenCalledWith('s1');
    expect(el.querySelector('.compare p-select')).not.toBeNull();
    const fixture = TestBed.createComponent(RunResultsPage);
    fixture.componentRef.setInput('id', 'r1');
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance as unknown as {
      compareOptions(): { label: string; value: string }[];
      compareWith(id: string | null): void;
    };

    expect(component.compareOptions()).toEqual([{ label: 'Nov 9, 10:00 · v1 (25%)', value: 'r0' }]);
    component.compareWith('r0');
    expect(navigate).toHaveBeenCalledWith(['/evaluation/runs', 'r1', 'compare', 'r0']);
  });

  it('says when the run does not exist', async () => {
    api.run.mockReturnValueOnce(throwError(() => new Error('404')));
    const el = await render();

    expect(el.textContent).toContain('Run not found');
  });
});
