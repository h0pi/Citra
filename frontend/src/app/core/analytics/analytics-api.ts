import { Injectable, inject } from '@angular/core';
import {
  AnalyticsPeriod,
  DashboardStats,
  DayCount,
  FeedbackSummary,
  UnansweredQuestion,
} from '@core/models/analytics.models';
import { Conversation } from '@core/models/chat.models';
import { ApiService } from '@core/services/api.service';
import { Observable } from 'rxjs';

/** HTTP calls for /api/analytics (the dashboard). */
@Injectable({ providedIn: 'root' })
export class AnalyticsApi {
  private readonly api = inject(ApiService);

  stats(period: AnalyticsPeriod): Observable<DashboardStats> {
    return this.api.get<DashboardStats>('/api/analytics/stats', { ...period });
  }

  messagesPerDay(period: AnalyticsPeriod): Observable<DayCount[]> {
    return this.api.get<DayCount[]>('/api/analytics/messages-per-day', { ...period });
  }

  unansweredQuestions(period: AnalyticsPeriod): Observable<UnansweredQuestion[]> {
    return this.api.get<UnansweredQuestion[]>('/api/analytics/unanswered-questions', { ...period });
  }

  feedback(period: AnalyticsPeriod): Observable<FeedbackSummary> {
    return this.api.get<FeedbackSummary>('/api/analytics/feedback', { ...period });
  }

  /** Any chat of the company, read-only (owners and admins). */
  conversation(id: string): Observable<Conversation> {
    return this.api.get<Conversation>(`/api/analytics/conversations/${encodeURIComponent(id)}`);
  }
}
