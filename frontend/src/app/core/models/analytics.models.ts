/** The days a dashboard query covers (UTC dates, both included), for one bot or all (no botId). */
export interface AnalyticsPeriod {
  from: string;
  to: string;
  botId: string | null;
}

/** GET /api/analytics/stats: the dashboard's headline numbers. */
export interface DashboardStats {
  conversations: number;
  questions: number;
  /** Share of answers found in the documents, 0–100. */
  answeredPercent: number;
  averageLatencyMs: number;
  /** Answers plus embeddings. */
  totalCostUsd: number;
}

/** GET /api/analytics/messages-per-day: one entry per day of the period. */
export interface DayCount {
  /** UTC date, yyyy-MM-dd. */
  date: string;
  questions: number;
}

/** GET /api/analytics/unanswered-questions: a question the documents could not answer ("knowledge gap"). */
export interface UnansweredQuestion {
  conversationId: string;
  botId: string;
  question: string;
  /** Admin app or website widget. */
  channel: 'Admin' | 'Widget';
  askedAt: string;
}

/** GET /api/analytics/most-cited-documents: a document and how many answers cited it. */
export interface CitedDocument {
  documentId: string;
  name: string;
  answers: number;
}

/** GET /api/analytics/feedback: how people rated the period's answers. */
export interface FeedbackSummary {
  positive: number;
  negative: number;
  /** Badly rated answers, newest first (at most 50). */
  badAnswers: RatedAnswer[];
}

export interface RatedAnswer {
  conversationId: string;
  botId: string;
  question: string;
  answer: string;
  answeredAt: string;
}

/** Admin app or website widget. */
export type ConversationChannel = 'Admin' | 'Widget';

/** GET /api/analytics/conversations: one chat of the company (owners and admins). */
export interface CompanyConversation {
  id: string;
  botId: string;
  title: string;
  channel: ConversationChannel;
  messageCount: number;
  updatedAt: string;
}

export interface CompanyConversationsQuery {
  botId: string | null;
  channel: ConversationChannel | null;
  page: number;
  pageSize: number;
}
