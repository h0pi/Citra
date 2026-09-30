import { HttpEventType } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { DocumentsApi } from '@core/documents/documents-api';
import { CitraDocument } from '@core/models/document.models';
import { toFormError } from '@core/models/problem-details';
import { Subject, Subscription } from 'rxjs';

import { uploadProblem } from './upload-rules';

export type UploadState = 'waiting' | 'uploading' | 'done' | 'failed';

/** One file on its way to the server, as the upload list shows it. */
export interface UploadItem {
  id: number;
  fileName: string;
  /** 0–100. */
  progress: number;
  state: UploadState;
  error: string | null;
}

interface Job {
  id: number;
  botId: string;
  file: File;
}

/**
 * The files being uploaded from the documents page: at most a few at a time, each with its own progress and
 * error. Finished uploads disappear after a moment; failed ones stay until dismissed. Provided by the page, so
 * leaving the page cancels uploads still running.
 */
@Injectable()
export class DocumentUploads {
  /** How many files are sent at once: enough to be quick, few enough not to choke a slow connection. */
  static readonly Parallel = 3;

  /** How long a finished upload stays in the list, in ms. */
  static readonly DoneVisibleMs = 4000;

  private readonly documentsApi = inject(DocumentsApi);

  private readonly _items = signal<UploadItem[]>([]);
  private readonly waiting: Job[] = [];
  private readonly running = new Map<number, Subscription>();
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();
  private nextId = 1;

  readonly items = this._items.asReadonly();

  /** Every document the server accepted (its bot may no longer be the one shown). */
  readonly uploaded = new Subject<CitraDocument>();

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.running.forEach((request) => request.unsubscribe());
      this.timers.forEach((timer) => clearTimeout(timer));
    });
  }

  /** Queues files for a bot. Files the server would refuse (type, size) fail at once, without uploading. */
  add(botId: string, files: readonly File[]): void {
    for (const file of files) {
      const id = this.nextId++;
      const problem = uploadProblem(file);
      this._items.update((items) => [
        ...items,
        {
          id,
          fileName: file.name,
          progress: 0,
          state: problem ? 'failed' : 'waiting',
          error: problem,
        },
      ]);
      if (!problem) {
        this.waiting.push({ id, botId, file });
      }
    }
    this.startWaiting();
  }

  /** Stops an upload still running or waiting, or removes a finished one from the list. */
  dismiss(id: number): void {
    this.running.get(id)?.unsubscribe();
    this.running.delete(id);
    const index = this.waiting.findIndex((job) => job.id === id);
    if (index >= 0) {
      this.waiting.splice(index, 1);
    }
    clearTimeout(this.timers.get(id));
    this.timers.delete(id);
    this._items.update((items) => items.filter((item) => item.id !== id));
    this.startWaiting();
  }

  private startWaiting(): void {
    while (this.running.size < DocumentUploads.Parallel && this.waiting.length > 0) {
      this.start(this.waiting.shift()!);
    }
  }

  private start(job: Job): void {
    this.patch(job.id, { state: 'uploading' });
    const request = this.documentsApi.upload(job.botId, job.file).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          // 99% until the server has answered: the last step (saving, checking for duplicates) takes a moment.
          this.patch(job.id, {
            progress: Math.min(99, Math.round((event.loaded / event.total) * 100)),
          });
        } else if (event.type === HttpEventType.Response && event.body) {
          this.patch(job.id, { state: 'done', progress: 100 });
          this.uploaded.next(event.body);
          this.timers.set(
            job.id,
            setTimeout(() => this.dismiss(job.id), DocumentUploads.DoneVisibleMs),
          );
        }
      },
      error: (error: unknown) => {
        const { message, fieldErrors } = toFormError(error, 'The upload failed. Please try again.');
        this.patch(job.id, { state: 'failed', error: fieldErrors['file']?.[0] ?? message });
        this.finish(job.id);
      },
      complete: () => this.finish(job.id),
    });
    // A request that fails at once has already finished (and been removed) by now.
    if (!request.closed) {
      this.running.set(job.id, request);
    }
  }

  private finish(id: number): void {
    this.running.delete(id);
    this.startWaiting();
  }

  private patch(id: number, changes: Partial<UploadItem>): void {
    this._items.update((items) =>
      items.map((item) => (item.id === id ? { ...item, ...changes } : item)),
    );
  }
}
