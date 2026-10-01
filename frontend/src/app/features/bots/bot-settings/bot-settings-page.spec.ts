import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { BotsApi } from '@core/bots/bots-api';
import { CurrentBot } from '@core/bots/current-bot';
import { Bot, UpdateBotRequest } from '@core/models/bot.models';
import { NotificationService } from '@core/services/notification.service';
import { Observable, of, throwError } from 'rxjs';

import { DeleteBotDialog } from '../delete-bot-dialog/delete-bot-dialog';
import { SearchPlayground } from '../search-playground/search-playground';
import { BotSettingsPage } from './bot-settings-page';

const savedBot: Bot = {
  id: 'b1',
  name: 'Support',
  slug: 'support',
  description: 'Customer questions',
  systemPrompt: null,
  welcomeMessage: 'Hi! Ask me anything.',
  model: null,
  temperature: 0.2,
  isPublic: false,
  widgetColor: '#6366F1',
  allowedDomains: [],
  starterQuestions: [],
  dailyMessageLimit: 500,
  createdAt: '2026-09-26T10:00:00Z',
  updatedAt: '2026-09-26T10:00:00Z',
};

describe('BotSettingsPage', () => {
  const get = vi.fn<(id: string) => Observable<Bot>>();
  const models = vi.fn<() => Observable<string[]>>();
  const update = vi.fn<(id: string, request: UpdateBotRequest) => Observable<Bot>>();
  const success = vi.fn();
  const refresh = vi.fn();
  const canEdit = signal(true);

  beforeAll(() => {
    // jsdom has no ResizeObserver; PrimeNG's tab list watches its size to show scroll buttons.
    globalThis.ResizeObserver ??= class {
      observe = () => undefined;
      unobserve = () => undefined;
      disconnect = () => undefined;
    } as unknown as typeof ResizeObserver;
  });

  beforeEach(async () => {
    get.mockReset().mockReturnValue(of(savedBot));
    models.mockReset().mockReturnValue(of(['claude-sonnet-5-5', 'claude-haiku-4-5']));
    update.mockReset();
    success.mockReset();
    refresh.mockReset();
    canEdit.set(true);
    await TestBed.configureTestingModule({
      imports: [BotSettingsPage],
      providers: [
        provideRouter([]),
        { provide: BotsApi, useValue: { get, models, update } },
        { provide: AuthService, useValue: { canEdit: canEdit.asReadonly() } },
        { provide: NotificationService, useValue: { success } },
        { provide: CurrentBot, useValue: { refresh } },
      ],
    }).compileComponents();
  });

  async function render(id = 'b1') {
    const fixture = TestBed.createComponent(BotSettingsPage);
    fixture.componentRef.setInput('id', id);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const input = (selector: string) => el.querySelector<HTMLInputElement>(selector)!;
    const type = async (selector: string, value: string) => {
      const field = input(selector);
      field.value = value;
      field.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const button = (label: string) =>
      [...el.querySelectorAll<HTMLButtonElement>('p-button button')].find((b) =>
        b.textContent?.includes(label),
      );
    return { fixture, el, input, type, button };
  }

  it('loads the bot into the form, with nothing to save yet', async () => {
    const { el, input, button } = await render();

    expect(get).toHaveBeenCalledWith('b1');
    expect(el.querySelector('h1')?.textContent).toContain('Support');
    expect(input('#name').value).toBe('Support');
    expect(input('#description').value).toBe('Customer questions');
    expect(input('#welcomeMessage').value).toBe('Hi! Ask me anything.');
    expect(input('#widgetColor').value).toBe('#6366F1');
    expect(el.textContent).toContain('support'); // the slug hint
    expect(button('Save changes')!.disabled).toBe(true);
    expect(el.textContent).not.toContain('Unsaved changes');
  });

  it('saves trimmed values, then shows the saved bot and confirms', async () => {
    update.mockImplementation((_id, request) =>
      of({ ...savedBot, ...request, updatedAt: '2026-09-26T12:00:00Z' }),
    );
    const { fixture, el, type, button } = await render();

    await type('#name', '  Sales  ');
    await type('#systemPrompt', '  Be brief.  ');
    await type('#description', '   ');
    await type('#widgetColor', '#10b981');
    expect(el.textContent).toContain('Unsaved changes');

    button('Save changes')!.click();
    await fixture.whenStable();

    expect(update).toHaveBeenCalledWith('b1', {
      name: 'Sales',
      description: null,
      systemPrompt: 'Be brief.',
      welcomeMessage: 'Hi! Ask me anything.',
      model: null,
      temperature: 0.2,
      widgetColor: '#10B981',
    });
    expect(success).toHaveBeenCalledWith('Settings saved');
    expect(refresh).toHaveBeenCalled(); // top-bar selector shows the new name
    expect(el.querySelector('h1')?.textContent).toContain('Sales');
    expect(el.textContent).not.toContain('Unsaved changes');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('does not send invalid values and explains what is wrong', async () => {
    const { fixture, el, type, button } = await render();

    await type('#name', '');
    await type('#widgetColor', 'red');
    button('Save changes')!.click();
    await fixture.whenStable();

    expect(update).not.toHaveBeenCalled();
    const errors = [...el.querySelectorAll('.field-error')].map((e) => e.textContent?.trim());
    expect(errors).toContain('This field is required.');
    expect(errors).toContain('Use a hex color like #6366F1.');
  });

  it('shows server validation errors under the right field', async () => {
    update.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { status: 400, errors: { model: ['Model must be one of: ...'] } },
          }),
      ),
    );
    const { fixture, el, type, button } = await render();

    await type('#name', 'Sales');
    button('Save changes')!.click();
    await fixture.whenStable();

    expect(el.querySelector('p-message')?.textContent).toContain(
      'Please fix the highlighted fields.',
    );
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('keeps the color picker and the hex field in sync, both ways', async () => {
    const { fixture, input, type } = await render();
    const swatch = input('input[type="color"]');

    await type('#widgetColor', '#10B981');
    expect(swatch.value).toBe('#10b981');

    swatch.value = '#ff0000';
    swatch.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(input('#widgetColor').value).toBe('#FF0000');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('Discard brings back the saved values', async () => {
    const { fixture, input, type, button } = await render();

    await type('#name', 'Something else');
    button('Discard')!.click();
    await fixture.whenStable();

    expect(input('#name').value).toBe('Support');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('reports unsaved changes so the route guard can ask before leaving', async () => {
    const { fixture, type } = await render();
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);

    await type('#name', 'Edited');

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('is read-only for viewers', async () => {
    canEdit.set(false);
    const { fixture, el, input, button } = await render();

    expect(el.textContent).toContain('only an owner or admin can change them');
    expect(input('#name').disabled).toBe(true);
    expect(input('#systemPrompt').disabled).toBe(true);
    expect(button('Save changes')).toBeUndefined();
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('shows "Bot not found" for a missing or foreign bot', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const { el } = await render('missing');

    expect(el.textContent).toContain('Bot not found');
    expect(el.querySelector('form')).toBeNull();
  });

  it('offers a retry when loading fails for another reason', async () => {
    get
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValueOnce(of(savedBot));
    const { fixture, el, input, button } = await render();

    expect(el.textContent).toContain("Couldn't load this bot");
    button('Try again')!.click();
    await fixture.whenStable();

    expect(input('#name').value).toBe('Support');
  });

  it('offers the default model plus the supported ones', async () => {
    const { fixture } = await render();
    const options = (
      fixture.componentInstance as unknown as { modelOptions: () => { value: string | null }[] }
    ).modelOptions();

    expect(options.map((o) => o.value)).toEqual([null, 'claude-sonnet-5-5', 'claude-haiku-4-5']);
  });

  it('has a danger zone for editors that opens the delete dialog', async () => {
    const { fixture, el, button } = await render();
    const dialog = fixture.debugElement.query(By.directive(DeleteBotDialog))
      .componentInstance as DeleteBotDialog;

    expect(el.textContent).toContain('Danger zone');
    button('Delete bot')!.click();
    await fixture.whenStable();

    expect(dialog.visible()).toBe(true);
    expect(dialog.bot()).toEqual(expect.objectContaining({ id: 'b1', name: 'Support' }));
  });

  it('hides the danger zone from viewers', async () => {
    canEdit.set(false);
    const { fixture, el } = await render();

    expect(el.textContent).not.toContain('Danger zone');
    expect(fixture.debugElement.query(By.directive(DeleteBotDialog))).toBeNull();
  });

  it('after deleting, goes back to the list without asking about unsaved changes', async () => {
    const { fixture, type } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await type('#name', 'Half-edited');

    const dialog = fixture.debugElement.query(By.directive(DeleteBotDialog))
      .componentInstance as DeleteBotDialog;
    dialog.deleted.emit();
    await fixture.whenStable();

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    expect(success).toHaveBeenCalledWith('Bot deleted', '"Support" and all its data were removed.');
    expect(navigate).toHaveBeenCalledWith('/bots');
    expect(refresh).toHaveBeenCalled(); // the deleted bot leaves the top-bar selector
  });

  describe('search playground tab', () => {
    const tabs = (el: HTMLElement) =>
      [...el.querySelectorAll<HTMLElement>('p-tab')].map((t) => t.textContent?.trim());
    const playground = (fixture: { debugElement: import('@angular/core').DebugElement }) =>
      fixture.debugElement.query(By.directive(SearchPlayground));

    it('gives owners and admins a second tab with the playground for this bot', async () => {
      const { fixture, el } = await render();

      expect(tabs(el)).toEqual(['Settings', 'Search playground']);
      expect(el.querySelector('p-tab[aria-selected="true"]')?.textContent).toContain('Settings');

      el.querySelectorAll<HTMLElement>('p-tab')[1].click();
      await fixture.whenStable();

      expect(el.querySelector('p-tab[aria-selected="true"]')?.textContent).toContain(
        'Search playground',
      );
      const component = playground(fixture).componentInstance as SearchPlayground;
      expect(component.botId()).toBe('b1');
    });

    it('keeps the settings form working inside its tab', async () => {
      const { el, input } = await render();

      expect(input('#name').value).toBe('Support');
      expect(el.textContent).toContain('Danger zone');
    });

    it('shows viewers only the settings, without tabs or playground', async () => {
      canEdit.set(false);
      const { fixture, el, input } = await render();

      expect(el.querySelector('p-tabs')).toBeNull();
      expect(playground(fixture)).toBeNull();
      expect(input('#name').value).toBe('Support');
    });
  });
});
