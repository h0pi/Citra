import { Routes } from '@angular/router';
import { Shell } from '@core/layout/shell/shell';

// Every page is lazy-loaded: its code is downloaded only when the user first opens it,
// which keeps the initial load small. Titles feed the browser tab via PageTitleStrategy.
export const routes: Routes = [
  {
    path: '',
    component: Shell,
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
        path: 'documents',
        title: 'Documents',
        loadComponent: () =>
          import('@features/documents/documents-page').then((m) => m.DocumentsPage),
      },
      {
        path: 'chat',
        title: 'Chat',
        loadComponent: () => import('@features/chat/chat-page').then((m) => m.ChatPage),
      },
      {
        path: 'evaluation',
        title: 'Evaluation',
        loadComponent: () =>
          import('@features/evaluation/evaluation-page').then((m) => m.EvaluationPage),
      },
      {
        path: 'settings',
        title: 'Settings',
        loadComponent: () => import('@features/settings/settings-page').then((m) => m.SettingsPage),
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
