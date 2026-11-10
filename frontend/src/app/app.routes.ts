import { Routes, UrlMatchResult, UrlSegment } from '@angular/router';
import { authChildGuard, authGuard, guestGuard } from '@core/guards/auth.guards';
import { unsavedChangesGuard } from '@core/guards/unsaved-changes.guard';
import { Shell } from '@core/layout/shell/shell';

// Every page is lazy-loaded: its code is downloaded only when the user first opens it,
// which keeps the initial load small. Titles feed the browser tab via PageTitleStrategy.
export const routes: Routes = [
  // Logged-out pages: own centered layout, no sidebar. Logged-in users are sent to the app.
  {
    path: 'login',
    title: 'Log in',
    canActivate: [guestGuard],
    loadComponent: () => import('@features/auth/login/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    title: 'Create account',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('@features/auth/register/register-page').then((m) => m.RegisterPage),
  },
  // App pages inside the shell (sidebar + top bar). Login required for all of them.
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    canActivateChild: [authChildGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Dashboard',
        loadComponent: () =>
          import('@features/dashboard/dashboard-page').then((m) => m.DashboardPage),
      },
      {
        path: 'bots',
        title: 'Bots',
        loadComponent: () => import('@features/bots/bots-page').then((m) => m.BotsPage),
      },
      {
        path: 'bots/:id',
        title: 'Bot settings',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('@features/bots/bot-settings/bot-settings-page').then((m) => m.BotSettingsPage),
      },
      {
        path: 'documents',
        title: 'Documents',
        loadComponent: () =>
          import('@features/documents/documents-page').then((m) => m.DocumentsPage),
      },
      {
        // /chat and /chat/{id} (one chat, from the list or a link; the id arrives as the page's conversationId
        // input). One route for both, so the page stays when a new chat gets its address mid-answer.
        matcher: chatMatcher,
        title: 'Chat',
        loadComponent: () => import('@features/chat/chat-page').then((m) => m.ChatPage),
      },
      {
        path: 'evaluation/runs/:id/compare/:otherId',
        title: 'Compare runs',
        loadComponent: () =>
          import('@features/evaluation/run-compare/run-compare-page').then((m) => m.RunComparePage),
      },
      {
        path: 'evaluation/runs/:id',
        title: 'Run results',
        loadComponent: () =>
          import('@features/evaluation/run-results/run-results-page').then((m) => m.RunResultsPage),
      },
      {
        path: 'evaluation',
        title: 'Evaluation',
        loadComponent: () =>
          import('@features/evaluation/evaluation-page').then((m) => m.EvaluationPage),
      },
      {
        path: '**',
        title: 'Page not found',
        loadComponent: () =>
          import('@features/not-found/not-found-page').then((m) => m.NotFoundPage),
      },
    ],
  },
];

/** Matches /chat and /chat/{conversationId}. */
export function chatMatcher(segments: UrlSegment[]): UrlMatchResult | null {
  if (segments[0]?.path !== 'chat' || segments.length > 2) {
    return null;
  }

  return segments.length === 2
    ? { consumed: segments, posParams: { conversationId: segments[1] } }
    : { consumed: segments };
}
