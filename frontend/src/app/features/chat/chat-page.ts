import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { CurrentBot } from '@core/bots/current-bot';
import { CONVERSATIONS_PAGE_SIZE, ChatApi } from '@core/chat/chat-api';
import { ChatStream, ChatStreamError } from '@core/chat/chat-stream';
import { toCitation } from '@core/chat/citation';
import { conversationTitle } from '@core/chat/conversation-title';
import {
  ChatStreamEvent,
  Citation,
  Conversation,
  ConversationMessage,
  ConversationSummary,
  MessageFeedback,
} from '@core/models/chat.models';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { InputTextModule } from 'primeng/inputtext';
import { SkeletonModule } from 'primeng/skeleton';
import {
  EMPTY,
  Subject,
  catchError,
  distinctUntilChanged,
  finalize,
  map,
  merge,
  of,
  switchMap,
  takeUntil,
  tap,
} from 'rxjs';

import { ConversationList } from './conversation-list/conversation-list';
import { MessageComposer } from './message-composer/message-composer';
import {
  AnswerRating,
  CitationPick,
  LiveAnswer,
  MessageThread,
} from './message-thread/message-thread';
import { SourcesPanel } from './sources-panel/sources-panel';

/**
 * Chat with the current bot: the user's chats on the left, the open chat in the middle, the sources of an answer
 * on the right. On smaller screens the side panels open over the chat. The open chat is in the URL
 * (/chat/{id}), so it survives a reload; opening a chat of another bot switches to that bot.
 */
