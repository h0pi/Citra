import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConversationSummary } from '@core/models/chat.models';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';

/** The user's chats with the current bot, most recently active first; each links to /chat/{id}. */
@Component({
  selector: 'app-conversation-list',
  imports: [DatePipe, RouterLink, ButtonModule, SkeletonModule],
  templateUrl: './conversation-list.html',
  styleUrl: './conversation-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConversationList {
  readonly conversations = input<ConversationSummary[]>([]);
  readonly selectedId = input<string | null>(null);
  readonly state = input<'loading' | 'ready' | 'error'>('loading');
  /** More chats exist than are shown. */
  readonly hasMore = input(false);
  readonly loadingMore = input(false);

  readonly retry = output<void>();
  readonly loadMore = output<void>();
  /** A chat was chosen (lets the page close the list on small screens). */
  readonly picked = output<void>();

  protected readonly skeletons = [1, 2, 3, 4];
}
