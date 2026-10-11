import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '@env/environment';

import { AnalyticsApi } from './analytics-api';

describe('AnalyticsApi', () => {
  let api: AnalyticsApi;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api/analytics`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(AnalyticsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the unanswered questions and a company chat', () => {
    api.unansweredQuestions({ from: '2026-10-01', to: '2026-10-07', botId: 'b1' }).subscribe();
    api.conversation('c1').subscribe();

    http.expectOne(`${base}/unanswered-questions?from=2026-10-01&to=2026-10-07&botId=b1`);
    http.expectOne(`${base}/conversations/c1`);
  });

  it('loads the feedback summary', () => {
    api.feedback({ from: '2026-10-01', to: '2026-10-07', botId: null }).subscribe();

    http.expectOne(`${base}/feedback?from=2026-10-01&to=2026-10-07`);
  });

  it('lists the company chats by bot, channel and page', () => {
    api.conversations({ botId: null, channel: 'Widget', page: 2, pageSize: 20 }).subscribe();

    http.expectOne(`${base}/conversations?channel=Widget&page=2&pageSize=20`);
  });

  it('loads questions per day for a period', () => {
    api.messagesPerDay({ from: '2026-10-01', to: '2026-10-07', botId: null }).subscribe();

    http.expectOne(`${base}/messages-per-day?from=2026-10-01&to=2026-10-07`);
  });

  it('loads the stats for a period and bot, leaving out "all bots"', () => {
    api.stats({ from: '2026-10-01', to: '2026-10-07', botId: 'b1' }).subscribe();
    api.stats({ from: '2026-10-01', to: '2026-10-07', botId: null }).subscribe();

    expect(
      http.expectOne(`${base}/stats?from=2026-10-01&to=2026-10-07&botId=b1`).request.method,
    ).toBe('GET');
    http.expectOne(`${base}/stats?from=2026-10-01&to=2026-10-07`);
  });
});
