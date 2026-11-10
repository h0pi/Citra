import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { EvaluationApi } from '@core/evaluation/evaluation-api';
import { EvalGrade } from '@core/models/evaluation.models';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { of } from 'rxjs';

export const GRADE_LABELS: Record<EvalGrade, string> = {
  Correct: 'Correct',
  Partial: 'Partly correct',
  Wrong: 'Wrong',
};

const GRADE_SEVERITY: Record<EvalGrade, 'success' | 'warn' | 'danger'> = {
  Correct: 'success',
  Partial: 'warn',
  Wrong: 'danger',
};

/** One eval run: its score and, per question, the bot's answer, the grade and the reason. */
@Component({
  selector: 'app-run-results-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    DecimalPipe,
    FormsModule,
    RouterLink,
    EmptyState,
    PageHeader,
    SelectModule,
    TagModule,
  ],
  templateUrl: './run-results-page.html',
  styleUrl: './run-results-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RunResultsPage {
  private readonly evaluationApi = inject(EvaluationApi);
  private readonly router = inject(Router);
  private readonly datePipe = new DatePipe('en-US');

  /** From the route: /evaluation/runs/:id. */
  readonly id = input.required<string>();

  protected readonly gradeLabels = GRADE_LABELS;
  protected readonly gradeSeverity = GRADE_SEVERITY;

  protected readonly run = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.evaluationApi.run(params),
  });
  protected readonly results = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.evaluationApi.results(params),
    defaultValue: [],
  });

  /** The set's other runs, to compare this one with. */
  protected readonly otherRuns = rxResource({
    params: () => (this.run.hasValue() ? this.run.value().evalSetId : undefined),
    stream: ({ params }) => (params ? this.evaluationApi.runs(params) : of([])),
    defaultValue: [],
  });
  protected readonly compareOptions = computed(() =>
    this.otherRuns
      .value()
      .filter((r) => r.id !== this.id() && r.status === 'Completed')
      .map((r) => ({
        label: `${this.datePipe.transform(r.createdAt, 'MMM d, HH:mm')}${r.notes ? ' · ' + r.notes : ''} (${r.score}%)`,
        value: r.id,
      })),
  );

  protected compareWith(otherId: string | null): void {
    if (otherId) {
      void this.router.navigate(['/evaluation/runs', this.id(), 'compare', otherId]);
    }
  }

  /** Retrieval is checked only for questions that name their document. */
  protected readonly retrieval = computed(() => {
    const checked = this.results.value().filter((r) => r.expectedDocumentId !== null);
    return { hits: checked.filter((r) => r.retrievalHit).length, checked: checked.length };
  });
  protected readonly wrongCount = computed(
    () => this.results.value().filter((r) => r.grade === 'Wrong').length,
  );
}
