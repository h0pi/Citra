import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { CurrentBot } from '@core/bots/current-bot';
import { DocumentsApi } from '@core/documents/documents-api';
import { EvaluationApi } from '@core/evaluation/evaluation-api';
import { EvalQuestion, EvalQuestionRequest, EvalRun } from '@core/models/evaluation.models';
import { toFormError } from '@core/models/problem-details';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { map } from 'rxjs';

import { QuestionEditor } from './question-editor/question-editor';
import { RunProgress } from './run-progress/run-progress';

/** Most documents offered as "expected document" (the API's largest page). */
const DOCUMENT_PAGE_SIZE = 100;

/**
 * Test sets of the current bot: questions with the answers a correct bot gives, to measure its accuracy.
 * Everyone sees them; owners and admins create sets, edit questions and import them from CSV.
 */
@Component({
  selector: 'app-evaluation-page',
  imports: [
    FormsModule,
    RouterLink,
    DatePipe,
    DecimalPipe,
    PageHeader,
    EmptyState,
    QuestionEditor,
    RunProgress,
    ButtonModule,
    ConfirmDialogModule,
    InputTextModule,
    MessageModule,
  ],
  templateUrl: './evaluation-page.html',
  styleUrl: './evaluation-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [ConfirmationService],
})
export class EvaluationPage {
  private readonly evaluationApi = inject(EvaluationApi);
  private readonly documentsApi = inject(DocumentsApi);
  private readonly confirmation = inject(ConfirmationService);

  protected readonly bot = inject(CurrentBot).bot;
  protected readonly canEdit = inject(AuthService).canEdit;

  protected readonly sets = rxResource({
    params: () => this.bot()?.id,
    stream: ({ params }) => this.evaluationApi.sets(params),
  });
  /** The open set: the first one, until another is chosen (or it is deleted). */
  protected readonly selectedId = linkedSignal<string | null>(() => {
    const sets = this.sets.hasValue() ? this.sets.value() : [];
    return sets[0]?.id ?? null;
  });
  protected readonly set = rxResource({
    params: () => this.selectedId() ?? undefined,
    stream: ({ params }) => this.evaluationApi.set(params),
  });
  /** The open set's earlier runs, newest first. */
  protected readonly runs = rxResource({
    params: () => this.selectedId() ?? undefined,
    stream: ({ params }) => this.evaluationApi.runs(params),
    defaultValue: [],
  });
  protected readonly documents = rxResource({
    params: () => this.bot()?.id,
    stream: ({ params }) =>
      this.documentsApi.list(params, { pageSize: DOCUMENT_PAGE_SIZE }).pipe(map((p) => p.items)),
    defaultValue: [],
  });
  protected readonly documentNames = computed(
    () => new Map(this.documents.value().map((d) => [d.id, d.fileName])),
  );

  protected readonly newSetName = signal('');
  /** Which question the editor is open for: 'new', a question id, or none. */
  protected readonly editing = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly importErrors = signal<string[]>([]);
  /** The run started on this page for the open set (cleared when another set is opened). */
  protected readonly startedRun = linkedSignal<string | null, EvalRun | null>({
    source: this.selectedId,
    computation: () => null,
  });
  /** A run of the open set is being started or is still answering questions. */
  protected readonly running = linkedSignal({ source: this.selectedId, computation: () => false });

  protected startRun(): void {
    const setId = this.selectedId();
    if (!setId || this.running()) {
      return;
    }
    this.running.set(true);
    this.evaluationApi.startRun(setId, null).subscribe({
      next: (run) => this.startedRun.set(run),
      error: () => this.running.set(false),
    });
  }

  /** The started run is completed or failed: it joins the list of runs. */
  protected runFinished(): void {
    this.running.set(false);
    this.runs.reload();
  }

  protected createSet(): void {
    const bot = this.bot();
    const name = this.newSetName().trim();
    if (!bot || !name || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.evaluationApi.createSet(bot.id, { name, description: null }).subscribe({
      next: (created) => {
        this.saving.set(false);
        this.newSetName.set('');
        this.sets.reload();
        this.selectedId.set(created.id);
      },
      error: () => this.saving.set(false),
    });
  }

  protected confirmDeleteSet(): void {
    const set = this.set.hasValue() ? this.set.value() : null;
    if (!set) {
      return;
    }
    this.confirmation.confirm({
      header: 'Delete test set?',
      message: `"${set.name}" with its questions and runs will be removed. This cannot be undone.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger', icon: 'pi pi-trash' },
      rejectButtonProps: { severity: 'secondary', text: true },
      defaultFocus: 'reject',
      accept: () => this.evaluationApi.deleteSet(set.id).subscribe(() => this.sets.reload()),
    });
  }

  protected openEditor(id: string): void {
    this.error.set(null);
    this.editing.set(id);
  }

  protected saveQuestion(request: EvalQuestionRequest): void {
    const setId = this.selectedId();
    const editing = this.editing();
    if (!setId || !editing || this.saving()) {
      return;
    }
    this.saving.set(true);
    const saved =
      editing === 'new'
        ? this.evaluationApi.addQuestion(setId, request)
        : this.evaluationApi.updateQuestion(setId, editing, request);
    saved.subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(null);
        this.set.reload();
        this.sets.reload(); // question counts
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.error.set(toFormError(error).message);
      },
    });
  }

  protected confirmDeleteQuestion(question: EvalQuestion): void {
    const setId = this.selectedId();
    if (!setId) {
      return;
    }
    this.confirmation.confirm({
      header: 'Delete question?',
      message: `"${question.question}" will be removed from the set.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger', icon: 'pi pi-trash' },
      rejectButtonProps: { severity: 'secondary', text: true },
      defaultFocus: 'reject',
      accept: () =>
        this.evaluationApi.deleteQuestion(setId, question.id).subscribe(() => {
          this.set.reload();
          this.sets.reload();
        }),
    });
  }

  /** Adds the questions of a chosen CSV file; the server checks every row and names the wrong ones. */
  protected async importCsv(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    const setId = this.selectedId();
    input.value = ''; // the same file can be chosen again after fixing it
    if (!file || !setId) {
      return;
    }
    this.importErrors.set([]);
    const csv = await file.text();
    this.evaluationApi.importQuestions(setId, csv).subscribe({
      next: () => {
        this.set.reload();
        this.sets.reload();
      },
      error: (error: unknown) => {
        const { message, fieldErrors } = toFormError(error);
        this.importErrors.set(fieldErrors['Csv'] ?? fieldErrors['csv'] ?? [message]);
      },
    });
  }
}
