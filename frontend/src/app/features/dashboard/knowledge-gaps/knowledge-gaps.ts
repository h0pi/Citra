import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { UnansweredQuestion } from '@core/models/analytics.models';
import { ButtonModule } from 'primeng/button';

/** Questions the documents could not answer: they show which documents are missing. */
@Component({
  selector: 'app-knowledge-gaps',
  imports: [DatePipe, ButtonModule],
  templateUrl: './knowledge-gaps.html',
  styleUrl: './knowledge-gaps.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KnowledgeGaps {
  readonly gaps = input.required<UnansweredQuestion[]>();
  /** Owners and admins may open the chats. */
  readonly canView = input(false);
  /** The chat to open (its id). */
  readonly view = output<string>();
}
