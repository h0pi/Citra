import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ButtonModule } from 'primeng/button';

/** Same limit as the API (SendMessageCommand.QuestionMaxLength). */
export const QUESTION_MAX_LENGTH = 4000;

/** The character counter appears once a question gets this long. */
export const COUNTER_FROM = 3500;

/** The box grows with the question up to this height, then scrolls. */
const MAX_HEIGHT_PX = 200;

/**
 * Where a question is typed. Enter sends, Shift+Enter starts a new line. While an answer is being written
 * (`busy`) nothing can be sent, but the next question can already be typed; the send button becomes a stop
 * button then.
 */
@Component({
  selector: 'app-message-composer',
  templateUrl: './message-composer.html',
  styleUrl: './message-composer.scss',
  imports: [ButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MessageComposer {
  /** An answer is on its way: sending waits until it is done. */
  readonly busy = input(false);
  readonly placeholder = input('Ask a question…');

  /** The question to send, trimmed. */
  readonly send = output<string>();
  /** Stop the answer being written. */
  readonly stop = output<void>();

  protected readonly maxLength = QUESTION_MAX_LENGTH;
  protected readonly text = signal('');
  protected readonly showCounter = computed(() => this.text().length >= COUNTER_FROM);
  protected readonly canSend = computed(() => {
    const question = this.text().trim();
    return !this.busy() && question.length > 0 && question.length <= QUESTION_MAX_LENGTH;
  });

  private readonly box = viewChild.required<ElementRef<HTMLTextAreaElement>>('box');

  /** Puts the cursor in the box (e.g. after starting a new chat). */
  focus(): void {
    this.box().nativeElement.focus();
  }

  protected onInput(value: string): void {
    this.text.set(value);
    this.resize();
  }

  protected onKeydown(event: KeyboardEvent): void {
    // Enter while an input method (e.g. for Chinese or accents) is composing a character confirms it instead.
    if (event.key !== 'Enter' || event.shiftKey || event.isComposing) {
      return;
    }

    event.preventDefault(); // no new line
    this.submit();
  }

  protected submit(): void {
    if (!this.canSend()) {
      return;
    }

    this.send.emit(this.text().trim());
    this.text.set('');
    const box = this.box().nativeElement;
    box.value = '';
    this.resize();
    box.focus();
  }

  protected stopAnswer(): void {
    this.stop.emit();
    // The stop button goes away: the cursor waits in the box instead.
    this.box().nativeElement.focus();
  }

  /** Grows the box with its text (one line when empty), up to MAX_HEIGHT_PX. */
  private resize(): void {
    const box = this.box().nativeElement;
    box.style.height = 'auto';
    box.style.height = `${Math.min(box.scrollHeight, MAX_HEIGHT_PX)}px`;
  }
}
