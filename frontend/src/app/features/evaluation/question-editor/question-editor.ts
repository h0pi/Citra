import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CitraDocument } from '@core/models/document.models';
import { EVAL_LIMITS, EvalQuestion, EvalQuestionRequest } from '@core/models/evaluation.models';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';

/** Adds a test question, or changes one: the question, the expected answer and (optionally) its document. */
@Component({
  selector: 'app-question-editor',
  imports: [ReactiveFormsModule, ButtonModule, MessageModule, SelectModule, TextareaModule],
  templateUrl: './question-editor.html',
  styleUrl: './question-editor.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionEditor {
  /** The question to change; null = a new one. */
  readonly question = input<EvalQuestion | null>(null);
  /** The bot's documents, to pick the one the answer should come from. */
  readonly documents = input<CitraDocument[]>([]);
  readonly saving = input(false);
  /** The server's answer when saving failed. */
  readonly error = input<string | null>(null);

  readonly save = output<EvalQuestionRequest>();
  readonly closed = output();

  protected readonly limits = EVAL_LIMITS;
  protected readonly documentOptions = computed(() => [
    { label: 'Any document', value: null },
    ...this.documents().map((d) => ({ label: d.fileName, value: d.id as string | null })),
  ]);

  protected readonly form = inject(NonNullableFormBuilder).group({
    question: ['', [Validators.required, Validators.maxLength(EVAL_LIMITS.question)]],
    expectedAnswer: ['', [Validators.required, Validators.maxLength(EVAL_LIMITS.expectedAnswer)]],
    expectedDocumentId: [null as string | null],
  });

  constructor() {
    effect(() => {
      const question = this.question();
      this.form.reset({
        question: question?.question ?? '',
        expectedAnswer: question?.expectedAnswer ?? '',
        expectedDocumentId: question?.expectedDocumentId ?? null,
      });
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.save.emit({
      question: value.question.trim(),
      expectedAnswer: value.expectedAnswer.trim(),
      expectedDocumentId: value.expectedDocumentId,
    });
  }

  protected invalid(field: 'question' | 'expectedAnswer'): boolean {
    const control = this.form.controls[field];
    return control.invalid && control.touched;
  }
}
