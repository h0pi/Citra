# Citra — Admin App (Angular)

Admin dashboard for Citra: manage bots and documents, chat with cited answers, view analytics.

**Stack:** Angular 21 (standalone, zoneless, signals) · PrimeNG 21 (Aura theme) · SCSS · Vitest

## Run

```bash
npm install
npm start          # ng serve → http://localhost:4200
npm test           # unit tests (Vitest)
npm run build      # production build → dist/
```

The dev app talks to the API at `https://localhost:7100` (see `src/environments/environment.development.ts`).
Start the backend first — see the root README.

## Folder structure

```
src/
├── environments/         dev / prod settings (apiUrl)
├── styles.scss           global styles + design tokens (--citra-*)
└── app/
    ├── core/             app-wide singletons, loaded once
    │   ├── services/     API client, auth, notifications
    │   ├── guards/       route guards (auth)
    │   ├── interceptors/ HTTP interceptors (auth token, errors, loading)
    │   ├── models/       TypeScript types shared across features
    │   ├── layout/       app shell: sidebar, top bar
    │   └── theme/        PrimeNG preset + light/dark ThemeService
    ├── shared/           reusable, stateless UI building blocks
    │   ├── components/
    │   ├── pipes/
    │   └── directives/
    └── features/         one folder per page/area, lazy-loaded
                          (auth, dashboard, bots, documents, chat, evaluation, settings)
```

**Rules of thumb**

- `features/*` may import from `core/` and `shared/`, never from another feature.
- `shared/` has no business logic and no API calls — only presentational pieces.
- `core/` services are `providedIn: 'root'` singletons.

**Import aliases** (tsconfig `paths`): `@core/*`, `@shared/*`, `@features/*`, `@env/*`.
