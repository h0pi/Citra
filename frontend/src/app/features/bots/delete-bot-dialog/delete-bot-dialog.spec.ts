import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { BotsApi } from '@core/bots/bots-api';
import { Observable, Subject, of, throwError } from 'rxjs';

import { DeleteBotDialog } from './delete-bot-dialog';

describe('DeleteBotDialog', () => {
  const remove = vi.fn<(id: string) => Observable<void>>();

  beforeEach(async () => {
    remove.mockReset();
    await TestBed.configureTestingModule({
      imports: [DeleteBotDialog],
      providers: [{ provide: BotsApi, useValue: { delete: remove } }],
    }).compileComponents();
  });

  afterEach(() => {
    document.body.querySelectorAll('.p-dialog-mask, .p-dialog').forEach((e) => e.remove());
  });

  async function open() {
    const fixture = TestBed.createComponent(DeleteBotDialog);
    let deleted = 0;
    fixture.componentInstance.deleted.subscribe(() => deleted++);
    fixture.componentRef.setInput('bot', { id: 'b1', name: 'Customer Support' });
    fixture.componentRef.setInput('visible', true);
    await fixture.whenStable();

    const deleteButton = () =>
      [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
        b.textContent?.includes('Delete bot'),
      )!;
    const type = async (value: string) => {
      const input = document.body.querySelector<HTMLInputElement>('#confirm-name')!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    return { fixture, deleted: () => deleted, deleteButton, type };
  }

  it('explains what will be lost', async () => {
    await open();
    const text = document.body.querySelector('.p-dialog')?.textContent ?? '';
    expect(text).toContain('Customer Support');
    expect(text).toContain('cannot be undone');
  });

  it('keeps Delete disabled until the exact bot name is typed', async () => {
    const { deleteButton, type } = await open();
    expect(deleteButton().disabled).toBe(true);

    await type('customer support');
    expect(deleteButton().disabled).toBe(true);

    await type('  Customer Support ');
    expect(deleteButton().disabled).toBe(false);
  });

  it('deletes, closes and tells the page', async () => {
    remove.mockReturnValue(of(undefined));
    const { fixture, deleted, deleteButton, type } = await open();

    await type('Customer Support');
    deleteButton().click();
    await fixture.whenStable();

    expect(remove).toHaveBeenCalledWith('b1');
    expect(deleted()).toBe(1);
    expect(fixture.componentInstance.visible()).toBe(false);
  });

  it('treats "already deleted" (404) as done', async () => {
    remove.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const { fixture, deleted, deleteButton, type } = await open();

    await type('Customer Support');
    deleteButton().click();
    await fixture.whenStable();

    expect(deleted()).toBe(1);
  });

  it('shows other errors and stays open so the user can retry', async () => {
    remove.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 403,
            error: { status: 403, detail: 'Your role can view but not change this.' },
          }),
      ),
    );
    const { fixture, deleted, deleteButton, type } = await open();

    await type('Customer Support');
    deleteButton().click();
    await fixture.whenStable();

    expect(document.body.querySelector('p-message')?.textContent).toContain(
      'Your role can view but not change this.',
    );
    expect(deleted()).toBe(0);
    expect(fixture.componentInstance.visible()).toBe(true);
  });

  it('sends one request even if Delete is clicked twice', async () => {
    remove.mockReturnValue(new Subject<void>());
    const { fixture, deleteButton, type } = await open();

    await type('Customer Support');
    deleteButton().click();
    await fixture.whenStable();
    deleteButton().click();

    expect(remove).toHaveBeenCalledTimes(1);
  });

  it('starts with an empty confirmation each time it opens', async () => {
    const { fixture, type } = await open();
    await type('Customer Support');

    fixture.componentRef.setInput('visible', false);
    await fixture.whenStable();
    fixture.componentRef.setInput('visible', true);
    await fixture.whenStable();

    expect(document.body.querySelector<HTMLInputElement>('#confirm-name')!.value).toBe('');
  });
});
