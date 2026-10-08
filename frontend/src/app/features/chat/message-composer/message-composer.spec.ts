import { TestBed } from '@angular/core/testing';

import { COUNTER_FROM, MessageComposer, QUESTION_MAX_LENGTH } from './message-composer';

describe('MessageComposer', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [MessageComposer] }).compileComponents();
  });

  async function render(busy = false) {
    const fixture = TestBed.createComponent(MessageComposer);
    fixture.componentRef.setInput('busy', busy);
    const sent: string[] = [];
    fixture.componentInstance.send.subscribe((question) => sent.push(question));
    let stops = 0;
    fixture.componentInstance.stop.subscribe(() => stops++);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const box = () => el.querySelector<HTMLTextAreaElement>('textarea')!;
    const sendButton = () =>
      el.querySelector<HTMLButtonElement>('p-button[ariaLabel="Send"] button');
    const stopButton = () =>
      el.querySelector<HTMLButtonElement>('p-button[ariaLabel="Stop answering"] button');
    const type = async (text: string) => {
      box().value = text;
      box().dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const press = async (init: KeyboardEventInit) => {
      const event = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, ...init });
      box().dispatchEvent(event);
      await fixture.whenStable();
      return event;
    };
    return { fixture, el, box, sendButton, stopButton, type, press, sent, stops: () => stops };
  }

  it('sends the trimmed question on Enter and empties the box', async () => {
    const { box, type, press, sent } = await render();

    await type('  How many vacation days?  ');
    const enter = await press({});

    expect(sent).toEqual(['How many vacation days?']);
    expect(enter.defaultPrevented).toBe(true);
    expect(box().value).toBe('');
    expect(document.activeElement).toBe(box());
  });

  it('starts a new line on Shift+Enter instead of sending', async () => {
    const { type, press, sent } = await render();

    await type('First line');
    const shiftEnter = await press({ shiftKey: true });

    expect(sent).toEqual([]);
    expect(shiftEnter.defaultPrevented).toBe(false);
  });

  it('lets Enter confirm a character being composed (e.g. accents) without sending', async () => {
    const { type, press, sent } = await render();

    await type('Koliko dana');
    const composing = await press({ isComposing: true });

    expect(sent).toEqual([]);
    expect(composing.defaultPrevented).toBe(false);
  });

  it('ignores other keys', async () => {
    const { type, press, sent } = await render();

    await type('Question');
    await press({ key: 'a' });

    expect(sent).toEqual([]);
  });

  it('sends with the button too, and only when there is a question', async () => {
    const { fixture, type, sendButton, sent } = await render();
    expect(sendButton()!.disabled).toBe(true);

    await type('   ');
    expect(sendButton()!.disabled).toBe(true);

    await type('Where do I park?');
    expect(sendButton()!.disabled).toBe(false);
    sendButton()!.click();
    await fixture.whenStable();

    expect(sent).toEqual(['Where do I park?']);
    expect(sendButton()!.disabled).toBe(true);
  });

  it('never sends an empty question with Enter', async () => {
    const { type, press, sent } = await render();

    await type(' \n ');
    const enter = await press({});

    expect(sent).toEqual([]);
    expect(enter.defaultPrevented).toBe(true); // Enter still adds no line
  });

  it('waits while an answer is being written, keeping what is typed', async () => {
    const { fixture, el, box, type, press, sendButton, stopButton, sent } = await render(true);

    await type('Next question');
    await press({});
    expect(sent).toEqual([]);
    expect(box().value).toBe('Next question');
    expect(sendButton()).toBeNull();
    expect(el.textContent).toContain('Wait for the answer to finish before sending, or stop it.');

    fixture.componentRef.setInput('busy', false);
    await fixture.whenStable();
    expect(stopButton()).toBeNull();
    await press({});
    expect(sent).toEqual(['Next question']);
  });

  it('offers to stop the answer being written, then waits in the box', async () => {
    const { fixture, el, box, stopButton, sent, stops } = await render(true);
    document.body.appendChild(el); // focus needs the page
    const stop = stopButton()!;
    expect(stop.type).toBe('button'); // never submits the form

    stop.focus();
    stop.click();
    await fixture.whenStable();

    expect(stops()).toBe(1);
    expect(sent).toEqual([]);
    expect(document.activeElement).toBe(box());
    el.remove();
  });

  it('explains the keys when nothing is being answered', async () => {
    const { el } = await render();

    expect(el.textContent).toContain('Enter to send, Shift+Enter for a new line.');
    expect(el.querySelector('textarea')!.getAttribute('aria-describedby')).toBe('composer-hint');
  });

  it('limits questions to the API’s length and counts characters near the limit', async () => {
    const { el, box, type, sendButton } = await render();
    expect(box().getAttribute('maxlength')).toBe(String(QUESTION_MAX_LENGTH));

    await type('a'.repeat(COUNTER_FROM - 1));
    expect(el.querySelector('.counter')).toBeNull();

    await type('a'.repeat(COUNTER_FROM));
    expect(el.querySelector('.counter')!.textContent!.trim()).toBe(
      `${COUNTER_FROM} / ${QUESTION_MAX_LENGTH}`,
    );
    expect(el.querySelector('.counter')!.classList).not.toContain('full');

    await type('a'.repeat(QUESTION_MAX_LENGTH));
    expect(el.querySelector('.counter')!.classList).toContain('full');
    expect(sendButton()!.disabled).toBe(false);

    await type('a'.repeat(QUESTION_MAX_LENGTH + 1)); // pasted past the limit by a script
    expect(sendButton()!.disabled).toBe(true);
  });

  it('grows with the question up to a maximum height', async () => {
    const { box, type } = await render();
    Object.defineProperty(box(), 'scrollHeight', { configurable: true, get: () => 96 });
    await type('one\ntwo\nthree\nfour');
    expect(box().style.height).toBe('96px');

    Object.defineProperty(box(), 'scrollHeight', { configurable: true, get: () => 900 });
    await type('very long');
    expect(box().style.height).toBe('200px');
  });

  it('can be focused by the page', async () => {
    const { fixture, box } = await render();

    fixture.componentInstance.focus();

    expect(document.activeElement).toBe(box());
  });
});
