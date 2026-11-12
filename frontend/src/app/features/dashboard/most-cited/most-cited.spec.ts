import { TestBed } from '@angular/core/testing';
import { CitedDocument } from '@core/models/analytics.models';

import { MostCited } from './most-cited';

describe('MostCited', () => {
  function render(documents: CitedDocument[]) {
    const fixture = TestBed.createComponent(MostCited);
    fixture.componentRef.setInput('documents', documents);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the documents numbered, with how many answers cited each', () => {
    const el = render([
      { documentId: 'd1', name: 'Returns.pdf', answers: 12 },
      { documentId: 'd2', name: 'Shipping.pdf', answers: 1 },
    ]);

    const rows = [...el.querySelectorAll('li')].map((li) =>
      [...li.querySelectorAll('span')].map((s) => s.textContent!.trim()).join(' '),
    );
    expect(rows).toEqual(['[1] Returns.pdf 12 answers', '[2] Shipping.pdf 1 answer']);
  });

  it('says so when nothing was cited', () => {
    const el = render([]);

    expect(el.querySelector('li')).toBeNull();
    expect(el.textContent).toContain('No answers cited a document in this period.');
  });
});
