import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EvaluationApi } from '@core/evaluation/evaluation-api';
import { EvalGrade, EvalResult, EvalRun } from '@core/models/evaluation.models';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { TagModule } from 'primeng/tag';
import { forkJoin } from 'rxjs';

import { GRADE_LABELS } from '../run-results/run-results-page';

const POINTS: Record<EvalGrade, number> = { Wrong: 0, Partial: 1, Correct: 2 };

export type GradeChange = 'better' | 'worse' | 'same' | 'new';

export interface ComparedQuestion {
  questionId: string;
  question: string;
  /** null: the question was not in the earlier run (added since). */
  before: EvalGrade | null;
  after: EvalGrade;
  change: GradeChange;
}

/** Each question of the later run with its grade in both runs, in the later run's order. */
export function compareResults(before: EvalResult[], after: EvalResult[]): ComparedQuestion[] {
  const earlier = new Map(before.map((r) => [r.questionId, r.grade]));
  return after.map((r) => {
    const old = earlier.get(r.questionId) ?? null;
    const change: GradeChange =
      old === null
        ? 'new'
        : POINTS[r.grade] > POINTS[old]
          ? 'better'
          : POINTS[r.grade] < POINTS[old]
            ? 'worse'
            : 'same';
    return { questionId: r.questionId, question: r.question, before: old, after: r.grade, change };
  });
}

interface Side {
  run: EvalRun;
  results: EvalResult[];
}

/** Two runs of a test set side by side: did a change (instructions, model, documents) improve the score? */
@Component({
  selector: 'app-run-compare-page',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    EmptyState,
    PageHeader,
    ButtonModule,
    SkeletonModule,
    TagModule,
  ],
  templateUrl: './run-compare-page.html',
  styleUrl: './run-compare-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RunComparePage {
  private readonly evaluationApi = inject(EvaluationApi);

  /** From the route: /evaluation/runs/:id/compare/:otherId (either order; the older run is "before"). */
  readonly id = input.required<string>();
  readonly otherId = input.required<string>();

  protected readonly gradeLabels = GRADE_LABELS;

  protected readonly runs = rxResource({
    params: () => [this.id(), this.otherId()] as const,
    stream: ({ params }) =>
      forkJoin(
        params.map((id) =>
          forkJoin({ run: this.evaluationApi.run(id), results: this.evaluationApi.results(id) }),
        ),
      ),
  });

  /** The older run first. */
  protected readonly sides = computed<[Side, Side] | null>(() => {
    if (!this.runs.hasValue()) {
      return null;
    }
    const [a, b] = this.runs.value();
    return a.run.createdAt <= b.run.createdAt ? [a, b] : [b, a];
  });
  protected readonly rows = computed(() => {
    const sides = this.sides();
    return sides ? compareResults(sides[0].results, sides[1].results) : [];
  });
  protected readonly scoreChange = computed(() => {
    const sides = this.sides();
    return sides ? (sides[1].run.score ?? 0) - (sides[0].run.score ?? 0) : 0;
  });
  protected readonly counts = computed(() => {
    const rows = this.rows();
    return {
      better: rows.filter((r) => r.change === 'better').length,
      worse: rows.filter((r) => r.change === 'worse').length,
    };
  });
}
