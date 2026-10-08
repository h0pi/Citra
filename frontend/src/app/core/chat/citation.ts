import { AnswerSource, Citation } from '@core/models/chat.models';

/** Same as the API (MessageCitation.SnippetMaxLength). */
export const CITATION_SNIPPET_MAX_LENGTH = 2000;

/** A source of an answer being written, as the chat shows it once the answer is saved (and after a reload). */
export function toCitation(source: AnswerSource): Citation {
  return {
    number: source.number,
    chunkId: source.chunkId,
    documentId: source.documentId,
    documentName: source.documentName,
    pageNumber: source.pageNumber,
    snippet: source.content.slice(0, CITATION_SNIPPET_MAX_LENGTH),
    score: source.score,
  };
}
