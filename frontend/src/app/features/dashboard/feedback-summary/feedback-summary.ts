import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FeedbackSummary } from '@core/models/analytics.models';
import { ButtonModule } from 'primeng/button';

/** How people rated the answers, and the badly rated ones to review. */
@Component({
  selector: 'app-feedback-summary',
  imports: [DatePipe, ButtonModule],
  templateUrl: './feedback-summary.html',
  styleUrl: './feedback-summary.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeedbackSummaryCard {
  readonly summary = input.required<FeedbackSummary>();
  /** Owners and admins may open the chats. */
  readonly canView = input(false);
  /** The chat to open (its id). */
  readonly view = output<string>();

  /** Share of good ratings, or null without ratings. */
  protected readonly goodPercent = computed(() => {
    const { positive, negative } = this.summary();
    return positive + negative === 0 ? null : Math.round((100 * positive) / (positive + negative));
  });
}
