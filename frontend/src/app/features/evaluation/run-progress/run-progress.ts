import { CurrencyPipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EvaluationApi } from '@core/evaluation/evaluation-api';
import { EvalRun } from '@core/models/evaluation.models';
import { MessageModule } from 'primeng/message';
import { ProgressBarModule } from 'primeng/progressbar';
import { switchMap, takeWhile, timer } from 'rxjs';

/** How often a running eval is checked. */
export const RUN_POLL_MS = 2000;

/** A run while the Worker answers its questions (progress bar), then its score, or why it failed. */
@Component({
  selector: 'app-run-progress',
  imports: [CurrencyPipe, DecimalPipe, RouterLink, MessageModule, ProgressBarModule],
  templateUrl: './run-progress.html',
  styleUrl: './run-progress.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RunProgress {
  private readonly evaluationApi = inject(EvaluationApi);

  /** The run as it was started. */
  readonly run = input.required<EvalRun>();
  /** The run is completed or failed. */
  readonly finished = output<EvalRun>();

  /** The latest state: checked every few seconds until the run is completed or failed. */
  protected readonly current = toSignal(
    toObservable(this.run).pipe(
      switchMap((started) =>
        timer(RUN_POLL_MS, RUN_POLL_MS).pipe(
          switchMap(() => this.evaluationApi.run(started.id)),
          takeWhile((run) => run.status === 'Pending' || run.status === 'Running', true),
        ),
      ),
    ),
  );
  /** The latest check of this run; a newer run starts from its own state, not the previous run's last one. */
  protected readonly state = computed(() => {
    const checked = this.current();
    return checked?.id === this.run().id ? checked : this.run();
  });
  protected readonly percent = computed(() => {
    const run = this.state();
    return Math.round((100 * run.answeredQuestions) / run.totalQuestions);
  });

  constructor() {
    effect(() => {
      const run = this.state();
      if (run.status === 'Completed' || run.status === 'Failed') {
        this.finished.emit(run);
      }
    });
  }
}
