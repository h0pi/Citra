import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { AnalyticsApi } from '@core/analytics/analytics-api';
import { DialogModule } from 'primeng/dialog';

/** A chat of the company, read-only: questions, answers and the sources they cite. */
@Component({
  selector: 'app-transcript-dialog',
  imports: [DialogModule],
  templateUrl: './transcript-dialog.html',
  styleUrl: './transcript-dialog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TranscriptDialog {
  private readonly analyticsApi = inject(AnalyticsApi);

  /** The chat to show; null = closed. */
  readonly conversationId = input<string | null>(null);
  readonly closed = output();

  protected readonly conversation = rxResource({
    params: () => this.conversationId() ?? undefined,
    stream: ({ params }) => this.analyticsApi.conversation(params),
  });
}
