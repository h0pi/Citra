import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { toCitation } from '@core/chat/citation';
import {
  AnswerSource,
  Citation,
  ConversationMessage,
  MessageFeedback,
} from '@core/models/chat.models';
import { NotificationService } from '@core/services/notification.service';
import { ButtonModule } from 'primeng/button';
import { MarkdownView } from '../markdown/markdown-view';

/** The question being answered right now, shown after the chat's messages until its answer is saved. */
export interface LiveAnswer {
  /** The question while it is not saved yet; null once the server saved it (it is then one of the messages). */
  question: string | null;
  /** The answer so far. */
  answer: string;
  /** The passages the answer is written from. */
  sources: AnswerSource[];
  /**
   * Being answered (searching until the first words arrive), failed (with the reason), or stopped by the user
   * (the server keeps no part of an answer, only the question).
   */
  status: 'answering' | 'failed' | 'stopped';
  error: string | null;
}

/** An answer was rated (None: the rating was taken back). */
export interface AnswerRating {
  message: ConversationMessage;
  feedback: MessageFeedback;
}

/** A citation chip was chosen: the sources of its answer, and the number of the one to show. */
export interface CitationPick {
  sources: Citation[];
  number: number;
}

/**
 * A chat's messages, oldest first: questions on the right (as typed), answers on the left (Markdown), then the
 * answer being written.
 */
@Component({
  selector: 'app-message-thread',
  templateUrl: './message-thread.html',
  styleUrl: './message-thread.scss',
  imports: [ButtonModule, MarkdownView],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MessageThread {
  readonly messages = input<ConversationMessage[]>([]);
  readonly live = input<LiveAnswer | null>(null);
  /** Name shown above answers. */
  readonly botName = input('Bot');

  /** Ask the failed question again. */
  readonly retry = output<void>();
  /** Show the source of a citation chip. */
  readonly cite = output<CitationPick>();
  readonly rate = output<AnswerRating>();

  private readonly notifications = inject(NotificationService);

  /** Copies the answer's text (as written, with its Markdown). */
  protected copy(message: ConversationMessage): void {
    navigator.clipboard.writeText(message.content).then(
      () => this.notifications.success('Answer copied'),
      () => this.notifications.error("Couldn't copy the answer"),
    );
  }

  /** The chosen rating, or none when it is chosen again. */
  protected rateAnswer(message: ConversationMessage, feedback: MessageFeedback): void {
    this.rate.emit({ message, feedback: message.feedback === feedback ? 'None' : feedback });
  }

  protected citeSaved(message: ConversationMessage, number: number): void {
    this.cite.emit({ sources: message.citations, number });
  }

  protected citeLive(live: LiveAnswer, number: number): void {
    this.cite.emit({ sources: live.sources.map(toCitation), number });
  }
}
