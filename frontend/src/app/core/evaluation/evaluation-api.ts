import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import {
  EvalQuestion,
  EvalResult,
  EvalRun,
  EvalQuestionRequest,
  EvalSet,
  EvalSetRequest,
  EvalSetSummary,
} from '@core/models/evaluation.models';
import { ApiService } from '@core/services/api.service';
import { Observable } from 'rxjs';

/** Sent from the question editor and the import, which show their errors themselves instead of a toast. */
const FORM_REQUEST = { context: new HttpContext().set(SKIP_ERROR_TOAST, true) };

/** HTTP calls for test sets and their questions (/api/bots/{botId}/eval-sets, /api/eval-sets). */
@Injectable({ providedIn: 'root' })
export class EvaluationApi {
  private readonly api = inject(ApiService);

  sets(botId: string): Observable<EvalSetSummary[]> {
    return this.api.get<EvalSetSummary[]>(`/api/bots/${encodeURIComponent(botId)}/eval-sets`);
  }

  set(id: string): Observable<EvalSet> {
    return this.api.get<EvalSet>(`/api/eval-sets/${encodeURIComponent(id)}`);
  }

  /** Errors show as a toast (the name field is too small for messages). */
  createSet(botId: string, request: EvalSetRequest): Observable<EvalSet> {
    return this.api.post<EvalSet>(`/api/bots/${encodeURIComponent(botId)}/eval-sets`, request);
  }

  deleteSet(id: string): Observable<void> {
    return this.api.delete<void>(`/api/eval-sets/${encodeURIComponent(id)}`);
  }

  addQuestion(setId: string, request: EvalQuestionRequest): Observable<EvalQuestion> {
    return this.api.post<EvalQuestion>(
      `/api/eval-sets/${encodeURIComponent(setId)}/questions`,
      request,
      FORM_REQUEST,
    );
  }

  updateQuestion(
    setId: string,
    questionId: string,
    request: EvalQuestionRequest,
  ): Observable<EvalQuestion> {
    return this.api.put<EvalQuestion>(
      `/api/eval-sets/${encodeURIComponent(setId)}/questions/${encodeURIComponent(questionId)}`,
      request,
      FORM_REQUEST,
    );
  }

  deleteQuestion(setId: string, questionId: string): Observable<void> {
    return this.api.delete<void>(
      `/api/eval-sets/${encodeURIComponent(setId)}/questions/${encodeURIComponent(questionId)}`,
    );
  }

  /** Adds questions from a CSV file's text; all or nothing. */
  importQuestions(setId: string, csv: string): Observable<EvalSet> {
    return this.api.post<EvalSet>(
      `/api/eval-sets/${encodeURIComponent(setId)}/questions/import`,
      { csv },
      FORM_REQUEST,
    );
  }

  /** Starts a run; the questions are answered in the background (follow it with {@link run}). */
  startRun(setId: string, notes: string | null): Observable<EvalRun> {
    return this.api.post<EvalRun>(`/api/eval-sets/${encodeURIComponent(setId)}/runs`, { notes });
  }

  run(id: string): Observable<EvalRun> {
    return this.api.get<EvalRun>(`/api/eval-runs/${encodeURIComponent(id)}`);
  }

  /** A test set's runs, newest first. */
  runs(setId: string): Observable<EvalRun[]> {
    return this.api.get<EvalRun[]>(`/api/eval-sets/${encodeURIComponent(setId)}/runs`);
  }

  results(runId: string): Observable<EvalResult[]> {
    return this.api.get<EvalResult[]>(`/api/eval-runs/${encodeURIComponent(runId)}/results`);
  }
}
