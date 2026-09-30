import { TestBed } from '@angular/core/testing';

import { UploadItem } from './document-uploads';
import { UploadList } from './upload-list';

function item(overrides: Partial<UploadItem>): UploadItem {
  return {
    id: 1,
    fileName: 'Handbook.pdf',
    progress: 0,
    state: 'waiting',
    error: null,
    ...overrides,
  };
}

describe('UploadList', () => {
  async function render(items: UploadItem[]) {
    const fixture = TestBed.createComponent(UploadList);
    fixture.componentRef.setInput('items', items);
    const dismissed: number[] = [];
    fixture.componentInstance.dismiss.subscribe((id) => dismissed.push(id));
    await fixture.whenStable();
    return { el: fixture.nativeElement as HTMLElement, dismissed };
  }

  it('shows nothing when no file is being uploaded', async () => {
    const { el } = await render([]);

    expect(el.querySelector('ul')).toBeNull();
  });

  it('shows each file with its progress, result or error', async () => {
    const { el } = await render([
      item({ id: 1, fileName: 'a.pdf', state: 'waiting' }),
      item({ id: 2, fileName: 'b.pdf', state: 'uploading', progress: 42 }),
      item({ id: 3, fileName: 'c.pdf', state: 'done', progress: 100 }),
      item({
        id: 4,
        fileName: 'd.jpg',
        state: 'failed',
        error: 'This file type is not supported.',
      }),
    ]);

    const rows = [...el.querySelectorAll('li')];
    expect(rows.map((r) => r.querySelector('.upload-state')?.textContent?.trim())).toEqual([
      'Waiting…',
      '42%',
      'Uploaded',
      'Failed',
    ]);
    expect(rows.map((r) => r.querySelector('p-progressbar') !== null)).toEqual([
      true,
      true,
      false,
      false,
    ]);
    expect(rows[3].querySelector('.field-error')?.textContent).toContain(
      'This file type is not supported.',
    );
  });

  it('offers to cancel a running upload and to dismiss a finished one', async () => {
    const { el, dismissed } = await render([
      item({ id: 7, fileName: 'a.pdf', state: 'uploading', progress: 10 }),
      item({ id: 8, fileName: 'b.pdf', state: 'failed', error: 'Too big' }),
    ]);

    const buttons = [...el.querySelectorAll<HTMLButtonElement>('p-button button')];
    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual([
      'Cancel a.pdf',
      'Dismiss b.pdf',
    ]);

    buttons[0].click();
    buttons[1].click();
    expect(dismissed).toEqual([7, 8]);
  });
});
