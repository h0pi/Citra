import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { BotsApi } from '@core/bots/bots-api';
import { BotSearchResult, SearchResult } from '@core/models/bot.models';
import { Observable, Subject, of, throwError } from 'rxjs';

import { DEFAULT_TOP_K, SearchPlayground } from './search-playground';

function result(overrides: Partial<SearchResult> = {}): SearchResult {
  return {
    rank: 1,
    chunkId: 'c1',
    documentId: 'd1',
    documentName: 'Handbook.pdf',
    chunkIndex: 3,
    pageNumber: 4,
    heading: 'Benefits › Vacation',
    content: 'Employees get 25 days of paid vacation.',
    charStart: 0,
    charEnd: 39,
    score: 0.0325,
    similarity: 0.58,
    keywordScore: 0.0608,
    vectorRank: 1,
    keywordRank: 2,
    chunkCount: 1,
    ...overrides,
  };
}

function found(results: SearchResult[], overrides: Partial<BotSearchResult> = {}): BotSearchResult {
  return {
    query: 'How many vacation days?',
    queryTokens: 7,
    minSimilarity: 0.25,
    minKeywordSimilarity: 0.15,
    results,
    ...overrides,
  };
}

describe('SearchPlayground', () => {
  const search = vi.fn<(id: string, query: string, topK: number) => Observable<BotSearchResult>>();

  beforeAll(() => {
    // jsdom has no matchMedia; PrimeNG's select overlay asks it about the screen size.
    window.matchMedia ??= ((query: string) =>
      ({
        matches: false,
        media: query,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
      }) as unknown as MediaQueryList) as typeof window.matchMedia;
  });

  beforeEach(async () => {
    search.mockReset().mockReturnValue(of(found([result()])));
    await TestBed.configureTestingModule({
      imports: [SearchPlayground],
      providers: [{ provide: BotsApi, useValue: { search } }],
    }).compileComponents();
  });

  async function render(botId = 'b1') {
    const fixture = TestBed.createComponent(SearchPlayground);
    fixture.componentRef.setInput('botId', botId);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const query = () => el.querySelector<HTMLInputElement>('#playground-query')!;
    const searchButton = () =>
      el.querySelector<HTMLButtonElement>('p-button[type="submit"] button, button[type="submit"]')!;
    const type = async (value: string) => {
      query().value = value;
      query().dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };
    const button = (label: string) =>
      [...el.querySelectorAll<HTMLButtonElement>('p-button button')].find((b) =>
        b.textContent?.includes(label),
      );
    const results = () => [...el.querySelectorAll<HTMLElement>('li.result')];
    return { fixture, el, query, searchButton, type, submit, button, results };
  }

  it('can search only once a question is typed', async () => {
    const { searchButton, type, submit } = await render();

    expect(searchButton().disabled).toBe(true);
    await submit();
    expect(search).not.toHaveBeenCalled();

    await type('   ');
    expect(searchButton().disabled).toBe(true);

    await type('How many vacation days?');
    expect(searchButton().disabled).toBe(false);
  });

  it('searches this bot with the trimmed question and 8 results by default', async () => {
    const { type, submit } = await render('b7');

    await type('  How many vacation days?  ');
    await submit();

    expect(search).toHaveBeenCalledExactlyOnceWith('b7', 'How many vacation days?', DEFAULT_TOP_K);
    expect(DEFAULT_TOP_K).toBe(8);
  });

  it('sends the chosen number of results', async () => {
    const { fixture, type, submit } = await render();
    (fixture.componentInstance as unknown as { topK: { set(v: number): void } }).topK.set(20);

    await type('vacation');
    await submit();

    expect(search).toHaveBeenCalledWith('b1', 'vacation', 20);
  });

  it('shows every passage with where it is and how it scored', async () => {
    search.mockReturnValue(
      of(
        found([
          result(),
          result({
            rank: 2,
            chunkId: 'c2',
            documentName: 'Policies.docx',
            pageNumber: null,
            heading: null,
            content: 'Leave requests go to your manager.',
            similarity: 0.41,
            keywordScore: null,
            keywordRank: null,
            vectorRank: 3,
            score: 0.0159,
            chunkCount: 2,
          }),
        ]),
      ),
    );
    const { el, type, submit, results } = await render();

    await type('vacation');
    await submit();

    expect(el.querySelector('.summary')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      "2 passages for “How many vacation days?” · 7 tokens · minimum similarity 0.25, 0.15 with the question's words",
    );
    const [first, second] = results();
    const text = (item: HTMLElement) => item.textContent!.replace(/\s+/g, ' ');
    const score = (item: HTMLElement, label: string) =>
      [...item.querySelectorAll('dt')]
        .find((dt) => dt.textContent?.trim() === label)!
        .nextElementSibling!.textContent!.replace(/\s+/g, ' ')
        .trim();
    expect(text(first)).toContain('#1');
    expect(text(first)).toContain('Handbook.pdf');
    expect(text(first)).toContain('Page 4');
    expect(text(first)).toContain('Benefits › Vacation');
    expect(score(first, 'Similarity')).toBe('0.580');
    expect(score(first, 'Meaning search')).toBe('#1');
    expect(score(first, 'Keyword search')).toBe('#2 (0.061)');
    expect(score(first, 'Combined')).toBe('0.0325');
    expect(first.querySelector('.content')!.textContent).toBe(
      'Employees get 25 days of paid vacation.',
    );
    expect(first.querySelector('p-tag')).toBeNull();

    expect(text(second)).toContain('Policies.docx');
    expect(text(second)).not.toContain('Page');
    expect(score(second, 'Meaning search')).toBe('#3');
    expect(score(second, 'Keyword search')).toBe('no matching words');
    expect(score(second, 'Similarity')).toBe('0.410');
    expect(second.querySelector('p-tag')?.textContent).toContain('2 chunks joined');
  });

  it('shows the similarity as a bar, empty for negative similarities', async () => {
    search.mockReturnValue(
      of(
        found([result({ similarity: 0.58 }), result({ rank: 2, chunkId: 'c2', similarity: -0.1 })]),
      ),
    );
    const { fixture, type, submit } = await render();
    const percent = (r: SearchResult) =>
      (
        fixture.componentInstance as unknown as { similarityPercent(r: SearchResult): number }
      ).similarityPercent(r);

    await type('vacation');
    await submit();

    expect(percent(result({ similarity: 0.58 }))).toBe(58);
    expect(percent(result({ similarity: -0.1 }))).toBe(0);
    expect(percent(result({ similarity: null }))).toBe(0);
  });

  it('says so when nothing close enough was found, and when there is no minimum', async () => {
    search.mockReturnValue(of(found([], { minSimilarity: -1, minKeywordSimilarity: -1 })));
    const { el, type, submit } = await render();

    await type('pizza recipe');
    await submit();

    expect(el.textContent).toContain('Nothing close enough was found');
    expect(el.querySelector('.summary')?.textContent).toContain('0 passages');
    expect(el.querySelector('.summary')?.textContent).toContain('no minimum similarity');
  });

  it('writes 1 passage, not 1 passages', async () => {
    const { el, type, submit } = await render();

    await type('vacation');
    await submit();

    expect(el.querySelector('.summary')?.textContent?.replace(/\s+/g, ' ')).toContain(
      ' 1 passage for',
    );
  });

  it('explains that the AI service is down, and tries the same search again', async () => {
    search
      .mockReturnValueOnce(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 503,
              error: {
                status: 503,
                title: 'The AI service is not available right now.',
                detail: 'Please try again in a minute.',
              },
            }),
        ),
      )
      .mockReturnValueOnce(of(found([result()])));
    const { el, type, submit, button, results } = await render();

    await type('vacation');
    await submit();
    expect(el.textContent).toContain(
      'The AI service is not available right now. Please try again in a minute.',
    );

    await type('something else'); // the retry repeats the search that failed
    button('Try again')!.click();
    await new Promise((resolve) => setTimeout(resolve));

    expect(search).toHaveBeenLastCalledWith('b1', 'vacation', 8);
    expect(results()).toHaveLength(1);
  });

  it('shows the reason when the server refuses the question', async () => {
    search.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { status: 400, errors: { query: ['Query must be 1000 characters or fewer.'] } },
          }),
      ),
    );
    const { el, type, submit } = await render();

    await type('vacation');
    await submit();

    expect(el.textContent).toContain('Query must be 1000 characters or fewer.');
  });

  it('shows only the newest search when an older answer arrives late', async () => {
    const slow = new Subject<BotSearchResult>();
    search
      .mockReturnValueOnce(slow)
      .mockReturnValueOnce(of(found([result({ content: 'newest' })], { query: 'second' })));
    const { el, type, submit, results } = await render();

    await type('first');
    await submit();
    await type('second');
    await submit();
    slow.next(found([result({ content: 'stale' })], { query: 'first' }));
    await new Promise((resolve) => setTimeout(resolve));

    expect(results().map((r) => r.querySelector('.content')!.textContent)).toEqual(['newest']);
    expect(el.querySelector('.summary')?.textContent).toContain('second');
  });

  it('starts over when another bot is chosen, ignoring its running search', async () => {
    const slow = new Subject<BotSearchResult>();
    const { fixture, el, type, submit, results } = await render();
    await type('vacation');
    await submit();
    expect(results()).toHaveLength(1);

    search.mockReturnValue(slow);
    await submit();
    fixture.componentRef.setInput('botId', 'b2');
    await fixture.whenStable();
    slow.next(found([result()]));
    await fixture.whenStable();

    expect(results()).toHaveLength(0);
    expect(el.querySelector('.summary')).toBeNull();
    expect(el.textContent).toContain('Results show the document');
  });

  it('clips long passages until opened', async () => {
    const long = 'A sentence about vacation. '.repeat(20);
    search.mockReturnValue(
      of(found([result({ content: long }), result({ rank: 2, chunkId: 'c2', content: 'Short.' })])),
    );
    const { fixture, type, submit, results } = await render();

    await type('vacation');
    await submit();
    const content = () => results()[0].querySelector('.content')!;
    const toggle = () => results()[0].querySelector<HTMLButtonElement>('button.toggle')!;
    expect(content().classList).toContain('clipped');
    expect(toggle().getAttribute('aria-expanded')).toBe('false');

    toggle().click();
    await fixture.whenStable();
    expect(content().classList).not.toContain('clipped');
    expect(toggle().textContent).toContain('Show less');
    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(results()[1].querySelector('button.toggle')).toBeNull();
  });

  it('shows new results clipped again, even passages opened before', async () => {
    const long = 'A sentence about vacation. '.repeat(20);
    search.mockReturnValue(of(found([result({ content: long })])));
    const { fixture, type, submit, results } = await render();
    await type('vacation');
    await submit();
    results()[0].querySelector<HTMLButtonElement>('button.toggle')!.click();
    await fixture.whenStable();
    expect(results()[0].querySelector('.content')!.classList).not.toContain('clipped');

    await submit();

    expect(results()[0].querySelector('.content')!.classList).toContain('clipped');
  });

  it('limits the question to 1000 characters like the server', async () => {
    const { query, searchButton, fixture } = await render();
    (fixture.componentInstance as unknown as { query: { set(v: string): void } }).query.set(
      'a'.repeat(1001),
    );
    await fixture.whenStable();

    expect(query().getAttribute('maxlength')).toBe('1000');
    expect(searchButton().disabled).toBe(true);
  });
});
