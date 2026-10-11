import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  output,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { AnalyticsApi } from '@core/analytics/analytics-api';
import { ConversationChannel } from '@core/models/analytics.models';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { PaginatorModule, PaginatorState } from 'primeng/paginator';
import { SelectModule } from 'primeng/select';

export const CHANNEL_OPTIONS = [
  { label: 'All chats', value: null },
  { label: 'Admin app', value: 'Admin' },
  { label: 'Website', value: 'Widget' },
];

export const CONVERSATIONS_PAGE_SIZE = 20;

/** All chats of the company (owners and admins), newest activity first, to open read-only. */
@Component({
  selector: 'app-conversations-browser',
  imports: [DatePipe, FormsModule, ButtonModule, MessageModule, PaginatorModule, SelectModule],
  templateUrl: './conversations-browser.html',
  styleUrl: './conversations-browser.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConversationsBrowser {
  private readonly analyticsApi = inject(AnalyticsApi);

  /** null = all bots. */
  readonly botId = input<string | null>(null);
  /** The chat to open (its id). */
  readonly view = output<string>();

  protected readonly channelOptions = CHANNEL_OPTIONS;
  protected readonly pageSize = CONVERSATIONS_PAGE_SIZE;
  protected readonly channel = signal<ConversationChannel | null>(null);
  /** Back to the first page when the bot or the channel changes. */
  protected readonly page = linkedSignal({
    source: () => [this.botId(), this.channel()],
    computation: () => 1,
  });

  protected readonly conversations = rxResource({
    params: () => ({
      botId: this.botId(),
      channel: this.channel(),
      page: this.page(),
      pageSize: this.pageSize,
    }),
    stream: ({ params }) => this.analyticsApi.conversations(params),
  });
  protected readonly totalCount = computed(() =>
    this.conversations.hasValue() ? this.conversations.value().totalCount : 0,
  );

  protected onPage(event: PaginatorState): void {
    this.page.set((event.page ?? 0) + 1);
  }
}
