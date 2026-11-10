import { TestBed } from '@angular/core/testing';
import { CitraDocument } from '@core/models/document.models';
import { EvalQuestion, EvalQuestionRequest } from '@core/models/evaluation.models';

import { QuestionEditor } from './question-editor';

describe('QuestionEditor', () => {
  async function render(question: EvalQuestion | null = null) {
    const fixture = TestBed.createComponent(QuestionEditor);
    fixture.componentRef.setInput('question', question);
    fixture.componentRef.setInput('documents', [
      { id: 'd1', fileName: 'Handbook.pdf' },
    ] as CitraDocument[]);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const saved: EvalQuestionRequest[] = [];
    fixture.componentInstance.save.subscribe((r) => saved.push(r));
    const type = async (selector: string, value: string) => {
      const field = el.querySelector<HTMLTextAreaElement>(selector)!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };
    return { fixture, el, saved, type, submit };
  }

  it('sends a new question with trimmed text', async () => {
    const { el, saved, type, submit } = await render();

    expect(el.querySelector('button[type="submit"]')?.textContent).toContain('Add question');
    await type('#evalQuestion', '  Vacation days?  ');
    await type('#evalExpectedAnswer', ' 25 days. ');
    await submit();

    expect(saved).toEqual([
      { question: 'Vacation days?', expectedAnswer: '25 days.', expectedDocumentId: null },
    ]);
  });

  it('asks for both texts before sending', async () => {
    const { el, saved, submit } = await render();

    await submit();

    expect(saved).toEqual([]);
    expect([...el.querySelectorAll('.field-error')].map((e) => e.textContent?.trim())).toEqual([
      'Write the question.',
      'Write the answer a correct bot gives.',
    ]);
  });

  it('starts from the question being changed, with its document', async () => {
    const { el, saved, submit } = await render({
      id: 'q1',
      question: 'Parking?',
      expectedAnswer: 'Behind the office.',
      expectedDocumentId: 'd1',
    });

    expect(el.querySelector<HTMLTextAreaElement>('#evalQuestion')!.value).toBe('Parking?');
    expect(el.querySelector('button[type="submit"]')?.textContent).toContain('Save question');
    await submit();

    expect(saved).toEqual([
      { question: 'Parking?', expectedAnswer: 'Behind the office.', expectedDocumentId: 'd1' },
    ]);
  });
});
