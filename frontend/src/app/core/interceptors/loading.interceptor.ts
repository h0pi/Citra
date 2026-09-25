import { HttpContextToken, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { LoadingService } from '@core/services/loading.service';
import { finalize } from 'rxjs';

/**
 * Set on background requests that should not show the global progress bar
 * (polling, long-running streams, silent refreshes).
 */
export const SKIP_LOADING = new HttpContextToken<boolean>(() => false);

/** Counts in-flight requests so the top progress bar shows while the app waits for the API. */
export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.context.get(SKIP_LOADING)) {
    return next(req);
  }

  const loading = inject(LoadingService);
  loading.start();

  // finalize runs on success, error AND cancellation (e.g. navigating away), so the count never leaks.
  return next(req).pipe(finalize(() => loading.stop()));
};
