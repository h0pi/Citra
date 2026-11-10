import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { CurrentBot } from '@core/bots/current-bot';
import { DocumentsApi } from '@core/documents/documents-api';
import { EvaluationApi } from '@core/evaluation/evaluation-api';
import { BotSummary } from '@core/models/bot.models';
import { EvalSet, EvalSetSummary } from '@core/models/evaluation.models';
import { of, throwError } from 'rxjs';

import { EvaluationPage } from './evaluation-page';
import { QuestionEditor } from './question-editor/question-editor';
import { RunProgress } from './run-progress/run-progress';

const summaries: EvalSetSummary[] = [
  { id: 's1', botId: 'b1', name: 'HR', description: null, questionCount: 1, createdAt: '' },
  { id: 's2', botId: 'b1', name: 'IT', description: null, questionCount: 0, createdAt: '' },
];
const hr: EvalSet = {
  id: 's1',
  botId: 'b1',
  name: 'HR',
  description: null,
  questions: [
    { id: 'q1', question: 'Vacation days?', expectedAnswer: '25 days.', expectedDocumentId: 'd1' },
  ],
};

describe('EvaluationPage', () => {
  const api = {
    sets: vi.fn(() => of(summaries)),
    set: vi.fn((id: string) =>
      of(id === 's1' ? hr : { ...hr, id: 's2', name: 'IT', questions: [] }),
    ),
    createSet: vi.fn(() => of({ ...hr, id: 's3', name: 'Sales', questions: [] })),
    addQuestion: vi.fn(() => of(hr.questions[0])),
    updateQuestion: vi.fn(() => of(hr.questions[0])),
    deleteQuestion: vi.fn(() => of(undefined)),
    deleteSet: vi.fn(() => of(undefined)),
    importQuestions: vi.fn(() => of(hr)),
    startRun: vi.fn(() =>
      of({ id: 'r1', status: 'Pending', answeredQuestions: 0, totalQuestions: 1 }),
    ),
    run: vi.fn(() => of({ id: 'r1', status: 'Running', answeredQuestions: 0, totalQuestions: 1 })),
    runs: vi.fn(() =>
      of([
        { id: 'r0', status: 'Completed', score: 75, notes: 'v1', createdAt: '2026-11-09T10:00:00' },
      ]),
    ),
  };
  const canEdit = signal(true);

  beforeEach(() => {
    vi.clearAllMocks();
    canEdit.set(true);
    TestBed.configureTestingModule({
      imports: [EvaluationPage],
      providers: [
        provideRouter([]),
        { provide: EvaluationApi, useValue: api },
        {
          provide: DocumentsApi,
          useValue: {
            list: () => of({ items: [{ id: 'd1', fileName: 'Handbook.pdf' }], totalCount: 1 }),
          },
        },
        {
          provide: CurrentBot,
          useValue: { bot: signal({ id: 'b1', name: 'Support' } as BotSummary) },
        },
        { provide: AuthService, useValue: { canEdit } },
      ],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(EvaluationPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const click = async (label: string) => {
      [...el.querySelectorAll<HTMLButtonElement>('button')]
        .find((b) => b.textContent?.includes(label) || b.getAttribute('aria-label') === label)!
        .click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, el, click };
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim();

  it('lists the bot’s test sets and opens the first with its questions', async () => {
    const { el, click } = await render();

    expect(api.sets).toHaveBeenCalledWith('b1');
    expect([...el.querySelectorAll('.set .name')].map(text)).toEqual(['HR', 'IT']);
    expect([...el.querySelectorAll('.set .count')].map(text)).toEqual(['1', '0']);
    expect([...el.querySelectorAll('.question .text > *')].map((e) => text(e))).toEqual([
      'Vacation days?',
      '25 days.',
      'Handbook.pdf',
    ]);

    await click('IT');
    expect(api.set).toHaveBeenLastCalledWith('s2');
    expect(el.textContent).toContain('No questions yet.');
  });

  it('lists the open set’s runs with links to their results', async () => {
    const { el } = await render();

    expect(api.runs).toHaveBeenCalledWith('s1');
    const link = el.querySelector<HTMLAnchorElement>('.runs a')!;
    expect(link.getAttribute('href')).toBe('/evaluation/runs/r0');
    expect(text(link)).toBe('Nov 9, 10:00 · v1');
    expect(text(el.querySelector('.runs li span'))).toBe('75%');
  });

  it('adds a question with the editor', async () => {
    const { fixture, el, click } = await render();

    await click('Add question');
    const editor = fixture.debugElement.query(By.directive(QuestionEditor))
      .componentInstance as QuestionEditor;
    const request = { question: 'Parking?', expectedAnswer: 'Behind.', expectedDocumentId: null };
    editor.save.emit(request);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(api.addQuestion).toHaveBeenCalledWith('s1', request);
    expect(api.set).toHaveBeenCalledTimes(2); // reloaded
    expect(el.querySelector('app-question-editor')).toBeNull();
  });

  it('creates a set and opens it', async () => {
    const { fixture, el } = await render();

    const input = el.querySelector<HTMLInputElement>('.new-set input')!;
    input.value = ' Sales ';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector('.new-set')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(api.createSet).toHaveBeenCalledWith('b1', { name: 'Sales', description: null });
    expect(api.set).toHaveBeenLastCalledWith('s3');
  });

  it('shows the rows a CSV import got wrong', async () => {
    api.importQuestions.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              title: 'Invalid',
              status: 400,
              errors: { Csv: ['Row 3: the question is empty.'] },
            },
          }),
      ),
    );
    const { fixture, el } = await render();
    const input = el.querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', {
      value: [new File(['question,expected_answer\n,A.'], 'set.csv')],
    });

    input.dispatchEvent(new Event('change'));
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    expect(api.importQuestions).toHaveBeenCalledWith('s1', 'question,expected_answer\n,A.');
    expect(text(el.querySelector('p-message ul'))).toBe('Row 3: the question is empty.');
  });

  it('starts a run, shows its progress and allows the next run once it is done', async () => {
    const { fixture, el, click } = await render();
    const runButton = () =>
      [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
        b.textContent?.includes('Run'),
      )!;

    await click('Run');

    expect(api.startRun).toHaveBeenCalledWith('s1', null);
    const progress = fixture.debugElement.query(By.directive(RunProgress))
      .componentInstance as RunProgress;
    expect(progress.run().id).toBe('r1');
    expect(runButton().disabled).toBe(true);

    progress.finished.emit({ ...progress.run(), status: 'Completed' });
    fixture.detectChanges();
    expect(runButton().disabled).toBe(false);
    expect(api.runs).toHaveBeenCalledTimes(2); // the finished run joins the list
  });

  it('is read-only for viewers', async () => {
    canEdit.set(false);
    const { el } = await render();

    expect(el.querySelector('.new-set')).toBeNull();
    expect(el.querySelector('.detail-actions')).toBeNull();
    expect(el.querySelector('.row-actions')).toBeNull();
    expect(el.textContent).toContain('Vacation days?');
  });
});
