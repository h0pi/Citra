import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { BotsApi } from '@core/bots/bots-api';
import { Bot, CreateBotRequest } from '@core/models/bot.models';
import { Observable, Subject, of, throwError } from 'rxjs';

import { CreateBotDialog } from './create-bot-dialog';

const createdBot = { id: 'b1', name: 'Support' } as Bot;

describe('CreateBotDialog', () => {
  const create = vi.fn<(request: CreateBotRequest) => Observable<Bot>>();

  beforeEach(async () => {
    create.mockReset();
    await TestBed.configureTestingModule({
      imports: [CreateBotDialog],
      providers: [{ provide: BotsApi, useValue: { create } }],
    }).compileComponents();
  });

  async function open() {
    const fixture = TestBed.createComponent(CreateBotDialog);
    const emitted: Bot[] = [];
    fixture.componentInstance.created.subscribe((bot) => emitted.push(bot));
    fixture.componentRef.setInput('visible', true);
    await fixture.whenStable();

    const q = <T extends Element>(selector: string) => document.body.querySelector<T>(selector);
    const type = async (selector: string, value: string) => {
      const input = q<HTMLInputElement | HTMLTextAreaElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const clickCreate = async () => {
      const button = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
        b.textContent?.includes('Create bot'),
      )!;
      button.click();
      await fixture.whenStable();
    };
    return { fixture, emitted, q, type, clickCreate };
  }

  afterEach(() => {
    // p-dialog may render outside the fixture; start every test with a clean page.
    document.body.querySelectorAll('.p-dialog-mask, .p-dialog').forEach((e) => e.remove());
  });

  it('requires a name and sends nothing without one', async () => {
    const { q, clickCreate } = await open();

    await clickCreate();

    expect(create).not.toHaveBeenCalled();
    expect(q('.field-error')?.textContent).toContain('Give your bot a name.');
  });

  it('sends the trimmed values, emits the new bot and closes', async () => {
    create.mockReturnValue(of(createdBot));
    const { fixture, emitted, type, clickCreate } = await open();

    await type('#bot-name', '  Support  ');
    await type('#bot-description', '   ');
    await clickCreate();

    expect(create).toHaveBeenCalledWith({ name: 'Support', description: null });
    expect(emitted).toEqual([createdBot]);
    expect(fixture.componentInstance.visible()).toBe(false);
  });

  it('shows server validation errors under the matching field and stays open', async () => {
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              title: 'One or more validation errors occurred.',
              status: 400,
              errors: { name: ["'Name' must not be empty."] },
            },
          }),
      ),
    );
    const { fixture, emitted, q, type, clickCreate } = await open();

    await type('#bot-name', 'Support');
    await clickCreate();

    expect(q('p-message')?.textContent).toContain('Please fix the highlighted fields.');
    expect(q('.field-error')?.textContent).toContain("'Name' must not be empty.");
    expect(emitted).toEqual([]);
    expect(fixture.componentInstance.visible()).toBe(true);
  });

  it('disables the buttons while saving, so a double click creates one bot', async () => {
    const response = new Subject<Bot>();
    create.mockReturnValue(response);
    const { type, clickCreate } = await open();

    await type('#bot-name', 'Support');
    await clickCreate();
    await clickCreate();

    expect(create).toHaveBeenCalledTimes(1);
    response.next(createdBot);
    response.complete();
  });

  it('starts empty every time it is opened', async () => {
    create.mockReturnValue(of(createdBot));
    const { fixture, q, type } = await open();
    await type('#bot-name', 'Old text');

    fixture.componentRef.setInput('visible', false);
    await fixture.whenStable();
    fixture.componentRef.setInput('visible', true);
    await fixture.whenStable();

    expect(q<HTMLInputElement>('#bot-name')!.value).toBe('');
  });
});
