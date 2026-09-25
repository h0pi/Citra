import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';

/** Placeholder — replaced by the real page later. */
@Component({
  selector: 'app-evaluation-page',
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header title="Evaluation" subtitle="Measure answer accuracy with test questions." />
    <app-empty-state
      icon="pi-verified"
      title="No test sets yet"
      message="Create a set of questions with expected answers to score your bot."
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EvaluationPage {}
