/** Same as the API (ConversationTitle.MaxLength). */
export const CONVERSATION_TITLE_MAX_LENGTH = 60;

/**
 * The title the API gives a new chat (ConversationTitle.FromQuestion): its first question on one line, shortened
 * at a word boundary. Used to show a new chat before the list is loaded again.
 */
export function conversationTitle(question: string): string {
  const oneLine = question.replace(/\s+/g, ' ').trim();
  if (oneLine.length <= CONVERSATION_TITLE_MAX_LENGTH) {
    return oneLine;
  }

  // Cut at the last space, unless that would leave a very short title (one long word).
  let cut = oneLine.slice(0, CONVERSATION_TITLE_MAX_LENGTH);
  const space = cut.lastIndexOf(' ');
  if (space >= CONVERSATION_TITLE_MAX_LENGTH / 2) {
    cut = cut.slice(0, space);
  }

  return cut.replace(/[ ,;:\-.]+$/, '') + '…';
}
