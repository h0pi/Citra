import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { AnalyticsApi } from '@core/analytics/analytics-api';
import { CompanyConversation, CompanyConversationsQuery } from '@core/models/analytics.models';
import { PagedResult } from '@core/models/document.models';
import { Paginator } from 'primeng/paginator';
import { Observable, of, throwError } from 'rxjs';

import { ConversationsBrowser } from './conversations-browser';

const chat = (id: string, channel: 'Admin' | 'Widget' = 'Admin'): CompanyConversation => ({
  id,
  botId: 'b1',
  title: `Chat ${id}`,
  channel,
  messageCount: 4,
  updatedAt: '2026-10-10T09:30:00',
});

const page = (items: CompanyConversation[], totalCount = items.length) =>
  of<PagedResult<CompanyConversation>>({
    items,
    page: 1,
    pageSize: 20,
    totalCount,
    totalPages: Math.ceil(totalCount / 20),
  });

describe('ConversationsBrowser', () => {
  const conversations =
    vi.fn<(query: CompanyConversationsQuery) => Observable<PagedResult<CompanyConversation>>>();

  beforeEach(() => {
    vi.clearAllMocks();
    conversations.mockReturnValue(page([chat('c1', 'Widget'), chat('c2')]));
    TestBed.configureTestingModule({
      imports: [ConversationsBrowser],
      providers: [{ provide: AnalyticsApi, useValue: { conversations } }],
    });
  });

  async function render(botId: string | null = null) {
    const fixture = TestBed.createComponent(ConversationsBrowser);
    fixture.componentRef.setInput('botId', botId);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const rows = (el: HTMLElement) =>
    [...el.querySelectorAll('tbody tr')].map((r) =>
      [...r.querySelectorAll('td')].map((c) => c.textContent?.trim()),
    );

  interface Browser {
    channel: { set(v: string | null): void };
  }

  it('lists the chats of the bot and opens one', async () => {
    const { fixture, el } = await render('b1');
    const opened: string[] = [];
    fixture.componentInstance.view.subscribe((id) => opened.push(id));

    expect(conversations).toHaveBeenCalledWith({
      botId: 'b1',
      channel: null,
      page: 1,
      pageSize: 20,
    });
    expect(rows(el)).toEqual([
      ['Chat c1', 'Website', '4', 'Oct 10, 09:30', 'View'],
      ['Chat c2', 'Admin app', '4', 'Oct 10, 09:30', 'View'],
    ]);
    el.querySelectorAll<HTMLButtonElement>('tbody button')[0].click();
    expect(opened).toEqual(['c1']);
    expect(el.querySelector('p-paginator')).toBeNull();
  });

  it('pages through long lists and starts again at page 1 for another channel', async () => {
    conversations.mockReturnValue(page([chat('c1')], 45));
    const { fixture } = await render();
    const browser = fixture.componentInstance as unknown as Browser;

    const paginator = fixture.debugElement.query(By.directive(Paginator))
      .componentInstance as Paginator;
    paginator.onPageChange.emit({ page: 2, first: 40, rows: 20 });
    fixture.detectChanges();
    await fixture.whenStable();
    expect(conversations).toHaveBeenLastCalledWith({
      botId: null,
      channel: null,
      page: 3,
      pageSize: 20,
    });

    browser.channel.set('Widget');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(conversations).toHaveBeenLastCalledWith({
      botId: null,
      channel: 'Widget',
      page: 1,
      pageSize: 20,
    });
  });

  it('says when there are no chats and offers a retry when loading fails', async () => {
    conversations.mockReturnValueOnce(throwError(() => new Error('offline')));
    conversations.mockReturnValueOnce(page([]));
    const { fixture, el } = await render();

    expect(el.textContent).toContain("Couldn't load the conversations.");
    el.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.textContent).toContain('No conversations yet.');
  });
});
