/** Settings that differ between development and production builds. */
export interface Environment {
  production: boolean;
  /**
   * Base URL of the Citra API, without a trailing slash.
   * Empty string = same origin (the API is served under the same domain, e.g. behind nginx).
   */
  apiUrl: string;
}
