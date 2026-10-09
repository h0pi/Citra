import { TestBed } from '@angular/core/testing';
import { Bot } from '@core/models/bot.models';
import { NotificationService } from '@core/services/notification.service';
import { environment } from '@env/environment';
import { MessageService } from 'primeng/api';

import { EmbedPanel } from './embed-panel';

const bot = { tenantSlug: 'acme', slug: 'support', isPublic: true } as Bot;
const snippet = `<script src="${environment.apiUrl}/widget.js" data-bot="acme/support" defer></script>`;

describe('EmbedPanel', () => {
  async function render(value: Bot = bot) {
    TestBed.configureTestingModule({ imports: [EmbedPanel], providers: [MessageService] });
    const fixture = TestBed.createComponent(EmbedPanel);
    fixture.componentRef.setInput('bot', value);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the snippet with the bot address and a preview page that uses it', async () => {
    const el = await render();

    expect(el.querySelector('pre')?.textContent).toBe(snippet);
    expect(el.querySelector('iframe')?.getAttribute('srcdoc')).toContain(snippet);
    expect(el.textContent).not.toContain('This bot is private');
  });

  it('copies the snippet', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const el = await render();
    const success = vi.spyOn(TestBed.inject(NotificationService), 'success');

    el.querySelector<HTMLButtonElement>('p-button button')!.click();
    await Promise.resolve();

    expect(writeText).toHaveBeenCalledWith(snippet);
    expect(success).toHaveBeenCalledWith('Snippet copied');
  });

  it('warns that a private bot does not appear on websites', async () => {
    const el = await render({ ...bot, isPublic: false });

    expect(el.textContent).toContain('This bot is private');
  });
});
