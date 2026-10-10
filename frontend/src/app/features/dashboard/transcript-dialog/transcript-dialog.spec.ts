import { TestBed } from '@angular/core/testing';
import { AnalyticsApi } from '@core/analytics/analytics-api';
import { Conversation, ConversationMessage } from '@core/models/chat.models';
import { Observable, of, throwError } from 'rxjs';

import { TranscriptDialog } from './transcript-dialog';

const message = (m: Partial<ConversationMessage>): ConversationMessage => ({
  id: 'm',
  role: 'User',
  content: '',
  createdAt: '2026-10-10T09:00:00Z',
  wasAnswered: true,
  feedback: 'None',
  citations: [],
  ...m,
});

const chat: Conversation = {
  id: 'c1',
  botId: 'b1',
  title: 'Parking',
  createdAt: '2026-10-10T09:00:00Z',
  updatedAt: '2026-10-10T09:01:00Z',
  messages: [
    message({ id: 'm1', content: 'Where do I park?' }),
    message({
      id: 'm2',
      role: 'Assistant',
      content: 'I could not find this in the documents.',
      wasAnswered: false,
    }),
    message({ id: 'm3', content: 'Vacation days?' }),
    message({
      id: 'm4',
      role: 'Assistant',
      content: 'You get 25 days [1].',
      citations: [
        {
          number: 1,
          chunkId: 'k1',
          documentId: 'd1',
          documentName: 'Handbook.pdf',
          pageNumber: 3,
          snippet: '25 days',
          score: 0.9,
        },
      ],
    }),
  ],
};

describe('TranscriptDialog', () => {
  const conversation = vi.fn<(id: string) => Observable<Conversation>>();

  beforeEach(() => {
    vi.clearAllMocks();
    conversation.mockReturnValue(of(chat));
    TestBed.configureTestingModule({
      imports: [TranscriptDialog],
      providers: [{ provide: AnalyticsApi, useValue: { conversation } }],
    });
  });

  afterEach(() => document.body.querySelectorAll('.p-dialog-mask').forEach((m) => m.remove()));

  async function render(id: string | null) {
    const fixture = TestBed.createComponent(TranscriptDialog);
    fixture.componentRef.setInput('conversationId', id);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('shows the chat read-only, with unanswered answers and sources marked', async () => {
    await render('c1');

    expect(conversation).toHaveBeenCalledWith('c1');
    const items = [...document.body.querySelectorAll('.messages > li')].map((li) =>
      [...li.children].map((c) => c.textContent?.replace(/\s+/g, ' ').trim()).join(' | '),
    );
    expect(items).toEqual([
      'Question | Where do I park?',
      'Answer | I could not find this in the documents. | Not found in the documents',
      'Question | Vacation days?',
      'Answer | You get 25 days [1]. | [1] Handbook.pdf, page 3',
    ]);
    expect(document.body.textContent).toContain('Parking');
  });

  it('loads nothing while closed and says when the chat cannot be opened', async () => {
    await render(null);
    expect(conversation).not.toHaveBeenCalled();

    conversation.mockReturnValue(throwError(() => new Error('403')));
    await render('c9');
    expect(document.body.textContent).toContain("Couldn't open this chat.");
  });
});
