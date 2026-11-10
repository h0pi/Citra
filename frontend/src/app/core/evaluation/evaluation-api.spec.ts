import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SKIP_ERROR_TOAST } from '@core/interceptors/error.interceptor';
import { environment } from '@env/environment';

import { EvaluationApi } from './evaluation-api';

describe('EvaluationApi', () => {
  let api: EvaluationApi;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api`;
  const question = { question: 'Q?', expectedAnswer: 'A.', expectedDocumentId: null };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(EvaluationApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads, creates and deletes test sets', () => {
    api.sets('b1').subscribe();
    api.set('s1').subscribe();
    api.createSet('b1', { name: 'HR', description: null }).subscribe();
    api.deleteSet('s1').subscribe();

    http.expectOne((r) => r.method === 'GET' && r.url === `${base}/bots/b1/eval-sets`);
    http.expectOne((r) => r.method === 'GET' && r.url === `${base}/eval-sets/s1`);
    const create = http.expectOne(
      (r) => r.method === 'POST' && r.url === `${base}/bots/b1/eval-sets`,
    );
    expect(create.request.body).toEqual({ name: 'HR', description: null });
    expect(create.request.context.get(SKIP_ERROR_TOAST)).toBe(false);
    expect(http.expectOne((r) => r.method === 'DELETE').request.url).toBe(`${base}/eval-sets/s1`);
  });

  it('adds, changes, removes and imports questions, showing form errors in place', () => {
    api.addQuestion('s1', question).subscribe();
    api.updateQuestion('s1', 'q1', question).subscribe();
    api.deleteQuestion('s1', 'q1').subscribe();
    api.importQuestions('s1', 'question,expected_answer').subscribe();

    const add = http.expectOne(
      (r) => r.method === 'POST' && r.url === `${base}/eval-sets/s1/questions`,
    );
    expect(add.request.body).toEqual(question);
    expect(add.request.context.get(SKIP_ERROR_TOAST)).toBe(true);
    expect(http.expectOne((r) => r.method === 'PUT').request.url).toBe(
      `${base}/eval-sets/s1/questions/q1`,
    );
    expect(http.expectOne((r) => r.method === 'DELETE').request.url).toBe(
      `${base}/eval-sets/s1/questions/q1`,
    );
    const csv = http.expectOne(`${base}/eval-sets/s1/questions/import`);
    expect(csv.request.body).toEqual({ csv: 'question,expected_answer' });
  });

  it('starts a run and reads its progress', () => {
    api.startRun('s1', 'prompt v2').subscribe();
    api.run('r1').subscribe();

    const start = http.expectOne(
      (r) => r.method === 'POST' && r.url === `${base}/eval-sets/s1/runs`,
    );
    expect(start.request.body).toEqual({ notes: 'prompt v2' });
    http.expectOne((r) => r.method === 'GET' && r.url === `${base}/eval-runs/r1`);
  });

  it("lists a set's runs and reads a run's results", () => {
    api.runs('s1').subscribe();
    api.results('r1').subscribe();

    http.expectOne((r) => r.method === 'GET' && r.url === `${base}/eval-sets/s1/runs`);
    http.expectOne((r) => r.method === 'GET' && r.url === `${base}/eval-runs/r1/results`);
  });
});
