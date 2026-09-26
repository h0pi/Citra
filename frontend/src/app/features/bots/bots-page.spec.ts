import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { BotsApi } from '@core/bots/bots-api';
import { CurrentBot } from '@core/bots/current-bot';
import { Bot, BotSummary } from '@core/models/bot.models';
import { NotificationService } from '@core/services/notification.service';
import { Observable, Subject, of, throwError } from 'rxjs';

import { BotsPage } from './bots-page';
import { CreateBotDialog } from './create-bot-dialog/create-bot-dialog';

function bot(overrides: Partial<BotSummary> = {}): BotSummary {
  return {
    id: 'b1',
    name: 'Support',
    slug: 'support',
    description: 'Answers product questions',
    isPublic: false,
    widgetColor: '#6366F1',
    documentCount: 2,
    messageCount: 1,
    createdAt: '2026-09-26T10:00:00Z',
    updatedAt: '2026-09-26T10:00:00Z',
    ...overrides,
  };
}

describe('BotsPage', () => {
  const list = vi.fn<() => Observable<BotSummary[]>>();
  const success = vi.fn();
  const refresh = vi.fn();
  const canEdit = signal(true);

  beforeEach(async () => {
    list.mockReset();
    success.mockReset();
    refresh.mockReset();
    canEdit.set(true);
    await TestBed.configureTestingModule({
      imports: [BotsPage],
      providers: [
        provideRouter([]),
        { provide: BotsApi, useValue: { list } },
        { provide: AuthService, useValue: { canEdit: canEdit.asReadonly() } },
        { provide: NotificationService, useValue: { success } },
        { provide: CurrentBot, useValue: { refresh } },
      ],
    }).compileComponents();
  });

  const buttonLabels = (el: HTMLElement) =>
    [...el.querySelectorAll('p-button button')].map((b) => b.textContent?.trim());

  async function render() {
    const fixture = TestBed.createComponent(BotsPage);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('shows placeholders while loading', async () => {
    list.mockReturnValue(new Subject<BotSummary[]>());
    const { el } = await render();

    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(el.querySelectorAll('p-skeleton').length).toBeGreaterThan(0);
  });

  it('shows a card per bot with its details and counts', async () => {
    list.mockReturnValue(
      of([
        bot(),
        bot({
          id: 'b2',
          name: 'hr',
          slug: 'hr',
          description: null,
          isPublic: true,
          documentCount: 1,
          messageCount: 0,
        }),
      ]),
    );
    const { el } = await render();

    const cards = el.querySelectorAll('.bot-card');
    expect(cards.length).toBe(2);

    const [support, hr] = [...cards].map((c) => c.textContent ?? '');
    expect(support).toContain('Support');
    expect(support).toContain('Answers product questions');
    expect(support).toContain('2 documents');
    expect(support).toContain('1 message');
    expect(support).toContain('Private');

    expect(hr).toContain('No description');
    expect(hr).toContain('1 document');
    expect(hr).toContain('0 messages');
    expect(hr).toContain('Public');
    expect(cards[1].querySelector('.bot-avatar')?.textContent?.trim()).toBe('H');
  });

  it('links each card to the bot settings page', async () => {
    list.mockReturnValue(of([bot({ id: 'b7' })]));
    const { el } = await render();

    expect(el.querySelector('.bot-card a')?.getAttribute('href')).toBe('/bots/b7');
  });

  it('colors the avatar with the bot widget color', async () => {
    list.mockReturnValue(of([bot({ widgetColor: '#10B981' })]));
    const { el } = await render();

    const avatar = el.querySelector<HTMLElement>('.bot-avatar')!;
    expect(avatar.style.background).toContain('rgb(16, 185, 129)');
  });

  it('shows the empty state when there are no bots', async () => {
    list.mockReturnValue(of([]));
    const { el } = await render();

    expect(el.textContent).toContain('No bots yet');
    expect(el.querySelector('.bot-card')).toBeNull();
  });

  it('offers a retry when loading fails, and retrying loads the bots', async () => {
    list
      .mockReturnValueOnce(throwError(() => new Error('offline')))
      .mockReturnValueOnce(of([bot()]));
    const { fixture, el } = await render();

    expect(el.textContent).toContain("Couldn't load your bots");
    el.querySelector<HTMLButtonElement>('p-button button')!.click();
    await fixture.whenStable();

    expect(list).toHaveBeenCalledTimes(2);
    expect(el.querySelectorAll('.bot-card').length).toBe(1);
  });

  it('shows "New bot" to owners and admins, but not to read-only roles', async () => {
    list.mockReturnValue(of([bot()]));
    const { el } = await render();
    expect(buttonLabels(el)).toContain('New bot');

    canEdit.set(false);
    const readOnly = await render();
    expect(buttonLabels(readOnly.el)).not.toContain('New bot');
  });

  it('the empty state invites editors to create a bot and tells viewers who can', async () => {
    list.mockReturnValue(of([]));
    const { el } = await render();
    expect(buttonLabels(el)).toContain('Create your first bot');

    canEdit.set(false);
    const readOnly = await render();
    expect(buttonLabels(readOnly.el)).toEqual([]);
    expect(readOnly.el.textContent).toContain(
      'An owner or admin of your workspace can create one.',
    );
  });

  it('opens the create dialog from the header button', async () => {
    list.mockReturnValue(of([bot()]));
    const { fixture, el } = await render();
    const dialog = fixture.debugElement.query(By.directive(CreateBotDialog))
      .componentInstance as CreateBotDialog;
    expect(dialog.visible()).toBe(false);

    [...el.querySelectorAll<HTMLButtonElement>('p-button button')]
      .find((b) => b.textContent?.includes('New bot'))!
      .click();
    await fixture.whenStable();

    expect(dialog.visible()).toBe(true);
  });

  it('adds a created bot to the list in name order and confirms with a toast', async () => {
    list.mockReturnValue(of([bot({ id: 'b1', name: 'Alpha' }), bot({ id: 'b3', name: 'Zulu' })]));
    const { fixture, el } = await render();

    const dialog = fixture.debugElement.query(By.directive(CreateBotDialog))
      .componentInstance as CreateBotDialog;
    dialog.created.emit({
      id: 'b2',
      name: 'marketing',
      slug: 'marketing',
      description: null,
      isPublic: false,
      widgetColor: '#6366F1',
      createdAt: '2026-09-26T11:00:00Z',
      updatedAt: '2026-09-26T11:00:00Z',
    } as Bot);
    await fixture.whenStable();

    const names = [...el.querySelectorAll('.bot-card h3')].map((h) => h.textContent?.trim());
    expect(names).toEqual(['Alpha', 'marketing', 'Zulu']);
    expect(el.querySelectorAll('.bot-card')[1].textContent).toContain('0 documents');
    expect(success).toHaveBeenCalledWith('Bot created', '"marketing" is ready for documents.');
    expect(refresh).toHaveBeenCalledWith('b2'); // the new bot becomes the current one
  });
});
