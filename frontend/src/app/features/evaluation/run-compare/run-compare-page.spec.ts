import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EvaluationApi } from '@core/evaluation/evaluation-api';
import { EvalGrade, EvalResult, EvalRun } from '@core/models/evaluation.models';
import { of } from 'rxjs';

import { RunComparePage, compareResults } from './run-compare-page';

const result = (questionId: string, grade: EvalGrade): EvalResult => ({
  questionId,
  question: `Question ${questionId}?`,
  expectedAnswer: 'A.',
  expectedDocumentId: null,
  actualAnswer: 'A.',
  grade,
  judgeReason: '',
  retrievalHit: false,
  latencyMs: 0,
  costUsd: 0,
});

const run = (id: string, createdAt: string, score: number, notes: string): EvalRun => ({
  id,
  evalSetId: 's1',
  botId: 'b1',
  status: 'Completed',
  model: 'claude-sonnet-5-5',
  notes,
  totalQuestions: 3,
  answeredQuestions: 3,
  correctCount: 0,
  partialCount: 0,
  retrievalHitCount: 0,
  score,
  totalCostUsd: 0,
  error: null,
  createdAt,
  startedAt: null,
  completedAt: null,
});

describe('compareResults', () => {
  it('marks each question of the later run better, worse, the same or new', () => {
    const rows = compareResults(
      [result('q1', 'Wrong'), result('q2', 'Correct'), result('q3', 'Partial')],
      [
        result('q1', 'Partial'),
        result('q2', 'Partial'),
        result('q3', 'Partial'),
        result('q4', 'Wrong'),
      ],
    );

    expect(rows.map((r) => [r.questionId, r.before, r.after, r.change])).toEqual([
      ['q1', 'Wrong', 'Partial', 'better'],
      ['q2', 'Correct', 'Partial', 'worse'],
      ['q3', 'Partial', 'Partial', 'same'],
      ['q4', null, 'Wrong', 'new'],
    ]);
  });
});

describe('RunComparePage', () => {
  const runs: Record<string, EvalRun> = {
    old: run('old', '2026-11-09T10:00:00', 50, 'v1'),
    new: run('new', '2026-11-10T10:00:00', 62.5, 'v2'),
  };
  const results: Record<string, EvalResult[]> = {
    old: [result('q1', 'Wrong'), result('q2', 'Correct')],
    new: [result('q1', 'Correct'), result('q2', 'Correct')],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RunComparePage],
      providers: [
        provideRouter([]),
        {
          provide: EvaluationApi,
          useValue: { run: (id: string) => of(runs[id]), results: (id: string) => of(results[id]) },
        },
      ],
    });
  });

  it('puts the older run first and shows what changed', async () => {
    const fixture = TestBed.createComponent(RunComparePage);
    fixture.componentRef.setInput('id', 'new'); // opened from the newer run's page
    fixture.componentRef.setInput('otherId', 'old');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const text = (e: Element | null) => e?.textContent?.replace(/\s+/g, ' ').trim();

    expect([...el.querySelectorAll('.tile .value')].map(text)).toEqual([
      '50%',
      '62.5%',
      '+12.5 points',
    ]);
    expect(text(el.querySelectorAll('.tile')[2].querySelector('.text-muted'))).toBe(
      '1 better, 0 worse',
    );
    expect(
      [...el.querySelectorAll('tbody tr')].map((r) =>
        [...r.querySelectorAll('td')].map((c) => text(c)),
      ),
    ).toEqual([
      ['Question q1?', 'Wrong', 'Correct', 'Better'],
      ['Question q2?', 'Correct', 'Correct', 'Same'],
    ]);
  });
});
