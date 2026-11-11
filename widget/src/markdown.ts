/**
 * Shows a bot answer's Markdown as formatted text: paragraphs, line breaks, bullet and numbered lists, headings,
 * **bold**, *italic* and `code` (what answers use). Everything else stays as written.
 * The elements are built one by one and every piece of text goes in as text, never as HTML, so an answer can
 * never run code on the website.
 */
export function renderMarkdown(target: HTMLElement, markdown: string): void {
  target.replaceChildren();
  let paragraph = null as HTMLElement | null;
  let list = null as HTMLElement | null;

  for (const raw of markdown.replace(/\r/g, '').split('\n')) {
    const line = raw.trim();
    if (!line) {
      paragraph = list = null;
      continue;
    }

    const item = /^(?:([-*+])|\d+[.)])\s+(.*)$/.exec(line);
    if (item) {
      const tag = item[1] ? 'UL' : 'OL';
      if (list?.tagName !== tag) {
        list = document.createElement(tag);
        target.append(list);
      }
      paragraph = null;
      const entry = document.createElement('li');
      appendInline(entry, item[2]);
      list.append(entry);
      continue;
    }

    list = null;
    if (paragraph) {
      paragraph.append(document.createElement('br'));
    } else {
      paragraph = document.createElement('p');
      target.append(paragraph);
    }

    const heading = /^#{1,6}\s+(.*)$/.exec(line);
    if (heading) {
      const strong = document.createElement('strong');
      appendInline(strong, heading[1]);
      paragraph.append(strong);
    } else {
      appendInline(paragraph, line);
    }
  }
}

/** **bold**, *italic* and `code` inside a line. */
function appendInline(target: HTMLElement, text: string): void {
  for (const part of text.split(/(\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*)/)) {
    const tag = part.length > 4 && part.startsWith('**') && part.endsWith('**') ? 'strong'
      : part.length > 2 && part.startsWith('`') && part.endsWith('`') ? 'code'
      : part.length > 2 && part.startsWith('*') && part.endsWith('*') ? 'em'
      : null;
    if (!tag) {
      if (part) {
        target.append(part);
      }
      continue;
    }

    const element = document.createElement(tag);
    element.textContent = part.slice(tag === 'strong' ? 2 : 1, tag === 'strong' ? -2 : -1);
    target.append(element);
  }
}
