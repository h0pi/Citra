import { TestBed } from '@angular/core/testing';
import { CitedSource, MarkdownView } from './markdown-view';

describe('MarkdownView', () => {
  async function render(text: string, sources: CitedSource[] = []) {
    await TestBed.configureTestingModule({ imports: [MarkdownView] }).compileComponents();
    const fixture = TestBed.createComponent(MarkdownView);
    fixture.componentRef.setInput('text', text);
    fixture.componentRef.setInput('sources', sources);
    const cited: number[] = [];
    fixture.componentInstance.cite.subscribe((number) => cited.push(number));
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    const update = async (next: string, nextSources = sources) => {
      fixture.componentRef.setInput('text', next);
      fixture.componentRef.setInput('sources', nextSources);
      await fixture.whenStable();
    };
    const chips = () => [...el.querySelectorAll<HTMLButtonElement>('button.citation')];
    return { el, update, chips, cited };
  }

  const handbook: CitedSource = { number: 1, documentName: 'Handbook.pdf', pageNumber: 3 };
  const policy: CitedSource = { number: 2, documentName: 'Leave policy.md', pageNumber: null };

  it('shows emphasis, code and line breaks with their own elements, without extra spaces', async () => {
    const { el } = await render('You get **25 days**, *paid*, ~~not~~ `per year` [1].\nAsk HR.');

    const paragraph = el.querySelector('p')!;
    expect(paragraph.textContent).toBe('You get 25 days, paid, not per year [1].Ask HR.');
    expect(paragraph.querySelector('strong')!.textContent).toBe('25 days');
    expect(paragraph.querySelector('em')!.textContent).toBe('paid');
    expect(paragraph.querySelector('del')!.textContent).toBe('not');
    expect(paragraph.querySelector('code')!.textContent).toBe('per year');
    expect(paragraph.querySelectorAll('br')).toHaveLength(1);
  });

  it('shows lists, nested lists, numbered lists from their first number and task lists', async () => {
    const { el } = await render(
      '- Apply\n  - online\n- Wait\n\n3. third\n4. fourth\n\n- [x] done\n- [ ] open',
    );

    const [bullets, numbers, tasks] = [...el.querySelectorAll('ul, ol')].filter(
      (list) => !list.closest('li'),
    );
    expect(bullets.tagName).toBe('UL');
    expect([...bullets.children].map((li) => li.querySelector('p')!.textContent)).toEqual([
      'Apply',
      'Wait',
    ]);
    expect(bullets.querySelector('li ul li')!.textContent).toBe('online');
    expect(numbers.tagName).toBe('OL');
    expect(numbers.getAttribute('start')).toBe('3');
    expect(numbers.querySelectorAll('li')).toHaveLength(2);
    const boxes = [...tasks.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
    expect(boxes.map((box) => [box.checked, box.disabled])).toEqual([
      [true, true],
      [false, true],
    ]);
    expect([...tasks.querySelectorAll('li')].map((li) => li.textContent!.trim())).toEqual([
      'done',
      'open',
    ]);
  });

  it('numbers a list from 1 without a start attribute', async () => {
    const { el } = await render('1. one\n2. two');

    expect(el.querySelector('ol')!.hasAttribute('start')).toBe(false);
  });

  it('shows tables with headers and alignment, scrollable on their own', async () => {
    const { el } = await render('| Plan | Days |\n|:--|--:|\n| Basic | **20** |');

    const region = el.querySelector('.table-scroll')!;
    expect(region.getAttribute('role')).toBe('region');
    expect(region.getAttribute('tabindex')).toBe('0');
    const headers = [...region.querySelectorAll('thead th')] as HTMLElement[];
    expect(
      headers.map((th) => [th.textContent!.trim(), th.getAttribute('scope'), th.style.textAlign]),
    ).toEqual([
      ['Plan', 'col', 'left'],
      ['Days', 'col', 'right'],
    ]);
    const cells = [...region.querySelectorAll('tbody td')] as HTMLElement[];
    expect(cells.map((td) => [td.textContent!.trim(), td.style.textAlign])).toEqual([
      ['Basic', 'left'],
      ['20', 'right'],
    ]);
    expect(cells[1].querySelector('strong')).not.toBeNull();
  });

  it('shows code blocks exactly, headings, quotes and lines', async () => {
    const { el } = await render('## Steps\n\n```html\n<b>a</b>\n  indented\n```\n\n> Note\n\n---');

    expect(el.querySelector('p.heading')!.textContent).toBe('Steps');
    expect(el.querySelectorAll('h1, h2, h3, h4, h5, h6')).toHaveLength(0);
    expect(el.querySelector('pre code')!.textContent).toBe('<b>a</b>\n  indented');
    expect(el.querySelector('pre')!.getAttribute('tabindex')).toBe('0');
    expect(el.querySelector('blockquote p')!.textContent).toBe('Note');
    expect(el.querySelector('hr')).not.toBeNull();
  });

  it('opens web links in a new tab and never makes a script link', async () => {
    const { el } = await render(
      '[Site](https://example.com) [Bad](javascript:alert(1)) ![Logo](https://x.test/a.png)',
    );

    const links = [...el.querySelectorAll('a')];
    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('https://example.com');
    expect(links[0].getAttribute('target')).toBe('_blank');
    expect(links[0].getAttribute('rel')).toBe('noopener noreferrer');
    expect(links[0].textContent).toBe('Site');
    expect(el.querySelector('img')).toBeNull();
    expect(el.textContent).toContain('Bad');
    expect(el.textContent).toContain('Logo');
  });

  it('shows HTML from the answer as text, so it cannot run or change the page', async () => {
    const attack =
      '<script>window.hacked = true</script>\n\n<img src=x onerror="window.hacked = true"> <iframe src="https://evil.test"></iframe> **<a href="javascript:alert(1)">click</a>**';
    const { el } = await render(attack);

    expect(el.querySelectorAll('script, img, iframe, a, [onerror]')).toHaveLength(0);
    expect(el.textContent).toContain('<script>window.hacked = true</script>');
    expect(el.textContent).toContain('<img src=x onerror="window.hacked = true">');
    expect(el.querySelector('strong')!.textContent).toBe('<a href="javascript:alert(1)">click</a>');
    expect((window as { hacked?: boolean }).hacked).toBeUndefined();
  });

  it('formats the answer as it is written', async () => {
    const { el, update } = await render('| Plan | Days |');
    expect(el.querySelector('table')).toBeNull();
    expect(el.textContent).toContain('| Plan | Days |');

    await update('| Plan | Days |\n|--|--|\n| Basic | 20 |');
    expect(el.querySelectorAll('tbody tr')).toHaveLength(1);

    await update('| Plan | Days |\n|--|--|\n| Basic | 20 |\n\nThat is **all');
    expect(el.querySelector('strong')).toBeNull();
    await update('| Plan | Days |\n|--|--|\n| Basic | 20 |\n\nThat is **all**.');
    expect(el.querySelector('strong')!.textContent).toBe('all');
  });

  it('shows citation markers with a source as chips that name it', async () => {
    const { el, chips } = await render('Vacation [1]. Carry-over [1][2].', [handbook, policy]);

    expect(
      chips().map((chip) => [chip.textContent, chip.type, chip.getAttribute('aria-label')]),
    ).toEqual([
      ['1', 'button', 'Source 1: Handbook.pdf, page 3'],
      ['1', 'button', 'Source 1: Handbook.pdf, page 3'],
      ['2', 'button', 'Source 2: Leave policy.md'],
    ]);
    expect(chips()[2].title).toBe('Source 2: Leave policy.md');
    expect(el.querySelector('p')!.textContent).toBe('Vacation 1. Carry-over 12.');
  });

  it('tells which source was chosen, also from deep inside the answer', async () => {
    const { chips, cited } = await render('- **Days [2]**\n\n| A |\n|--|\n| x [1] |', [
      handbook,
      policy,
    ]);

    chips()[0].click();
    chips()[1].click();

    expect(cited).toEqual([2, 1]);
  });

  it('keeps markers without a source as text', async () => {
    const { el, chips } = await render('Known [1, 9]. Unknown [9]. Both unknown [8, 9].', [
      handbook,
    ]);

    expect(chips().map((chip) => chip.textContent)).toEqual(['1']);
    expect(el.querySelector('p')!.textContent).toBe(
      'Known 1[9]. Unknown [9]. Both unknown [8, 9].',
    );
  });

  it('makes no chips when the answer has no sources, or in code and links', async () => {
    const { el, chips, update } = await render('Not found [1].');
    expect(chips()).toHaveLength(0);
    expect(el.textContent).toBe('Not found [1].');

    await update('`x[1]` [see [1]](https://example.com)\n\n```\nlist[1]\n```', [handbook]);
    expect(chips()).toHaveLength(0);
    expect(el.querySelector('a')!.textContent).toBe('see [1]');
  });

  it('turns markers into chips once their sources arrive', async () => {
    const { chips, update } = await render('Vacation [1]');
    expect(chips()).toHaveLength(0);

    await update('Vacation [1]', [handbook]);
    expect(chips()).toHaveLength(1);
  });
});
