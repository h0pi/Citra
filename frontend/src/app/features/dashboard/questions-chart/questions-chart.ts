import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DayCount } from '@core/models/analytics.models';

/** Questions per day as a bar chart (plain CSS bars; the screen-reader text lists every day). */
@Component({
  selector: 'app-questions-chart',
  imports: [DatePipe],
  templateUrl: './questions-chart.html',
  styleUrl: './questions-chart.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionsChart {
  readonly days = input.required<DayCount[]>();

  protected readonly max = computed(() => Math.max(1, ...this.days().map((d) => d.questions)));
  protected readonly total = computed(() => this.days().reduce((sum, d) => sum + d.questions, 0));
}