@Component({
  selector: 'app-chat-page',
  imports: [
    RouterLink,
    EmptyState,
    ButtonModule,
    ConfirmDialogModule,
    InputTextModule,
    SkeletonModule,
    ConversationList,
    MessageComposer,
    MessageThread,
    SourcesPanel,
  ],
  providers: [ConfirmationService],
  templateUrl: './chat-page.html',
  styleUrl: './chat-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatPage {
  private readonly chatApi = inject(ChatApi);
  private readonly chatStream = inject(ChatStream);
  private readonly router = inject(Router);
  private readonly confirmation = inject(ConfirmationService);
  private readonly injector = inject(Injector);
  protected readonly currentBot = inject(CurrentBot);

  /** From the route: /chat/:conversationId (absent on /chat). */
  readonly conversationId = input<string>();

  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly totalConversations = signal(0);
  protected readonly listState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly loadingMore = signal(false);
  protected readonly hasMore = computed(
    () => this.conversations().length < this.totalConversations(),
  );

  protected readonly conversation = signal<Conversation | null>(null);
  protected readonly chatState = signal<'none' | 'loading' | 'ready' | 'not-found' | 'error'>(
    'none',
  );

  /** Smaller screens: which side panel is open over the chat. */
  protected readonly listOpen = signal(false);
  protected readonly sourcesOpen = signal(false);
  /** The sources of the answer whose citation chip was chosen last, and the number of the chosen one. */
  private readonly pickedSources = signal<Citation[] | null>(null);
  protected readonly selectedSource = signal<number | null>(null);
  /** The sources panel shows the chosen answer's sources, otherwise those of the chat's latest answer. */
  protected readonly sources = computed(
    () =>
      this.pickedSources() ??
      this.conversation()
        ?.messages.filter((m) => m.role === 'Assistant')
        .at(-1)?.citations ??
      [],
  );
  /** Where focus goes back to when the sources panel over the chat is closed (the chosen chip). */
  private focusBeforeSources: HTMLElement | null = null;

  protected readonly skeletons = [1, 2, 3];

  /** The open chat's name is being edited. */
  protected readonly renaming = signal(false);
  private readonly titleInput = viewChild<ElementRef<HTMLInputElement>>('titleInput');

  /** The question being answered (until its answer is saved and joins the chat's messages). */
  protected readonly live = signal<LiveAnswer | null>(null);
  /** An answer is being written: the composer waits until it is done. */
  protected readonly sending = signal(false);
  /** For screen readers: said once an answer is complete (its words are not read out one by one). */
  protected readonly announcement = signal('');

  /** Questions to answer in the open chat (or a new one when none is open). */
  private readonly questions = new Subject<string>();
  /** Stops the answer being written (another chat or bot was opened). */
  private readonly stopAnswer = new Subject<void>();
  /** The bot of the question being answered. */
  private readonly answeringBotId = signal<string | null>(null);

  /** A chat of another bot was opened: switch to that bot once the bot list is loaded. */
  private readonly pendingBotId = signal<string | null>(null);
  private readonly retryList = new Subject<void>();
  private readonly retryChat = new Subject<void>();
  private readonly more = new Subject<void>();

  constructor() {
    const botId = computed(() => this.currentBot.selectedId());

    // The chat list follows the current bot (a newer load cancels an older one).
    merge(toObservable(botId).pipe(distinctUntilChanged()), this.retryList.pipe(map(() => botId())))
      .pipe(
        switchMap((id) => {
          this.conversations.set([]);
          this.totalConversations.set(0);
          if (!id) {
            return EMPTY;
          }
          this.listState.set('loading');
          return this.chatApi.conversations(id).pipe(
            tap((page) => {
              this.conversations.set(page.items);
              this.totalConversations.set(page.totalCount);
              this.listState.set('ready');
            }),
            catchError(() => {
              this.listState.set('error');
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();

    // Older chats, a page at a time.
    this.more
      .pipe(
        switchMap(() => {
          const id = botId();
          if (!id || this.loadingMore()) {
            return EMPTY;
          }
          this.loadingMore.set(true);
          const page = Math.floor(this.conversations().length / CONVERSATIONS_PAGE_SIZE) + 1;
          return this.chatApi.conversations(id, page).pipe(
            tap((result) => {
              // Skip any chat already shown (the list can shift while paging).
              const shown = new Set(this.conversations().map((c) => c.id));
              this.conversations.update((list) => [
                ...list,
                ...result.items.filter((c) => !shown.has(c.id)),
              ]);
              this.totalConversations.set(result.totalCount);
              this.loadingMore.set(false);
            }),
            catchError(() => {
              this.loadingMore.set(false);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();

    // The open chat follows the URL.
    merge(
      toObservable(this.conversationId).pipe(distinctUntilChanged()),
      this.retryChat.pipe(map(() => this.conversationId())),
    )
      .pipe(
        switchMap((id) => {
          // A new chat got its address when its first question was saved: it is already open.
          if (id && id === this.conversation()?.id) {
            return EMPTY;
          }
          this.stopAnswering();
          this.conversation.set(null);
          this.forgetChosenSource();
          if (!id) {
            this.chatState.set('none');
            return EMPTY;
          }
          this.chatState.set('loading');
          return this.chatApi.conversation(id).pipe(
            tap((chat) => {
              this.conversation.set(chat);
              this.chatState.set('ready');
              if (chat.botId !== botId()) {
                this.pendingBotId.set(chat.botId);
              }
            }),
            catchError((error: unknown) => {
              this.chatState.set(
                error instanceof HttpErrorResponse && error.status === 404 ? 'not-found' : 'error',
              );
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();

    // Answers stream in as they are written; one at a time (the composer waits).
    this.questions
      .pipe(
        switchMap((question) => {
          const botId = this.currentBot.selectedId();
          if (!botId) {
            return EMPTY;
          }
          // The panel follows the latest answer again.
          this.forgetChosenSource();
          this.live.set({ question, answer: '', sources: [], status: 'answering', error: null });
          this.answeringBotId.set(botId);
          this.sending.set(true);
          this.announcement.set('');
          return this.chatStream.ask(botId, question, this.conversation()?.id).pipe(
            tap((event) => this.onAnswerEvent(event, question, botId)),
            catchError((error: unknown) => {
              this.live.update(
                (live) =>
                  live && {
                    ...live,
                    status: 'failed',
                    error:
                      error instanceof ChatStreamError
                        ? error.message
                        : 'Something went wrong. Please try again.',
                  },
              );
              this.announcement.set('The answer failed.');
              return EMPTY;
            }),
            takeUntil(this.stopAnswer),
            finalize(() => {
              this.sending.set(false);
              this.answeringBotId.set(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();

    // A question for one bot is not answered after another bot was chosen.
    effect(() => {
      const answering = this.answeringBotId();
      const selected = this.currentBot.selectedId();
      if (answering && answering !== selected) {
        untracked(() => this.stopAnswering());
      }
    });

    // Keep the open chat and the current bot together: a chat of another bot (e.g. a link) switches to its bot;
    // choosing another bot in the top bar leaves the open chat.
    effect(() => {
      const status = this.currentBot.status();
      const selected = this.currentBot.selectedId();
      const pending = this.pendingBotId();
      const chat = this.conversation();
      untracked(() => {
        if (pending) {
          if (status === 'ready') {
            this.pendingBotId.set(null);
            this.currentBot.select(pending); // a deleted bot is not selected: the chat is then left below
          }
          return;
        }
        if (chat && selected && chat.botId !== selected) {
          void this.router.navigate(['/chat']);
        }
      });
    });
  }

  protected ask(question: string): void {
    this.questions.next(question);
  }

  protected startRename(): void {
    this.renaming.set(true);
    afterNextRender(() => this.titleInput()?.nativeElement.select(), { injector: this.injector });
  }

  /** Saves a changed name (Enter or leaving the box); an empty or unchanged one changes nothing. */
  protected saveTitle(value: string): void {
    const chat = this.conversation();
    if (!this.renaming() || !chat) {
      return;
    }
    this.renaming.set(false);
    const title = value.trim();
    if (!title || title === chat.title) {
      return;
    }
    this.chatApi.rename(chat.id, title).subscribe(() => {
      this.conversation.update((c) => c && { ...c, title });
      this.conversations.update((list) =>
        list.map((c) => (c.id === chat.id ? { ...c, title } : c)),
      );
    });
  }

  protected confirmDelete(): void {
    const chat = this.conversation();
    if (!chat) {
      return;
    }
    this.confirmation.confirm({
      header: 'Delete chat?',
      message: `"${chat.title}" and all its messages will be removed. This cannot be undone.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger', icon: 'pi pi-trash' },
      rejectButtonProps: { severity: 'secondary', text: true },
      defaultFocus: 'reject',
      accept: () =>
        this.chatApi.delete(chat.id).subscribe(() => {
          this.conversations.update((list) => list.filter((c) => c.id !== chat.id));
          this.totalConversations.update((total) => total - 1);
          void this.router.navigate(['/chat']);
        }),
    });
  }

  /**
   * Stops the answer being written: the request is cancelled, so the server stops the model and saves no part
   * of the answer (the question stays saved). The words so far stay on screen, marked as stopped.
   */
  protected stopGenerating(): void {
    if (!this.sending()) {
      return;
    }
    this.stopAnswer.next();
    this.live.update((live) => live && { ...live, status: 'stopped' });
    this.announcement.set('Answer stopped.');
  }

  /** Asks the question whose answer failed or was stopped once more. */
  protected retryAnswer(): void {
    const live = this.live();
    if (!live) {
      return;
    }

    // A question the server already saved is shown again with the new answer (as it is saved again).
    const question = live.question ?? this.lastQuestion();
    if (question) {
      this.ask(question);
    }
  }

  /** Shows the rating at once; if saving it fails, the old one comes back (the error is shown as a toast). */
  protected rate({ message, feedback }: AnswerRating): void {
    const show = (value: MessageFeedback) =>
      this.conversation.update(
        (chat) =>
          chat && {
            ...chat,
            messages: chat.messages.map((m) =>
              m.id === message.id ? { ...m, feedback: value } : m,
            ),
          },
      );
    show(feedback);
    this.chatApi.rate(message.id, feedback).subscribe({ error: () => show(message.feedback) });
  }

  protected retryLoadingList(): void {
    this.retryList.next();
  }

  protected retryLoadingChat(): void {
    this.retryChat.next();
  }

  protected loadMoreConversations(): void {
    this.more.next();
  }

  protected toggleList(): void {
    this.listOpen.update((open) => !open);
    this.sourcesOpen.set(false);
  }

  protected toggleSources(): void {
    this.sourcesOpen.update((open) => !open);
    this.listOpen.set(false);
  }

  /** A citation chip was chosen: its answer's sources are shown, with the chosen one picked out. */
  protected showSource(pick: CitationPick): void {
    // A new list each time, so the panel scrolls to the source again when the same chip is chosen again.
    this.pickedSources.set([...pick.sources]);
    this.selectedSource.set(pick.number);
    this.focusBeforeSources =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.sourcesOpen.set(true);
    this.listOpen.set(false);
  }

  protected closePanels(): void {
    this.listOpen.set(false);
    if (this.sourcesOpen()) {
      this.closeSources();
    }
  }

  /** Closes the sources panel over the chat; focus goes back to the chip it was opened from. */
  protected closeSources(): void {
    this.sourcesOpen.set(false);
    if (this.focusBeforeSources?.isConnected) {
      this.focusBeforeSources.focus();
    }
    this.focusBeforeSources = null;
  }

  private onAnswerEvent(event: ChatStreamEvent, question: string, botId: string): void {
    const now = new Date().toISOString();
    switch (event.type) {
      case 'start': {
        // The question is saved: it joins the chat (a new chat is created and gets its address).
        const isNew = !this.conversation();
        const asked: ConversationMessage = {
          id: event.questionId,
          role: 'User',
          content: question,
          createdAt: now,
          wasAnswered: true,
          feedback: 'None',
          citations: [],
        };
        this.conversation.update((chat) =>
          chat
            ? { ...chat, updatedAt: now, messages: [...chat.messages, asked] }
            : {
                id: event.conversationId,
                botId,
                title: conversationTitle(question),
                createdAt: now,
                updatedAt: now,
                messages: [asked],
              },
        );
        this.chatState.set('ready');
        this.live.update((live) => live && { ...live, question: null });
        this.showFirstInList();
        if (isNew) {
          void this.router.navigate(['/chat', event.conversationId]);
        }
        break;
      }
      case 'citations':
        this.live.update((live) => live && { ...live, sources: event.sources });
        break;
      case 'token':
        this.live.update((live) => live && { ...live, answer: live.answer + event.text });
        break;
      case 'done': {
        // The saved answer replaces the streamed words (it may differ slightly, e.g. a "not found" answer).
        const sources = this.live()?.sources ?? [];
        const answer: ConversationMessage = {
          id: event.messageId,
          role: 'Assistant',
          content: event.content,
          createdAt: now,
          wasAnswered: event.wasAnswered,
          feedback: 'None',
          citations: sources
            .filter((source) => event.citedSources.includes(source.number))
            .map(toCitation),
        };
        this.conversation.update(
          (chat) => chat && { ...chat, updatedAt: now, messages: [...chat.messages, answer] },
        );
        this.live.set(null);
        this.showFirstInList();
        this.announcement.set('Answer ready.');
        break;
      }
    }
  }

  /** The open chat moves to the top of the list (newest activity first); a new chat is added there. */
  private showFirstInList(): void {
    const chat = this.conversation();
    if (!chat) {
      return;
    }

    const shown = this.conversations().find((c) => c.id === chat.id);
    const updated: ConversationSummary = {
      id: chat.id,
      botId: chat.botId,
      title: shown?.title ?? chat.title,
      messageCount: chat.messages.length,
      createdAt: shown?.createdAt ?? chat.createdAt,
      updatedAt: chat.updatedAt,
    };
    this.conversations.update((list) => [updated, ...list.filter((c) => c.id !== chat.id)]);
    if (!shown) {
      this.totalConversations.update((total) => total + 1);
    }
  }

  private lastQuestion(): string | null {
    const questions = (this.conversation()?.messages ?? []).filter((m) => m.role === 'User');
    return questions.at(-1)?.content ?? null;
  }

  private forgetChosenSource(): void {
    this.pickedSources.set(null);
    this.selectedSource.set(null);
    this.focusBeforeSources = null;
  }

  private stopAnswering(): void {
    this.stopAnswer.next();
    this.live.set(null);
  }
}
