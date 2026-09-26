import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CurrentBot } from '@core/bots/current-bot';
import { BotSummary } from '@core/models/bot.models';

import { BotSelector } from './bot-selector';

function bot(id: string, name: string, color = '#6366F1'): BotSummary {
  return {
    id,
    name,
    slug: id,
    description: null,
    isPublic: false,
    widgetColor: color,
    documentCount: 0,
    messageCount: 0,
    createdAt: '2026-09-26T10:00:00Z',
    updatedAt: '2026-09-26T10:00:00Z',
  };
}

describe('BotSelector', () => {
  const bots = signal<BotSummary[]>([]);
  const status = signal<'idle' | 'loading' | 'ready' | 'error'>('ready');
  const selectedId = signal<string | null>(null);
  const select = vi.fn((id: string) => selectedId.set(id));
  const refresh = vi.fn();

  beforeAll(() => {
    // jsdom has no matchMedia; PrimeNG's dropdown overlay asks it about the screen size when opening.
    window.matchMedia ??= ((query: string) =>
      ({
        matches: false,
        media: query,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
      }) as unknown as MediaQueryList) as typeof window.matchMedia;
  });

  beforeEach(async () => {
    bots.set([bot('a', 'Customer Support', '#10B981'), bot('b', 'HR Assistant')]);
    status.set('ready');
    selectedId.set('a');
    select.mockClear();
    refresh.mockReset();
    await TestBed.configureTestingModule({
      imports: [BotSelector],
      providers: [
        provideRouter([]),
        {
          provide: CurrentBot,
          useValue: {
            bots,
            status,
            selectedId,
            bot: computed(() => bots().find((b) => b.id === selectedId()) ?? null),
            select,
            refresh,
          },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    document.body.querySelectorAll('.p-select-overlay').forEach((e) => e.remove());
  });

  async function render() {
    const fixture = TestBed.createComponent(BotSelector);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('shows the current bot with its color', async () => {
    const { el } = await render();

    expect(el.querySelector('.p-select-label')?.textContent).toContain('Customer Support');
    expect(el.querySelector<HTMLElement>('.p-select-label .dot')!.style.background).toContain(
      'rgb(16, 185, 129)',
    );
  });

  it('switches the current bot when another one is picked', async () => {
    const { fixture, el } = await render();

    el.querySelector<HTMLElement>('.p-select')!.click();
    await fixture.whenStable();
    const option = [...document.body.querySelectorAll<HTMLElement>('.p-select-option')].find((o) =>
      o.textContent?.includes('HR Assistant'),
    )!;
    option.click();
    await fixture.whenStable();

    expect(select).toHaveBeenCalledWith('b');
    expect(el.querySelector('.p-select-label')?.textContent).toContain('HR Assistant');
  });

  it('offers to create a bot when the company has none', async () => {
    bots.set([]);
    selectedId.set(null);
    const { el } = await render();

    const link = el.querySelector('p-button');
    expect(link?.textContent).toContain('Create a bot');
    expect(el.querySelector('a, button')).not.toBeNull();
    expect(el.querySelector('p-select')).toBeNull();
  });

  it('shows a loading hint, then a retry if loading failed', async () => {
    status.set('loading');
    const { fixture, el } = await render();
    expect(el.textContent).toContain('Loading bots');

    status.set('error');
    await fixture.whenStable();
    el.querySelector<HTMLButtonElement>('p-button button')!.click();

    expect(refresh).toHaveBeenCalledTimes(1);
  });
});
