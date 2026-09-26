/** One row of GET /api/bots: enough for a card, without the long prompt texts. */
export interface BotSummary {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  isPublic: boolean;
  widgetColor: string;
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
}

/** Same limits as the backend (Bot entity constants). */
export const BOT_LIMITS = {
  name: 100,
  description: 500,
  systemPrompt: 4000,
  welcomeMessage: 500,
} as const;
