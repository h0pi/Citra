import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '@core/auth/auth.service';
import { CurrentUser } from '@core/models/auth.models';
import { BotSummary } from '@core/models/bot.models';
import { Observable, of, throwError } from 'rxjs';

import { BotsApi } from './bots-api';
import { CurrentBot } from './current-bot';

function bot(id: string, name = id): BotSummary {
  return {
    id,
    name,
    slug: id,
    description: null,
    isPublic: false,
    widgetColor: '#6366F1',
    documentCount: 0,
    messageCount: 0,
    createdAt: '2026-09-26T10:00:00Z',
    updatedAt: '2026-09-26T10:00:00Z',
  };
}

function user(id: string): CurrentUser {
  return {
    id,
    email: `${id}@acme.test`,
    displayName: id,
    role: 'Admin',
    tenantId: 't1',
    tenantName: 'Acme',
  };
}

describe('CurrentBot', () => {
  const currentUser = signal<CurrentUser | null>(null);
  const list = vi.fn<() => Observable<BotSummary[]>>();

  beforeEach(() => {
    localStorage.clear();
    currentUser.set(null);
    list.mockReset().mockReturnValue(of([bot('a'), bot('b'), bot('c')]));
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { currentUser: currentUser.asReadonly() } },
        { provide: BotsApi, useValue: { list } },
      ],
    });
  });

  afterEach(() => localStorage.clear());

  function start(userId: string | null = 'u1') {
    const service = TestBed.inject(CurrentBot);
    currentUser.set(userId ? user(userId) : null);
    TestBed.tick();
    return service;
  }

  it('does nothing while nobody is logged in', () => {
    const service = start(null);
    expect(list).not.toHaveBeenCalled();
    expect(service.status()).toBe('idle');
    expect(service.bot()).toBeNull();
  });

  it('loads the bots after login and picks the first one', () => {
    const service = start();
    expect(service.status()).toBe('ready');
    expect(service.bots().map((b) => b.id)).toEqual(['a', 'b', 'c']);
    expect(service.bot()?.id).toBe('a');
  });

  it('remembers the choice per user', () => {
    localStorage.setItem('citra.currentBot.u1', 'c');
    localStorage.setItem('citra.currentBot.u2', 'b');

    expect(start('u1').bot()?.id).toBe('c');
  });

  it('saves a new choice and ignores ids that are not in the list', () => {
    const service = start();

    service.select('b');
    service.select('someone-elses-bot');

    expect(service.bot()?.id).toBe('b');
    expect(localStorage.getItem('citra.currentBot.u1')).toBe('b');
  });

  it('falls back to the first bot when the remembered one was deleted', () => {
    const service = start();
    service.select('c');

    list.mockReturnValue(of([bot('a'), bot('b')]));
    service.refresh();

    expect(service.bot()?.id).toBe('a');
    expect(localStorage.getItem('citra.currentBot.u1')).toBe('a');
  });

  it('has no current bot when the company has none', () => {
    list.mockReturnValue(of([]));
    const service = start();

    expect(service.status()).toBe('ready');
    expect(service.bot()).toBeNull();
    expect(localStorage.getItem('citra.currentBot.u1')).toBeNull();
  });

  it('can switch to a just-created bot', () => {
    const service = start();

    list.mockReturnValue(of([bot('a'), bot('b'), bot('c'), bot('new')]));
    service.refresh('new');

    expect(service.bot()?.id).toBe('new');
  });

  it('picks up renames on refresh', () => {
    const service = start();

    list.mockReturnValue(of([bot('a', 'Renamed'), bot('b'), bot('c')]));
    service.refresh();

    expect(service.bot()?.name).toBe('Renamed');
  });

  it('forgets everything on logout', () => {
    const service = start();

    currentUser.set(null);
    TestBed.tick();

    expect(service.bots()).toEqual([]);
    expect(service.bot()).toBeNull();
    expect(service.status()).toBe('idle');
  });

  it('does not reload when the same user gets a new token', () => {
    start();
    currentUser.set(user('u1')); // same id, new object (what a token refresh does)
    TestBed.tick();

    expect(list).toHaveBeenCalledTimes(1);
  });

  it('reports a failed load so the selector can offer a retry', () => {
    list.mockReturnValue(throwError(() => new Error('offline')));
    expect(start().status()).toBe('error');
  });
});
