/** Who wrote a message. */
export type MessageRole = 'User' | 'Assistant';

/** Thumbs up / down on an answer. */
export type MessageFeedback = 'None' | 'Positive' | 'Negative';

/** One row of the chat list (GET /api/bots/{botId}/conversations). */
export interface ConversationSummary {
  id: string;
  botId: string;
  title: string;
  messageCount: number;
  createdAt: string;
  /** Last activity; the list is sorted by it, newest first. */
  updatedAt: string;
}

/** A chat with all its messages, oldest first (GET /api/conversations/{id}). */
export interface Conversation {
  id: string;
  botId: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  messages: ConversationMessage[];
}

/** A question or an answer. Answers also say whether the documents had the answer, their rating and sources. */
export interface ConversationMessage {
  id: string;
  role: MessageRole;
  content: string;
  createdAt: string;
  wasAnswered: boolean;
  feedback: MessageFeedback;
  citations: Citation[];
}

/** A source an answer cites as [number]; the links are null once the document was deleted. */
export interface Citation {
  number: number;
  chunkId: string | null;
  documentId: string | null;
  documentName: string;
  pageNumber: number | null;
  snippet: string;
  score: number;
}

/** A numbered passage an answer is written from (the `citations` stream event), cited as [number]. */
export interface AnswerSource {
  number: number;
  chunkId: string;
  documentId: string;
  documentName: string;
  pageNumber: number | null;
  heading: string | null;
  content: string;
  charStart: number;
  charEnd: number;
  score: number;
}

/** What POST /api/bots/{botId}/chat streams, in order: start, citations, token…, done. */
export type ChatStreamEvent =
  | { type: 'start'; conversationId: string; questionId: string; searchQuery: string }
  | { type: 'citations'; sources: AnswerSource[] }
  | { type: 'token'; text: string }
  | {
      type: 'done';
      messageId: string;
      content: string;
      wasAnswered: boolean;
      citedSources: number[];
    };
