import { Injectable, computed, signal } from '@angular/core';

/**
 * Tracks how many HTTP requests are in flight (fed by loadingInterceptor).
 * `isLoading` drives the top progress bar.
 */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly activeRequests = signal(0);

  readonly isLoading = computed(() => this.activeRequests() > 0);

  start(): void {
    this.activeRequests.update((count) => count + 1);
  }

  stop(): void {
    // Never below zero, even if stop() is called one time too many.
    this.activeRequests.update((count) => Math.max(0, count - 1));
  }
}
