import { Environment } from './environment.model';

// Production build (`ng build`).
export const environment: Environment = {
  production: true,
  // Same origin: in production the API is served under the same domain as the app
  // (reverse proxy routes /api and /health to the backend). Finalized during deployment (Phase 22).
  apiUrl: '',
};
