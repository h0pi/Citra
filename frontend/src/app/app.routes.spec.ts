import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UrlSegment, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { chatMatcher } from './app.routes';

@Component({ template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class ChatStub {
  readonly conversationId = input<string>();
}

const segments = (...paths: string[]) => paths.map((path) => new UrlSegment(path, {}));

describe('chatMatcher', () => {
  it('matches /chat and /chat/{id}, giving the id as conversationId', () => {
    expect(chatMatcher(segments('chat'))).toEqual({ consumed: segments('chat') });

    const one = chatMatcher(segments('chat', 'c1'));
    expect(one?.consumed).toEqual(segments('chat', 'c1'));
    expect(one?.posParams?.['conversationId'].path).toBe('c1');
  });

  it('matches nothing else', () => {
    expect(chatMatcher(segments())).toBeNull();
    expect(chatMatcher(segments('chats'))).toBeNull();
    expect(chatMatcher(segments('bots', 'chat'))).toBeNull();
    expect(chatMatcher(segments('chat', 'c1', 'x'))).toBeNull();
  });

  it('keeps the same page between /chat and /chat/{id}, updating its conversationId', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ matcher: chatMatcher, component: ChatStub }], withComponentInputBinding()),
      ],
    });
    const harness = await RouterTestingHarness.create();

    const page = await harness.navigateByUrl('/chat', ChatStub);
    expect(page.conversationId()).toBeUndefined();

    expect(await harness.navigateByUrl('/chat/c1', ChatStub)).toBe(page);
    expect(page.conversationId()).toBe('c1');

    expect(await harness.navigateByUrl('/chat/c2', ChatStub)).toBe(page);
    expect(page.conversationId()).toBe('c2');

    expect(await harness.navigateByUrl('/chat', ChatStub)).toBe(page);
    expect(page.conversationId()).toBeUndefined();
  });
});
