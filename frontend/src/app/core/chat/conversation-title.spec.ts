import { CONVERSATION_TITLE_MAX_LENGTH, conversationTitle } from './conversation-title';

describe('conversationTitle', () => {
  it('keeps a short question, on one line', () => {
    expect(conversationTitle('  How many\n\nvacation   days?\t')).toBe('How many vacation days?');
  });

  it('keeps a question of exactly the maximum length', () => {
    const question = 'a'.repeat(CONVERSATION_TITLE_MAX_LENGTH);

    expect(conversationTitle(question)).toBe(question);
  });

  it('shortens a long question at the last word that fits, without trailing punctuation', () => {
    const question =
      'What is the policy for carrying over unused vacation days, and; how many can I keep for next year?';

    const title = conversationTitle(question);

    expect(title).toBe('What is the policy for carrying over unused vacation days…');
    expect(title.length).toBeLessThanOrEqual(CONVERSATION_TITLE_MAX_LENGTH + 1);
  });

  it('drops punctuation left at the cut', () => {
    expect(
      conversationTitle('Tell me about the travel reimbursement rules for trips; -. abroad please'),
    ).toBe('Tell me about the travel reimbursement rules for trips…');
  });

  it('cuts a very long word instead of leaving a too short title', () => {
    const question = 'Hi ' + 'x'.repeat(100);

    expect(conversationTitle(question)).toBe(('Hi ' + 'x'.repeat(100)).slice(0, 60) + '…');
  });

  it('cuts at a space in the second half of the limit', () => {
    const question = 'y'.repeat(30) + ' ' + 'z'.repeat(40);

    expect(conversationTitle(question)).toBe('y'.repeat(30) + '…');
  });
});
