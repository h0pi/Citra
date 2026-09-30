import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { DocumentsApi } from '@core/documents/documents-api';
import { CitraDocument } from '@core/models/document.models';
import { Observable, of, throwError } from 'rxjs';

import { AddUrlDialog, normalizeUrl } from './add-url-dialog';

const added: CitraDocument = {
  id: 'd9',
  botId: 'b1',
  fileName: 'acme.test/help',
  contentType: 'text/html',
  sizeBytes: 0,
  sourceType: 'Url',
  sourceUrl: 'https://acme.test/help',
  status: 'Uploaded',
  errorMessage: null,
  pageCount: 0,
  chunkCount: 0,
  createdAt: '2026-09-29T10:00:00Z',
  updatedAt: '2026-09-29T10:00:00Z',
  processedAt: null,
};

describe('normalizeUrl', () => {
  it('adds https:// when the scheme is missing and keeps full addresses', () => {
    expect(normalizeUrl('  acme.test/help ')).toBe('https://acme.test/help');
    expect(normalizeUrl('http://acme.test/a?b=1')).toBe('http://acme.test/a?b=1');
    expect(normalizeUrl('HTTPS://Acme.Test')).toBe('https://acme.test/');
    expect(normalizeUrl('acme.test:8080/help')).toBe('https://acme.test:8080/help');
  });

  it('refuses what is not a web address', () => {
    for (const value of [
      '',
      '   ',
      'hello',
      'ftp://acme.test/file',
      'javascript:alert(1)',
      'https://',
      'mailto:a@b.c',
      'https://user:secret@acme.test/',
      'data:text/html,hi',
    ]) {
      expect(normalizeUrl(value)).toBeNull();
    }
  });
});

describe('AddUrlDialog', () => {
  const addUrl = vi.fn<(botId: string, url: string) => Observable<CitraDocument>>();

  beforeEach(async () => {
    addUrl.mockReset();
    await TestBed.configureTestingModule({
      imports: [AddUrlDialog],
      providers: [{ provide: DocumentsApi, useValue: { addUrl } }],
    }).compileComponents();
  });

  async function open() {
    const fixture = TestBed.createComponent(AddUrlDialog);
    fixture.componentRef.setInput('botId', 'b1');
    fixture.componentInstance.visible.set(true);
    const emitted: CitraDocument[] = [];
    fixture.componentInstance.added.subscribe((d) => emitted.push(d));
    await fixture.whenStable();
    const input = document.querySelector<HTMLInputElement>('#document-url')!;
    const type = async (value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const submit = async () => {
      const button = [...document.querySelectorAll<HTMLButtonElement>('p-button button')].find(
        (b) => b.textContent?.includes('Add page'),
      )!;
      button.click();
      await fixture.whenStable();
    };
    return { fixture, emitted, type, submit, text: () => document.body.textContent ?? '' };
  }

  afterEach(() => document.querySelectorAll('.p-dialog-mask').forEach((m) => m.remove()));

  it('asks for an address', async () => {
    const { submit, text } = await open();

    await submit();

    expect(text()).toContain('Enter a web address.');
    expect(addUrl).not.toHaveBeenCalled();
  });

  it('refuses something that is not a web address', async () => {
    const { type, submit, text } = await open();

    await type('not a url');
    await submit();

    expect(text()).toContain('Enter a full web address, e.g. https://example.com/help.');
    expect(addUrl).not.toHaveBeenCalled();
  });

  it('adds the page (https:// added) and closes', async () => {
    addUrl.mockReturnValue(of(added));
    const { fixture, type, submit, emitted } = await open();

    await type('acme.test/help');
    await submit();

    expect(addUrl).toHaveBeenCalledWith('b1', 'https://acme.test/help');
    expect(emitted).toEqual([added]);
    expect(fixture.componentInstance.visible()).toBe(false);
  });

  it('shows the server’s reason under the field, and stays open', async () => {
    addUrl.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              title: 'Validation failed',
              errors: { url: ['This address points to a private network.'] },
            },
          }),
      ),
    );
    const { fixture, type, submit, text, emitted } = await open();

    await type('https://intranet.local.test');
    await submit();

    expect(text()).toContain('This address points to a private network.');
    expect(emitted).toEqual([]);
    expect(fixture.componentInstance.visible()).toBe(true);
  });

  it('shows other errors above the form', async () => {
    addUrl.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 409, error: { detail: 'This page was already added.' } }),
      ),
    );
    const { type, submit, text } = await open();

    await type('https://acme.test/help');
    await submit();

    expect(text()).toContain('This page was already added.');
  });
});
