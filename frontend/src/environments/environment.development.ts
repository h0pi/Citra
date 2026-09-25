import { Environment } from './environment.model';

// Development build (`ng serve`). Swapped in via fileReplacements in angular.json.
export const environment: Environment = {
  production: false,
  // Citra.Api "https" launch profile. The API allows this app's origin (localhost:4200) via CORS.
  apiUrl: 'https://localhost:7100',
};
