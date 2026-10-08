import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { CurrentBot } from '@core/bots/current-bot';
import { ChatApi } from '@core/chat/chat-api';
import { ChatStream, ChatStreamError } from '@core/chat/chat-stream';
import { BotSummary } from '@core/models/bot.models';
import {
  AnswerSource,
  ChatStreamEvent,
  Citation,
  Conversation,
  ConversationMessage,
  ConversationSummary,
} from '@core/models/chat.models';
import { PagedResult } from '@core/models/document.models';
import { ConfirmationService, MessageService } from 'primeng/api';
import { Observable, Subject, of, throwError } from 'rxjs';

import { NotificationService } from '@core/services/notification.service';

import { ChatPage } from './chat-page';
import { MessageComposer } from './message-composer/message-composer';
import { SourcesPanel } from './sources-panel/sources-panel';

@Component({ template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class RouteStub {}

function bot(id: string, name: string): BotSummary {
  return {
    id,
    name,
    slug: name.toLowerCase(),
    description: null,
    isPublic: false,
    widgetColor: '#6366F1',
    starterQuestions: [],
    documentCount: 1,
    messageCount: 0,
    createdAt: '2026-09-26T10:00:00Z',
    updatedAt: '2026-09-26T10:00:00Z',
  };
}

export function summary(id: string, title: string, botId = 'b1'): ConversationSummary {
  return {
    id,
    botId,
    title,
    messageCount: 2,
    createdAt: '2026-10-01T09:00:00Z',
    updatedAt: '2026-10-01T09:05:00Z',
  };
}

export function message(overrides: Partial<ConversationMessage> = {}): ConversationMessage {
  return {
    id: 'm1',
    role: 'User',
    content: 'How many vacation days?',
    createdAt: '2026-10-01T09:00:00Z',
    wasAnswered: true,
    feedback: 'None',
    citations: [],
    ...overrides,
  };
}

function chat(overrides: Partial<Conversation> = {}): Conversation {
  return {
    id: 'c1',
    botId: 'b1',
    title: 'How many vacation days?',
    createdAt: '2026-10-01T09:00:00Z',
    updatedAt: '2026-10-01T09:05:00Z',
    messages: [
      message(),
      message({ id: 'm2', role: 'Assistant', content: 'You get 25 days [1].' }),
    ],
    ...overrides,
  };
}

function citation(number: number, overrides: Partial<Citation> = {}): Citation {
  return {
    number,
    chunkId: `k${number}`,
    documentId: 'd1',
    documentName: 'Handbook.pdf',
    pageNumber: number,
    snippet: `Passage ${number}`,
    score: 0.03,
    ...overrides,
  };
}

function answerSource(number: number, content = `Passage ${number}`): AnswerSource {
  return {
    number,
    chunkId: `k${number}`,
    documentId: 'd1',
    documentName: 'Handbook.pdf',
    pageNumber: number,
    heading: null,
    content,
    charStart: 0,
    charEnd: content.length,
    score: 0.03,
  };
}

function page(
  items: ConversationSummary[],
  totalCount = items.length,
): PagedResult<ConversationSummary> {
  return { items, page: 1, pageSize: 30, totalCount, totalPages: 1 };
}

describe('ChatPage', () => {
  const conversations =
    vi.fn<
      (
        botId: string,
        page?: number,
        pageSize?: number,
      ) => Observable<PagedResult<ConversationSummary>>
    >();
  const conversation = vi.fn<(id: string) => Observable<Conversation>>();
  const rate = vi.fn<(id: string, feedback: string) => Observable<void>>();
  const rename = vi.fn<(id: string, title: string) => Observable<void>>();
  const remove = vi.fn<(id: string) => Observable<void>>();
  /** Each question's answer stream, in the order asked (the test plays the server). */
  let answers: Subject<ChatStreamEvent>[] = [];
  const ask =
    vi.fn<
      (
        botId: string,
        question: string,
        conversationId?: string | null,
      ) => Observable<ChatStreamEvent>
    >();
  const bots = signal<BotSummary[]>([bot('b1', 'Support'), bot('b2', 'HR')]);
  const selectedId = signal<string | null>('b1');
  const status = signal<'idle' | 'loading' | 'ready' | 'error'>('ready');
  const refresh = vi.fn();
  const select = vi.fn((id: string) => {
    if (bots().some((b) => b.id === id)) selectedId.set(id);
  });

  beforeEach(async () => {
    // jsdom does not scroll; the sources panel scrolls the chosen source into view.
    Element.prototype.scrollIntoView = vi.fn();
    conversations
      .mockReset()
      .mockReturnValue(
        of(page([summary('c1', 'How many vacation days?'), summary('c2', 'Parking?')])),
      );
    conversation.mockReset().mockReturnValue(of(chat()));
    answers = [];
    ask.mockReset().mockImplementation(() => {
      const answer = new Subject<ChatStreamEvent>();
      answers.push(answer);
      return answer;
    });
    bots.set([bot('b1', 'Support'), bot('b2', 'HR')]);
    selectedId.set('b1');
    status.set('ready');
    refresh.mockReset();
    select.mockClear();
    await TestBed.configureTestingModule({
      imports: [ChatPage],
      providers: [
        provideRouter([
          { path: 'chat', component: RouteStub },
          { path: 'chat/:conversationId', component: RouteStub },
        ]),
        MessageService,
        {
          provide: ChatApi,
          useValue: { conversations, conversation, rate, rename, delete: remove },
        },
        { provide: ChatStream, useValue: { ask } },
        {
          provide: CurrentBot,
          useValue: {
            bots,
            selectedId,
            status,
            bot: computed(() => bots().find((b) => b.id === selectedId()) ?? null),
            refresh,
            select,
          },
        },
      ],
    }).compileComponents();
  });

  async function render(conversationId?: string) {
    const fixture = TestBed.createComponent(ChatPage);
    if (conversationId) {
      fixture.componentRef.setInput('conversationId', conversationId);
    }
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const text = () => el.textContent!.replace(/\s+/g, ' ');
    const chatLinks = () => [...el.querySelectorAll<HTMLAnchorElement>('a.conversation')];
    const button = (label: string) =>
      [...el.querySelectorAll<HTMLButtonElement>('p-button button')].find(
        (b) => b.textContent?.includes(label) || b.getAttribute('aria-label') === label,
      );
    const layout = () => el.querySelector('.chat-layout')!;
    /** Types a question and presses Enter. */
    const send = async (question: string) => {
      const box = el.querySelector('textarea')!;
      box.value = question;
      box.dispatchEvent(new Event('input'));
      box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      await fixture.whenStable();
    };
    /** The server sends the next events of the latest answer (like the real stream, it ends after "done"). */
    const stream = async (...events: ChatStreamEvent[]) => {
      events.forEach((event) => {
        answers.at(-1)!.next(event);
        if (event.type === 'done') {
          answers.at(-1)!.complete();
        }
      });
      await fixture.whenStable();
    };
    const fail = async (message: string) => {
      answers.at(-1)!.error(new ChatStreamError(message, null));
      await fixture.whenStable();
    };
    const bubbles = () =>
      [...el.querySelectorAll('.message')].map(
        (m) =>
          `${m.classList.contains('question') ? 'Q' : 'A'}: ${m.querySelector('.bubble')?.textContent ?? ''}`,
      );
    const live = () => el.querySelector('.message.live');
    const announced = () => el.querySelector('[aria-live="polite"]')!.textContent!.trim();
    const chips = () => [...el.querySelectorAll<HTMLButtonElement>('button.citation')];
    /** What the sources panel was given to show. */
    const panel = () => {
      const sources = fixture.debugElement.query((d) => d.componentInstance instanceof SourcesPanel)
        .componentInstance as SourcesPanel;
      return { sources: sources.sources(), selected: sources.selected() };
    };
    return {
      chips,
      panel,
      fixture,
      el,
      text,
      chatLinks,
      button,
      layout,
      send,
      stream,
      fail,
      bubbles,
      live,
      announced,
    };
  }

  function started(conversationId = 'new1', questionId = 'q1'): ChatStreamEvent {
    return { type: 'start', conversationId, questionId, searchQuery: 'search' };
  }

  function done(content: string, citedSources: number[] = [], messageId = 'a1'): ChatStreamEvent {
    return { type: 'done', messageId, content, wasAnswered: true, citedSources };
  }

  // ---------- no bot ----------

  it('asks to create a bot when there is none', async () => {
    bots.set([]);
    selectedId.set(null);
    const { el, text } = await render();

    expect(text()).toContain('Create a bot first');
    expect(el.querySelector('.chat-layout')).toBeNull();
    expect(conversations).not.toHaveBeenCalled();
  });

  it('offers a retry when the bots cannot be loaded', async () => {
    status.set('error');
    const { fixture, text, button } = await render();

    expect(text()).toContain("Couldn't load your bots");
    button('Try again')!.click();
    await fixture.whenStable();
    expect(refresh).toHaveBeenCalled();
  });

  // ---------- the chat list ----------

  it('lists the current bot’s chats, each linking to its page', async () => {
    const { chatLinks } = await render();

    expect(conversations).toHaveBeenCalledWith('b1');
    expect(chatLinks().map((a) => a.querySelector('.title')!.textContent)).toEqual([
      'How many vacation days?',
      'Parking?',
    ]);
    expect(chatLinks()[1].getAttribute('href')).toBe('/chat/c2');
    expect(chatLinks()[0].getAttribute('aria-current')).toBeNull();
  });

  it('marks the open chat in the list', async () => {
    const { chatLinks } = await render('c2');

    expect(chatLinks()[1].getAttribute('aria-current')).toBe('page');
    expect(chatLinks()[1].classList).toContain('active');
    expect(chatLinks()[0].getAttribute('aria-current')).toBeNull();
  });

  it('says so when there are no chats yet', async () => {
    conversations.mockReturnValue(of(page([])));
    const { text } = await render();

    expect(text()).toContain('No chats yet');
  });

  it('offers a retry when the chats cannot be loaded', async () => {
    conversations.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { fixture, text, button, chatLinks } = await render();

    expect(text()).toContain('The chats could not be loaded.');
    button('Try again')!.click();
    await fixture.whenStable();

    expect(conversations).toHaveBeenCalledTimes(2);
    expect(chatLinks()).toHaveLength(2);
  });

  it('shows the new bot’s chats when another bot is chosen, ignoring a late answer for the old one', async () => {
    const slow = new Subject<PagedResult<ConversationSummary>>();
    conversations
      .mockReturnValueOnce(slow)
      .mockReturnValueOnce(of(page([summary('h1', 'HR question', 'b2')])));
    const { fixture, chatLinks } = await render();

    selectedId.set('b2');
    await fixture.whenStable();
    slow.next(page([summary('c1', 'Old bot question')]));
    await fixture.whenStable();

    expect(conversations).toHaveBeenLastCalledWith('b2');
    expect(chatLinks().map((a) => a.querySelector('.title')!.textContent)).toEqual(['HR question']);
  });

  it('loads older chats on request, without showing one twice', async () => {
    conversations
      .mockReturnValueOnce(of(page([summary('c1', 'One'), summary('c2', 'Two')], 3)))
      .mockReturnValueOnce(
        of({ ...page([summary('c2', 'Two'), summary('c3', 'Three')], 3), page: 2 }),
      );
    const { fixture, button, chatLinks } = await render();

    button('Show older chats')!.click();
    await fixture.whenStable();

    expect(conversations).toHaveBeenLastCalledWith('b1', 1);
    expect(chatLinks().map((a) => a.querySelector('.title')!.textContent)).toEqual([
      'One',
      'Two',
      'Three',
    ]);
    expect(button('Show older chats')).toBeUndefined();
  });

  it('keeps offering older chats when new ones appeared meanwhile', async () => {
    conversations
      .mockReturnValueOnce(of(page([summary('c1', 'One'), summary('c2', 'Two')], 3)))
      .mockReturnValueOnce(of({ ...page([summary('c3', 'Three')], 4), page: 2 }));
    const { fixture, button } = await render();

    button('Show older chats')!.click();
    await fixture.whenStable();

    expect(button('Show older chats')).toBeDefined();
  });

  // ---------- the open chat ----------

  it('welcomes the user when no chat is open', async () => {
    const { el, text } = await render();

    expect(text()).toContain('Ask Support anything');
    expect(el.querySelector('h1')!.textContent).toContain('Chat with Support');
    expect(conversation).not.toHaveBeenCalled();
  });

  it('opens the chat from the address with its messages', async () => {
    const { el } = await render('c1');

    expect(conversation).toHaveBeenCalledWith('c1');
    expect(el.querySelector('h1')!.textContent).toContain('How many vacation days?');
    const messages = [...el.querySelectorAll('li.message')];
    expect(messages.map((m) => m.querySelector('.bubble')!.textContent)).toEqual([
      'How many vacation days?',
      'You get 25 days [1].',
    ]);
    expect(messages[0].classList).toContain('question');
    expect(messages[1].classList).toContain('answer');
    expect(messages[1].querySelector('.author')!.textContent).toContain('Support');
  });

  it('formats answers, saved and while written, and shows questions exactly as typed', async () => {
    conversation.mockReturnValue(
      of(
        chat({
          messages: [
            message({ content: 'Is **this** <b>bold</b>?' }),
            message({ id: 'm2', role: 'Assistant', content: 'Yes:\n\n- **one** [1]\n- two' }),
          ],
        }),
      ),
    );
    const { el, send, stream } = await render('c1');

    const [question, answer] = [...el.querySelectorAll('li.message .bubble')];
    expect(question.textContent).toBe('Is **this** <b>bold</b>?');
    expect(question.children).toHaveLength(0);
    expect(answer.querySelector('p')!.textContent).toBe('Yes:');
    expect([...answer.querySelectorAll('li')].map((li) => li.textContent)).toEqual([
      'one [1]',
      'two',
    ]);
    expect(answer.querySelector('li strong')!.textContent).toBe('one');

    await send('And?');
    await stream(started('c1'), { type: 'token', text: '1. **Fir' });
    const live = () => el.querySelector('.message.live .bubble')!;
    expect(live().querySelector('ol li')!.textContent).toBe('**Fir');
    expect(live().querySelector('strong')).toBeNull();
    await stream({ type: 'token', text: 'st**' });
    expect(live().querySelector('ol li strong')!.textContent).toBe('First');
  });

  it('shows placeholders while the chat loads', async () => {
    conversation.mockReturnValue(new Subject<Conversation>());
    const { el } = await render('c1');

    expect(el.querySelector('[aria-label="Loading the chat"]')).not.toBeNull();
  });

  it('says a deleted or unknown chat was not found', async () => {
    conversation.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const { fixture, text, button } = await render('gone');

    expect(text()).toContain('Chat not found');
    button('New chat')!.click();
    await fixture.whenStable();
    expect(String(navigate.mock.calls[0][0])).toBe('/chat');
  });

  it('offers a retry when the chat cannot be loaded', async () => {
    conversation.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
    const { fixture, el, text, button } = await render('c1');

    expect(text()).toContain("Couldn't load this chat");
    button('Try again')!.click();
    await fixture.whenStable();

    expect(conversation).toHaveBeenCalledTimes(2);
    expect(el.querySelectorAll('li.message')).toHaveLength(2);
  });

  it('opens another chat when the address changes, ignoring a late answer for the previous one', async () => {
    const slow = new Subject<Conversation>();
    conversation
      .mockReturnValueOnce(slow)
      .mockReturnValueOnce(of(chat({ id: 'c2', title: 'Parking?' })));
    const { fixture, el } = await render('c1');

    fixture.componentRef.setInput('conversationId', 'c2');
    await fixture.whenStable();
    slow.next(chat({ title: 'Too late' }));
    await fixture.whenStable();

    expect(el.querySelector('h1')!.textContent).toContain('Parking?');
  });

  it('switches to the bot of a chat opened from a link', async () => {
    conversation.mockReturnValue(of(chat({ id: 'h1', botId: 'b2', title: 'HR question' })));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, el } = await render('h1');
    await fixture.whenStable();

    expect(select).toHaveBeenCalledWith('b2');
    expect(selectedId()).toBe('b2');
    expect(el.querySelector('h1')!.textContent).toContain('HR question');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('waits for the bot list before switching to a linked chat’s bot', async () => {
    status.set('loading');
    conversation.mockReturnValue(of(chat({ id: 'h1', botId: 'b2' })));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture } = await render('h1');
    expect(select).not.toHaveBeenCalled();

    status.set('ready');
    await fixture.whenStable();

    expect(select).toHaveBeenCalledWith('b2');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('leaves a linked chat whose bot no longer exists', async () => {
    conversation.mockReturnValue(of(chat({ id: 'x1', botId: 'deleted' })));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture } = await render('x1');
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/chat']);
  });

  it('leaves the open chat when another bot is chosen in the top bar', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture } = await render('c1');
    expect(navigate).not.toHaveBeenCalled();

    selectedId.set('b2');
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/chat']);
  });

  // ---------- the message box ----------

  it('offers the message box under a new chat and under an open chat, addressed to the bot', async () => {
    const welcome = await render();
    const composer = () => welcome.el.querySelector('app-message-composer');
    expect(composer()).not.toBeNull();
    expect(welcome.el.querySelector('textarea')!.placeholder).toBe('Ask Support a question…');

    const open = await render('c1');
    expect(open.el.querySelector('app-message-composer')).not.toBeNull();
  });

  it('hides the message box while a chat loads, or when it was not found or failed', async () => {
    conversation.mockReturnValueOnce(new Subject<Conversation>());
    const loading = await render('c1');
    expect(loading.el.querySelector('app-message-composer')).toBeNull();

    conversation.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 404 })));
    const missing = await render('gone');
    expect(missing.el.querySelector('app-message-composer')).toBeNull();

    conversation.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
    const failed = await render('c1');
    expect(failed.el.querySelector('app-message-composer')).toBeNull();
  });

  // ---------- streaming answers ----------

  it('starts a new chat with the first question and shows the answer as it is written', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { el, send, stream, bubbles, live, text, chatLinks, announced } = await render();

    await send('How many vacation days do I get?');

    expect(ask).toHaveBeenCalledWith('b1', 'How many vacation days do I get?', undefined);
    expect(text()).not.toContain('Ask Support anything');
    expect(bubbles()).toEqual([
      'Q: How many vacation days do I get?',
      'A: Searching the documents…',
    ]);
    expect(live()!.getAttribute('aria-busy')).toBe('true');
    expect(live()!.querySelectorAll('.dots i')).toHaveLength(3); // typing indicator
    expect(el.textContent).toContain('Wait for the answer to finish before sending, or stop it.');

    await stream(started('new1'), {
      type: 'citations',
      sources: [answerSource(1), answerSource(2)],
    });
    expect(navigate).toHaveBeenCalledWith(['/chat', 'new1']);
    expect(el.querySelector('h1')!.textContent).toContain('How many vacation days do I get?');
    expect(chatLinks()[0].textContent).toContain('How many vacation days do I get?');
    expect(chatLinks()).toHaveLength(3);
    expect(bubbles()).toEqual([
      'Q: How many vacation days do I get?',
      'A: Searching the documents…',
    ]);

    await stream({ type: 'token', text: 'You get ' });
    expect(bubbles()).toEqual(['Q: How many vacation days do I get?', 'A: You get ']);
    await stream({ type: 'token', text: '25 days [1].' });
    // The marker is a chip ("1") as soon as its source is known.
    expect(bubbles()).toEqual(['Q: How many vacation days do I get?', 'A: You get 25 days 1.']);
    expect(announced()).toBe('');

    await stream(done('You get 25 days [1].', [1]));
    expect(bubbles()).toEqual(['Q: How many vacation days do I get?', 'A: You get 25 days 1.']);
    expect(live()).toBeNull();
    expect(el.textContent).toContain('Enter to send, Shift+Enter for a new line.');
    expect(announced()).toBe('Answer ready.');
  });

  it('keeps the new chat open when its address arrives, without loading it again', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, send, stream, bubbles } = await render();
    await send('Parking?');
    await stream(started('new1'), { type: 'token', text: 'Level 2' });

    fixture.componentRef.setInput('conversationId', 'new1');
    await fixture.whenStable();
    await stream({ type: 'token', text: ' [1].' }, done('Level 2 [1].'));

    expect(conversation).not.toHaveBeenCalled();
    expect(bubbles()).toEqual(['Q: Parking?', 'A: Level 2 [1].']);
  });

  it('continues an open chat and moves it to the top of the list', async () => {
    conversation.mockReturnValue(of(chat({ id: 'c2', title: 'Parking?' })));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { send, stream, bubbles, chatLinks } = await render('c2');
    expect(chatLinks()[0].textContent).toContain('How many vacation days?');

    await send('And for guests?');
    expect(ask).toHaveBeenCalledWith('b1', 'And for guests?', 'c2');
    await stream(started('c2', 'q9'), { type: 'token', text: 'Level 3.' }, done('Level 3.'));

    expect(navigate).not.toHaveBeenCalled();
    expect(bubbles()).toEqual([
      'Q: How many vacation days?',
      'A: You get 25 days [1].',
      'Q: And for guests?',
      'A: Level 3.',
    ]);
    expect(chatLinks()).toHaveLength(2);
    expect(chatLinks()[0].textContent).toContain('Parking?');
  });

  it('shows the source of a citation chip in a saved answer', async () => {
    const cited = [citation(1), citation(3, { documentName: 'Policy.md', pageNumber: null })];
    conversation.mockReturnValue(
      of(
        chat({
          messages: [
            message(),
            message({ id: 'm2', role: 'Assistant', content: 'Yes [1][3].', citations: cited }),
          ],
        }),
      ),
    );
    const { fixture, chips, panel, layout, button } = await render('c1');
    // Before a chip is chosen, the panel shows the sources of the chat's latest answer.
    expect(panel()).toEqual({ sources: cited, selected: null });
    expect(chips().map((chip) => chip.getAttribute('aria-label'))).toEqual([
      'Source 1: Handbook.pdf, page 1',
      'Source 3: Policy.md',
    ]);
    button('Show chats')!.click(); // small screens: the chat list was open over the chat
    await fixture.whenStable();
    expect(layout().classList).toContain('list-open');

    chips()[1].click();
    await fixture.whenStable();

    expect(panel()).toEqual({ sources: cited, selected: 3 });
    expect(layout().classList).toContain('sources-open');
    expect(layout().classList).not.toContain('list-open');
  });

  it('shows the source of a citation chip while the answer is written', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, send, stream, chips, panel } = await render();
    await send('Days?');
    await stream(
      started(),
      { type: 'citations', sources: [answerSource(1), answerSource(2, 'y'.repeat(2500))] },
      { type: 'token', text: 'Twenty [2]' },
    );

    chips()[0].click();
    await fixture.whenStable();

    expect(panel().selected).toBe(2);
    expect(panel().sources.map((s) => [s.number, s.snippet.length])).toEqual([
      [1, 9],
      [2, 2000],
    ]);
  });

  it('shows the sources of the latest answer again after another chat or a new question', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const older = [citation(1)];
    const latest = [citation(2)];
    conversation.mockReturnValue(
      of(
        chat({
          messages: [
            message(),
            message({ id: 'm2', role: 'Assistant', content: 'Old [1].', citations: older }),
            message({ id: 'm3', content: 'And?' }),
            message({ id: 'm4', role: 'Assistant', content: 'New [2].', citations: latest }),
          ],
        }),
      ),
    );
    const { fixture, chips, panel, send, stream } = await render('c1');
    expect(panel()).toEqual({ sources: latest, selected: null });

    chips()[0].click();
    await fixture.whenStable();
    expect(panel()).toEqual({ sources: older, selected: 1 });

    await send('More?');
    expect(panel()).toEqual({ sources: latest, selected: null });
    await stream(
      started('c1', 'q9'),
      { type: 'citations', sources: [answerSource(1), answerSource(2), answerSource(3)] },
      done('Yes [3].', [3]),
    );
    expect(panel().sources.map((s) => s.number)).toEqual([3]);

    chips()[0].click();
    await fixture.whenStable();
    expect(panel()).toEqual({ sources: older, selected: 1 });
    conversation.mockReturnValue(of(chat({ id: 'c2', messages: [message()] })));
    fixture.componentRef.setInput('conversationId', 'c2');
    await fixture.whenStable();
    expect(panel()).toEqual({ sources: [], selected: null });
  });

  it('puts focus back on the chip when the sources panel over the chat is closed', async () => {
    conversation.mockReturnValue(
      of(
        chat({
          messages: [
            message({ id: 'm2', role: 'Assistant', content: 'Yes [1].', citations: [citation(1)] }),
          ],
        }),
      ),
    );
    const { fixture, el, chips, button, layout, send } = await render('c1');
    const backdrop = () => el.querySelector<HTMLElement>('.backdrop');

    chips()[0].focus();
    chips()[0].click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(el.querySelector('.source[data-number="1"]'));
    button('Close sources')!.click();
    await fixture.whenStable();
    expect(layout().classList).not.toContain('sources-open');
    expect(document.activeElement).toBe(chips()[0]);

    chips()[0].focus();
    chips()[0].click();
    await fixture.whenStable();
    backdrop()!.click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(chips()[0]);

    // Opened with the Sources button instead: focus stays where it is.
    button('Sources')!.click();
    await fixture.whenStable();
    const opener = el.querySelector<HTMLElement>('p-button[label="Sources"] button')!;
    opener.focus();
    button('Close sources')!.click();
    await fixture.whenStable();
    expect(document.activeElement).not.toBe(chips()[0]);

    // A chip chosen before a new question: closing later does not jump back to it.
    chips()[0].focus();
    chips()[0].click();
    await fixture.whenStable();
    await send('More?');
    button('Close sources')!.click();
    await fixture.whenStable();
    expect(document.activeElement).not.toBe(chips()[0]);
  });

  it('goes to the source again when the same chip is chosen again', async () => {
    conversation.mockReturnValue(
      of(
        chat({
          messages: [
            message({ id: 'm2', role: 'Assistant', content: 'Yes [1].', citations: [citation(1)] }),
          ],
        }),
      ),
    );
    const { fixture, el, chips } = await render('c1');
    const source = () => el.querySelector<HTMLElement>('.source[data-number="1"]');

    chips()[0].click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(source());
    chips()[0].focus(); // the reader goes back to the answer

    chips()[0].click();
    await fixture.whenStable();

    expect(document.activeElement).toBe(source());
    expect(Element.prototype.scrollIntoView).toHaveBeenCalledTimes(2);
  });

  it('stops the answer on request, keeping the words so far marked as not saved', async () => {
    const { send, stream, bubbles, live, button, announced, el } = await render('c1');
    await send('Long one?');
    await stream(started('c1', 'q9'), { type: 'token', text: 'Part of' });
    const answer = answers.at(-1)!;

    button('Stop answering')!.click();
    await stream();

    expect(answer.observed).toBe(false); // the request is cancelled
    expect(bubbles().slice(-2)).toEqual(['Q: Long one?', 'A: Part of']);
    expect(live()!.getAttribute('aria-busy')).toBe('false');
    expect(live()!.querySelector('.answer-stopped')!.textContent).toContain(
      "You stopped this answer, so it isn't saved.",
    );
    expect(live()!.querySelector('[role="alert"]')).toBeNull();
    expect(announced()).toBe('Answer stopped.');
    expect(button('Stop answering')).toBeUndefined();
    expect(el.textContent).toContain('Enter to send, Shift+Enter for a new line.');

    // A late word from the cancelled request changes nothing.
    answer.next({ type: 'token', text: ' more' });
    await stream();
    expect(bubbles().at(-1)).toBe('A: Part of');

    button('Ask again')!.click();
    await stream();
    expect(ask).toHaveBeenLastCalledWith('b1', 'Long one?', 'c1');
    expect(live()!.querySelector('.answer-stopped')).toBeNull();
  });

  it('ignores a stop that arrives after the answer is complete', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, send, stream, bubbles, announced } = await render();
    await send('Fast?');
    await stream(started(), done('Done.'));

    // e.g. a click on the stop button just as the answer finished
    const composer = fixture.debugElement.query(
      (d) => d.componentInstance instanceof MessageComposer,
    ).componentInstance as MessageComposer;
    composer.stop.emit();
    await stream();

    expect(bubbles()).toEqual(['Q: Fast?', 'A: Done.']);
    expect(announced()).toBe('Answer ready.');
  });

  it('stops an answer before the question is saved, and can ask it again', async () => {
    const { send, bubbles, live, button, stream } = await render();
    await send('Quick?');

    button('Stop answering')!.click();
    await stream();

    expect(bubbles()).toEqual(['Q: Quick?', 'A: ']);
    expect(live()!.querySelector('.waiting')).toBeNull();
    expect(live()!.querySelector('.answer-stopped')).not.toBeNull();

    button('Ask again')!.click();
    await stream();
    expect(ask).toHaveBeenLastCalledWith('b1', 'Quick?', undefined);
  });

  it('rates an answer, takes the rating back, and restores it when saving fails', async () => {
    rate.mockReset().mockReturnValue(of(undefined));
    conversation.mockReturnValue(
      of(
        chat({
          messages: [
            message(),
            message({ id: 'm2', role: 'Assistant', content: 'One.' }),
            message({ id: 'm3', content: 'Again?' }),
            message({ id: 'm4', role: 'Assistant', content: 'Two.' }),
          ],
        }),
      ),
    );
    const { fixture, el } = await render('c1');
    const good = () => el.querySelector<HTMLButtonElement>('button[aria-label="Good answer"]')!;
    const bad = () => el.querySelector<HTMLButtonElement>('button[aria-label="Bad answer"]')!;
    const pressed = () =>
      [...el.querySelectorAll('.actions button[aria-pressed]')].map((b) =>
        b.getAttribute('aria-pressed'),
      );
    expect(pressed()).toEqual(['false', 'false', 'false', 'false']); // answers only

    good().click();
    await fixture.whenStable();
    expect(rate).toHaveBeenLastCalledWith('m2', 'Positive');
    expect(pressed()).toEqual(['true', 'false', 'false', 'false']);
    expect(good().querySelector('i')!.className).toBe('pi pi-thumbs-up-fill');

    good().click();
    await fixture.whenStable();
    expect(rate).toHaveBeenLastCalledWith('m2', 'None');
    expect(pressed()).toEqual(['false', 'false', 'false', 'false']);

    rate.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    bad().click();
    await fixture.whenStable();
    expect(rate).toHaveBeenLastCalledWith('m2', 'Negative');
    expect(pressed()).toEqual(['false', 'false', 'false', 'false']);
  });

  it('copies an answer and says so', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const success = vi.spyOn(TestBed.inject(NotificationService), 'success');
    const error = vi.spyOn(TestBed.inject(NotificationService), 'error');
    const { fixture, el } = await render('c1');
    const copy = () => el.querySelector<HTMLButtonElement>('button[aria-label="Copy answer"]')!;

    copy().click();
    await fixture.whenStable();
    expect(writeText).toHaveBeenCalledWith('You get 25 days [1].');
    expect(success).toHaveBeenCalledWith('Answer copied');

    writeText.mockRejectedValue(new Error('denied'));
    copy().click();
    await fixture.whenStable();
    expect(error).toHaveBeenCalledWith("Couldn't copy the answer");
  });

  it('renames the open chat with Enter, and changes nothing on Escape or an empty name', async () => {
    rename.mockReset().mockReturnValue(of(undefined));
    const { fixture, el, button, chatLinks } = await render('c1');
    const input = () => el.querySelector<HTMLInputElement>('input[aria-label="Chat name"]');
    const edit = async (value: string, key: string) => {
      button('Rename chat')!.click();
      await fixture.whenStable();
      input()!.value = value;
      input()!.dispatchEvent(new KeyboardEvent('keydown', { key }));
      await fixture.whenStable();
    };

    await edit('Ignored', 'Escape');
    await edit('   ', 'Enter');
    expect(rename).not.toHaveBeenCalled();
    expect(input()).toBeNull();

    await edit('  Vacation  ', 'Enter');
    expect(rename).toHaveBeenCalledWith('c1', 'Vacation');
    expect(el.querySelector('h1')!.textContent!.trim()).toBe('Vacation');
    expect(chatLinks()[0].textContent).toContain('Vacation');
  });

  it('deletes the open chat after confirming, then starts a new one', async () => {
    remove.mockReset().mockReturnValue(of(undefined));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, button, chatLinks } = await render('c1');
    const confirm = vi.spyOn(fixture.debugElement.injector.get(ConfirmationService), 'confirm');

    button('Delete chat')!.click();
    expect(remove).not.toHaveBeenCalled();
    confirm.mock.calls[0][0].accept!();
    await fixture.whenStable();

    expect(remove).toHaveBeenCalledWith('c1');
    expect(chatLinks().map((a) => a.getAttribute('href'))).not.toContain('/chat/c1');
    expect(navigate).toHaveBeenCalledWith(['/chat']);
  });

  it("suggests the bot's starter questions in a new chat and asks one when chosen", async () => {
    bots.set([{ ...bots()[0], starterQuestions: ['Vacation?', 'Parking?'] }, ...bots().slice(1)]);
    const { fixture, el, bubbles } = await render();
    const starters = () => [...el.querySelectorAll<HTMLButtonElement>('.starters button')];
    expect(starters().map((b) => b.textContent!.trim())).toEqual(['Vacation?', 'Parking?']);

    starters()[1].click();
    await fixture.whenStable();

    expect(ask).toHaveBeenCalledWith('b1', 'Parking?', undefined);
    expect(bubbles()[0]).toBe('Q: Parking?');
  });

  it('follows new words unless the reader scrolled up, and jumps back on request', async () => {
    const { fixture, el, send, stream, button } = await render('c1');
    const body = el.querySelector<HTMLElement>('.thread-body')!;
    let height = 1000;
    let top = 0;
    Object.defineProperty(body, 'scrollHeight', { get: () => height });
    Object.defineProperty(body, 'clientHeight', { get: () => 400 });
    Object.defineProperty(body, 'scrollTop', { get: () => top, set: (v: number) => (top = v) });
    const scroll = async (to: number) => {
      top = to;
      body.dispatchEvent(new Event('scroll'));
      await fixture.whenStable();
    };

    await scroll(100);
    await send('More?'); // asking goes to the latest message
    expect(top).toBe(1000);

    await scroll(100); // reading further up
    await stream(started('c1', 'q9'), { type: 'token', text: 'Words' });
    expect(top).toBe(100);

    button('Jump to latest')!.click();
    await scroll(top);
    expect(top).toBe(1000);
    expect(button('Jump to latest')).toBeUndefined();

    height = 1200;
    await stream({ type: 'token', text: ' more' });
    expect(top).toBe(1200);
  });

  it('starts a new chat from the list', async () => {
    const { fixture, el } = await render('c1');

    el.querySelector<HTMLButtonElement>('p-button[label="New chat"] button')!.click();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/chat');
  });

  it('keeps the saved answer with the sources it cites', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, send, stream, bubbles } = await render();
    await send('Q?');
    const long = 'x'.repeat(2500);
    await stream(
      started(),
      { type: 'citations', sources: [answerSource(1, long), answerSource(2), answerSource(3)] },
      { type: 'token', text: 'Streamed words' },
      {
        type: 'done',
        messageId: 'a1',
        content: 'Saved [1][3].',
        wasAnswered: false,
        citedSources: [3, 1],
      },
    );

    expect(bubbles()).toEqual(['Q: Q?', 'A: Saved 13.']);
    const saved = (fixture.componentInstance as unknown as { conversation: () => Conversation })
      .conversation()
      .messages.at(-1)!;
    expect(saved).toMatchObject({
      id: 'a1',
      role: 'Assistant',
      wasAnswered: false,
      feedback: 'None',
    });
    expect(saved.citations.map((c) => c.number)).toEqual([1, 3]);
    expect(saved.citations[0]).toEqual({
      number: 1,
      chunkId: 'k1',
      documentId: 'd1',
      documentName: 'Handbook.pdf',
      pageNumber: 1,
      snippet: 'x'.repeat(2000),
      score: 0.03,
    });
  });

  it('says why a question could not be answered and asks it again on request', async () => {
    const { send, fail, stream, bubbles, live, el, button, announced } = await render();
    await send('Too long?');

    await fail('Question must be 4000 characters or fewer.');

    expect(bubbles()).toEqual(['Q: Too long?', 'A: ']);
    expect(live()!.getAttribute('aria-busy')).toBe('false');
    expect(el.querySelector('[role="alert"]')!.textContent).toContain(
      'Question must be 4000 characters or fewer.',
    );
    expect(announced()).toBe('The answer failed.');
    expect(el.textContent).toContain('Enter to send, Shift+Enter for a new line.');

    button('Try again')!.click();
    await stream(started(), done('Answered.'));

    expect(ask).toHaveBeenCalledTimes(2);
    expect(ask).toHaveBeenLastCalledWith('b1', 'Too long?', undefined);
    expect(bubbles()).toEqual(['Q: Too long?', 'A: Answered.']);
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });

  it('keeps the words written so far when answering breaks, and asks the saved question again', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { send, stream, fail, bubbles, el, button } = await render('c1');
    await send('Remote work?');
    await stream(started('c1', 'q5'), { type: 'token', text: 'You can work' });

    await fail('Something went wrong while answering. Please try again.');

    expect(bubbles().slice(2)).toEqual(['Q: Remote work?', 'A: You can work']);
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('Something went wrong');

    button('Try again')!.click();
    await stream(started('c1', 'q6'), done('Two days a week.'));

    expect(ask).toHaveBeenLastCalledWith('b1', 'Remote work?', 'c1');
    expect(bubbles().slice(2)).toEqual([
      'Q: Remote work?',
      'Q: Remote work?',
      'A: Two days a week.',
    ]);
  });

  it('stops the answer when another chat is opened', async () => {
    conversation
      .mockReturnValueOnce(of(chat()))
      .mockReturnValueOnce(of(chat({ id: 'c2', title: 'Parking?', messages: [] })));
    const { fixture, send, stream, bubbles, el, live } = await render('c1');
    await send('Remote work?');
    await stream(started('c1'), { type: 'token', text: 'You can' });

    fixture.componentRef.setInput('conversationId', 'c2');
    await fixture.whenStable();

    expect(answers[0].observed).toBe(false);
    expect(live()).toBeNull();
    expect(bubbles()).toEqual([]);
    expect(el.textContent).toContain('Enter to send, Shift+Enter for a new line.');
  });

  it('stops the answer to a new chat when another bot is chosen', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, send, text, live } = await render();
    await send('Parking?');

    selectedId.set('b2');
    await fixture.whenStable();

    expect(answers[0].observed).toBe(false);
    expect(live()).toBeNull();
    expect(text()).toContain('Ask HR anything');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('counts a new chat, so older chats not shown yet can still be loaded', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    conversations.mockReturnValue(
      of(page([summary('c1', 'One'), summary('c2', 'Two')], 3)), // one older chat not shown yet
    );
    const { send, stream, chatLinks, button } = await render();
    expect(button('Show older chats')).toBeDefined();

    await send('A new one?');
    await stream(started('new1'), done('Yes.'));

    expect(chatLinks()).toHaveLength(3);
    expect(button('Show older chats')).toBeDefined();
  });

  it('stops the answer when the page is left', async () => {
    const { fixture, send } = await render();
    await send('Parking?');

    fixture.destroy();

    expect(answers[0].observed).toBe(false);
  });

  // ---------- side panels on smaller screens ----------

  it('opens one side panel at a time over the chat, and closes them from the backdrop', async () => {
    const { fixture, el, button, layout } = await render('c1');
    const backdrop = () => el.querySelector<HTMLElement>('.backdrop');

    button('Show chats')!.click();
    await fixture.whenStable();
    expect(layout().classList).toContain('list-open');
    expect(
      el.querySelector('p-button[ariaLabel="Show chats"]')?.getAttribute('aria-expanded'),
    ).toBe('true');
    backdrop()!.click();
    await fixture.whenStable();
    expect(layout().classList).not.toContain('list-open');

    button('Sources')!.click();
    await fixture.whenStable();
    expect(layout().classList).toContain('sources-open');
    button('Show chats')!.click();
    await fixture.whenStable();
    expect(layout().classList).toContain('list-open');
    expect(layout().classList).not.toContain('sources-open');

    button('Sources')!.click();
    await fixture.whenStable();
    expect(layout().classList).toContain('sources-open');
    expect(layout().classList).not.toContain('list-open');

    backdrop()!.click();
    await fixture.whenStable();
    expect(layout().classList).not.toContain('sources-open');
    expect(backdrop()).toBeNull();
  });

  it('closes the chat list when a chat is picked or its close button is used', async () => {
    const { fixture, button, layout, chatLinks } = await render();

    button('Show chats')!.click();
    await fixture.whenStable();
    chatLinks()[0].click();
    await fixture.whenStable();
    expect(layout().classList).not.toContain('list-open');

    button('Show chats')!.click();
    await fixture.whenStable();
    button('Close chats')!.click();
    await fixture.whenStable();
    expect(layout().classList).not.toContain('list-open');
  });

  it('closes the sources panel with its close button', async () => {
    const { fixture, button, layout } = await render();

    button('Sources')!.click();
    await fixture.whenStable();
    button('Close sources')!.click();
    await fixture.whenStable();

    expect(layout().classList).not.toContain('sources-open');
  });
});
