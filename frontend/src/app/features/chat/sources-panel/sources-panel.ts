import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import { Citation } from '@core/models/chat.models';
import { ButtonModule } from 'primeng/button';

/** Longer passages show this much first, with "Show more". */
export const SNIPPET_PREVIEW_LENGTH = 320;

/**
 * The passages an answer was based on: number (as in the answer's citation chips), document, page and text.
 * The chosen source is picked out, scrolled to and focused, so a chip leads straight to its passage. On small
 * screens the page shows this as a panel over the chat, with a close button.
 */
@Component({
  selector: 'app-sources-panel',
  imports: [ButtonModule],
  templateUrl: './sources-panel.html',
  styleUrl: './sources-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SourcesPanel {
  readonly sources = input<Citation[]>([]);
  /** The number of the source whose citation was chosen. */
  readonly selected = input<number | null>(null);
  readonly closed = output<void>();
  /** Open the original document of a source. */
  readonly open = output<Citation>();

  /** Which sources are shown: the same ones again (a chip chosen again) keep their passages as they are. */
  private readonly shownSources = computed(() =>
    this.sources()
      .map((source) => `${source.number}:${source.chunkId}`)
      .join('|'),
  );

  /** Numbers of the passages shown in full; all are short again when other sources are shown. */
  protected readonly expanded = linkedSignal<string, ReadonlySet<number>>({
    source: this.shownSources,
    computation: () => new Set(),
  });

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    // Each time a source is chosen (the page passes a new list then, also for the same chip again).
    afterRenderEffect(() => {
      const number = this.selected();
      this.sources();
      if (number === null) {
        return;
      }
      const item = host.querySelector<HTMLElement>(`[data-number="${number}"]`);
      item?.scrollIntoView({ block: 'nearest' });
      item?.focus({ preventScroll: true });
    });
  }

  /** The original can be shown while the document still exists. */
  protected canOpen(source: Citation): boolean {
    return source.documentId !== null;
  }

  protected isLong(source: Citation): boolean {
    return source.snippet.length > SNIPPET_PREVIEW_LENGTH;
  }

  /** The passage, or its start (cut at a word) while it is not shown in full. */
  protected text(source: Citation): string {
    if (!this.isLong(source) || this.expanded().has(source.number)) {
      return source.snippet;
    }
    const start = source.snippet.slice(0, SNIPPET_PREVIEW_LENGTH);
    const space = start.lastIndexOf(' ');
    return (space > 0 ? start.slice(0, space) : start).trimEnd() + '…';
  }

  protected toggle(number: number): void {
    this.expanded.update((expanded) => {
      const next = new Set(expanded);
      if (!next.delete(number)) {
        next.add(number);
      }
      return next;
    });
  }
}
