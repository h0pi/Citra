/** One row of GET /api/bots: enough for a card, without the long prompt texts. */
export interface BotSummary {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  isPublic: boolean;
  widgetColor: string;
  /** Suggested first questions, shown when a new chat starts. */
  starterQuestions: string[];
  documentCount: number;
  messageCount: number;
  createdAt: string;
  updatedAt: string;
}

/** A bot with all its settings (GET /api/bots/{id}). */
export interface Bot {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  systemPrompt: string | null;
  welcomeMessage: string;
  /** null = the app's default model. */
  model: string | null;
  temperature: number;
  isPublic: boolean;
  widgetColor: string;
  allowedDomains: string[];
  starterQuestions: string[];
  dailyMessageLimit: number | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateBotRequest {
  name: string;
  description: string | null;
}

export interface UpdateBotRequest {
  name: string;
  description: string | null;
  systemPrompt: string | null;
  welcomeMessage: string;
  model: string | null;
  temperature: number;
  widgetColor: string;
  starterQuestions: string[];
}

/** Same limits as the backend (Bot entity constants). */
export const BOT_LIMITS = {
  name: 100,
  description: 500,
  systemPrompt: 4000,
  welcomeMessage: 500,
  starterQuestions: 6,
  starterQuestion: 200,
} as const;

/** What GET /api/bots/{id}/search found (the search playground). */
export interface BotSearchResult {
  /** The question as searched (trimmed). */
  query: string;
  /** Tokens the question's embedding cost. */
  queryTokens: number;
  /** Passages found only by meaning, less similar than this, were left out (-1 = none). */
  minSimilarity: number;
  /** The same for passages containing the question's words (a lower bar). */
  minKeywordSimilarity: number;
  results: SearchResult[];
}

/** One passage a search found, with where it is and how it scored. */
export interface SearchResult {
  /** 1-based place in the combined ranking. */
  rank: number;
  chunkId: string;
  documentId: string;
  documentName: string;
  chunkIndex: number;
  pageNumber: number | null;
  heading: string | null;
  content: string;
  charStart: number;
  charEnd: number;
  /** Combined score of both searches (Reciprocal Rank Fusion). */
  score: number;
  /** Cosine similarity of meaning to the question (-1 to 1). */
  similarity: number | null;
  /** Full-text rank, when the question's words were found. */
  keywordScore: number | null;
  /** Place in the meaning search, if found there. */
  vectorRank: number | null;
  /** Place in the keyword search, if found there. */
  keywordRank: number | null;
  /** Overlapping neighbour chunks joined into this passage. */
  chunkCount: number;
}

/** Same limits as the backend (SearchBotQuery, IRetriever). */
export const SEARCH_LIMITS = {
  query: 1000,
  maxTopK: 50,
} as const;
