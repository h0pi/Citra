import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { UploadZone } from './upload-zone';

/** jsdom has no DragEvent/DataTransfer: a plain object with what the zone reads. */
function dragEvent(files: File[], types = files.length ? ['Files'] : ['text/plain']) {
  return {
    preventDefault: vi.fn(),
    currentTarget: null as EventTarget | null,
    relatedTarget: null as EventTarget | null,
    dataTransfer: { files, types, dropEffect: 'none' },
  };
}

describe('UploadZone', () => {
  async function render() {
    const fixture = TestBed.createComponent(UploadZone);
    const emitted: File[][] = [];
    fixture.componentInstance.files.subscribe((files) => emitted.push(files));
    await fixture.whenStable();
    const zone = fixture.debugElement.query(By.css('.upload-zone'));
    return { fixture, zone, emitted, el: fixture.nativeElement as HTMLElement };
  }

  it('emits the files dropped on it, and stops the browser from opening them', async () => {
    const { zone, emitted } = await render();
    const files = [new File(['a'], 'a.pdf'), new File(['b'], 'b.md')];
    const drop = dragEvent(files);

    zone.triggerEventHandler('drop', drop);

    expect(drop.preventDefault).toHaveBeenCalled();
    expect(emitted).toEqual([files]);
  });

  it('highlights while files are dragged over it, and stops when they leave', async () => {
    const { fixture, zone } = await render();
    const over = dragEvent([new File(['a'], 'a.pdf')]);

    zone.triggerEventHandler('dragover', over);
    await fixture.whenStable();
    expect(over.preventDefault).toHaveBeenCalled();
    expect(over.dataTransfer.dropEffect).toBe('copy');
    expect(zone.nativeElement.classList).toContain('dragging');

    const leave = dragEvent([]);
    leave.currentTarget = zone.nativeElement;
    leave.relatedTarget = document.body; // outside the zone
    zone.triggerEventHandler('dragleave', leave);
    await fixture.whenStable();
    expect(zone.nativeElement.classList).not.toContain('dragging');
  });

  it('stays highlighted while the pointer moves over its own text', async () => {
    const { fixture, zone } = await render();
    zone.triggerEventHandler('dragover', dragEvent([new File(['a'], 'a.pdf')]));

    const leave = dragEvent([]);
    leave.currentTarget = zone.nativeElement;
    leave.relatedTarget = zone.nativeElement.querySelector('strong');
    zone.triggerEventHandler('dragleave', leave);
    await fixture.whenStable();

    expect(zone.nativeElement.classList).toContain('dragging');
  });

  it('ignores dragged text or links', async () => {
    const { fixture, zone } = await render();
    const over = dragEvent([], ['text/uri-list']);

    zone.triggerEventHandler('dragover', over);
    await fixture.whenStable();

    expect(over.preventDefault).not.toHaveBeenCalled();
    expect(zone.nativeElement.classList).not.toContain('dragging');
  });

  it('opens the file picker from its button and emits the chosen files', async () => {
    const { el, emitted } = await render();
    const input = el.querySelector<HTMLInputElement>('input[type="file"]')!;
    const click = vi.spyOn(input, 'click');
    expect(input.multiple).toBe(true);
    expect(input.accept).toBe('.pdf,.docx,.txt,.md');

    el.querySelector<HTMLButtonElement>('p-button button')!.click();
    expect(click).toHaveBeenCalled();

    const chosen = [new File(['a'], 'a.txt')];
    Object.defineProperty(input, 'files', { value: chosen, configurable: true });
    // A real browser keeps the picked path in "value"; the zone must clear it, or picking the same file again
    // would not fire "change". (jsdom always reports an empty value, so the reset itself is watched.)
    const valueSet = vi.fn();
    Object.defineProperty(input, 'value', {
      set: valueSet,
      get: () => 'C:/fakepath/a.txt',
      configurable: true,
    });
    input.dispatchEvent(new Event('change'));

    expect(emitted).toEqual([chosen]);
    expect(valueSet).toHaveBeenCalledWith('');
  });
});
