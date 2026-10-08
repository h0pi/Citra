import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import { environment } from '@env/environment';

import { CONVERSATIONS_PAGE_SIZE, ChatApi } from './chat-api';

describe('ChatApi', () => {
  let api: ChatApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ChatApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists a bot’s chats a page at a time, showing errors on the page instead of a toast', () => {
    api.conversations('b/1').subscribe();
    api.conversations('b1', 3, 10).subscribe();

    const first = http.expectOne(
      (r) => r.url === `${environment.apiUrl}/api/bots/b%2F1/conversations`,
    );
    expect(first.request.method).toBe('GET');
    expect(first.request.params.get('page')).toBe('1');
    expect(first.request.params.get('pageSize')).toBe(String(CONVERSATIONS_PAGE_SIZE));
    expect(first.request.context.get(SKIP_ERROR_TOAST)).toBe(true);

    const third = http.expectOne(
      (r) => r.url === `${environment.apiUrl}/api/bots/b1/conversations`,
    );
    expect(third.request.params.get('page')).toBe('3');
    expect(third.request.params.get('pageSize')).toBe('10');
  });

  it('rates an answer, showing errors as a toast', () => {
    api.rate('m/1', 'Positive').subscribe();

    const request = http.expectOne(`${environment.apiUrl}/api/messages/m%2F1/feedback`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ feedback: 'Positive' });
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(false);
  });

  it('renames and deletes a chat', () => {
    api.rename('c/1', 'Vacation').subscribe();
    api.delete('c/1').subscribe();

    const rename = http.expectOne(`${environment.apiUrl}/api/conversations/c%2F1/title`);
    expect(rename.request.method).toBe('PUT');
    expect(rename.request.body).toEqual({ title: 'Vacation' });
    const remove = http.expectOne(
      (req) =>
        req.method === 'DELETE' && req.url === `${environment.apiUrl}/api/conversations/c%2F1`,
    );
    expect(remove.request.body).toBeNull();
  });

  it('loads one chat with its messages', () => {
    api.conversation('c/1').subscribe();

    const request = http.expectOne(`${environment.apiUrl}/api/conversations/c%2F1`);
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
  });
});
