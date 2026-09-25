import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { errorInterceptor } from '@core/interceptors/error.interceptor';
import { loadingInterceptor } from '@core/interceptors/loading.interceptor';
import { PageTitleStrategy } from '@core/services/page-title.strategy';
import { CitraPreset } from '@core/theme/citra-preset';
import { MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withComponentInputBinding: route params (e.g. /documents/:id) arrive as component inputs.
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useClass: PageTitleStrategy },
    // XHR-based HttpClient on purpose (not withFetch): fetch cannot report *upload* progress,
    // which the document upload progress bar needs.
    // Order matters: loading wraps everything, so the bar also stops when an error toast is shown.
    provideHttpClient(withInterceptors([loadingInterceptor, errorInterceptor])),
    // Backs NotificationService / <p-toast>.
    MessageService,
    providePrimeNG({
      theme: {
        preset: CitraPreset,
        options: {
          // Dark mode = this class on <html>, toggled by ThemeService.
          darkModeSelector: '.app-dark',
        },
      },
    }),
  ],
};
