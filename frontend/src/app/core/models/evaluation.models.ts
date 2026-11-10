/** One row of a bot's test set list (GET /api/bots/{botId}/eval-sets). */
export interface EvalSetSummary {
  id: string;
  botId: string;
  name: string;
  description: string | null;
  questionCount: number;
  createdAt: string;
}

/** A test set with its questions, in the order they were added. */
export interface EvalSet {
  id: string;
  botId: string;
  name: string;
  description: string | null;
  questions: EvalQuestion[];
}

/** A test question and the answer a correct bot gives; optionally the document it should come from. */
export interface EvalQuestion {
  id: string;
  question: string;
  expectedAnswer: string;
  expectedDocumentId: string | null;
}

export interface EvalSetRequest {
  name: string;
  description: string | null;
}

export interface EvalQuestionRequest {
  question: string;
  expectedAnswer: string;
  expectedDocumentId: string | null;
}

export type EvalRunStatus = 'Pending' | 'Running' | 'Completed' | 'Failed';

/** One run of a test set (POST /api/eval-sets/{id}/runs, GET /api/eval-runs/{id}). */
export interface EvalRun {
  id: string;
  evalSetId: string;
  botId: string;
  status: EvalRunStatus;
  model: string;
  notes: string | null;
  totalQuestions: number;
  /** Answered so far (the progress while it runs). */
  answeredQuestions: number;
  correctCount: number;
  partialCount: number;
  retrievalHitCount: number;
  /** 0–100 once completed (correct = 1 point, partly correct = ½). */
  score: number | null;
  totalCostUsd: number;
  error: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export type EvalGrade = 'Wrong' | 'Partial' | 'Correct';

/** A run's outcome for one question (GET /api/eval-runs/{id}/results), in the set's order. */
export interface EvalResult {
  questionId: string;
  question: string;
  expectedAnswer: string;
  expectedDocumentId: string | null;
  actualAnswer: string;
  grade: EvalGrade;
  judgeReason: string;
  /** The expected document was among the sources found (false when the question names none). */
  retrievalHit: boolean;
  latencyMs: number;
  costUsd: number;
}

/** Same limits as the backend (EvalSet / EvalQuestion constants). */
export const EVAL_LIMITS = {
  name: 100,
  question: 1000,
  expectedAnswer: 4000,
} as const;
