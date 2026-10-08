import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injectable,
  input,
  output,
  Signal,
  signal,
} from '@angular/core';
import { Citation } from '@core/models/chat.models';
import { MarkdownBlock, MarkdownInline, parseMarkdown } from './markdown';

/** What a citation chip needs to know about the source it links to. */
export type CitedSource = Pick<Citation, 'number' | 'documentName' | 'pageNumber'>;

/** A citation marker as shown: a chip for each number with a source, the number as text otherwise. */
interface MarkerPart {
  number: number;
  source: CitedSource | null;
}

/** The sources of the answer shown, shared with the pieces inside it (they are nested as deep as the text). */
@Injectable()
export class AnswerCitations {
  sources: Signal<ReadonlyMap<number, CitedSource>> = signal(new Map());
  pick: (number: number) => void = () => undefined;
}

/** Text inside a block: emphasis, code, links, line breaks and citation chips, nested as written. */
@Component({
  selector: 'app-markdown-inlines',
  templateUrl: './markdown-inlines.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Only the pieces belong in the page, not this element around them.
  host: { style: 'display: contents' },
})
export class MarkdownInlines {
  readonly content = input.required<MarkdownInline[]>();

  protected readonly citations = inject(AnswerCitations);

  /** A marker none of whose numbers has a source stays text as written (null); otherwise one part per number. */
  protected parts(numbers: number[]): MarkerPart[] | null {
    const sources = this.citations.sources();
    const parts = numbers.map((number) => ({ number, source: sources.get(number) ?? null }));
    return parts.some((part) => part.source) ? parts : null;
  }

  protected label(source: CitedSource): string {
    const page = source.pageNumber === null ? '' : `, page ${source.pageNumber}`;
    return `Source ${source.number}: ${source.documentName}${page}`;
  }
}

/** Paragraphs, lists, tables, code and quotes; lists and quotes hold blocks of their own. */
@Component({
  selector: 'app-markdown-blocks',
  templateUrl: './markdown-blocks.html',
  imports: [MarkdownInlines],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display: contents' },
})
export class MarkdownBlocks {
  readonly blocks = input.required<MarkdownBlock[]>();
}

/**
 * An answer written in Markdown, shown with the page's own elements (never as HTML), also while it is written.
 * Its citation markers ("[1]") become chips that show their source; markers without a source stay text.
 */
@Component({
  selector: 'app-markdown',
  template: '<app-markdown-blocks [blocks]="blocks()" />',
  styleUrl: './markdown-view.scss',
  imports: [MarkdownBlocks],
  providers: [AnswerCitations],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'markdown' },
})
export class MarkdownView {
  readonly text = input.required<string>();
  /** The sources the answer's markers link to. */
  readonly sources = input<CitedSource[]>([]);

  /** A citation chip was chosen: the number of its source. */
  readonly cite = output<number>();

  protected readonly blocks = computed(() => parseMarkdown(this.text()));

  constructor() {
    const citations = inject(AnswerCitations);
    citations.sources = computed(
      () => new Map(this.sources().map((source) => [source.number, source])),
    );
    citations.pick = (number) => this.cite.emit(number);
  }
}
