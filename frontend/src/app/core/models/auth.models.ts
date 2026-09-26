/** Matches the backend's UserRole enum (sent as text). */
export type UserRole = 'Owner' | 'Admin' | 'Viewer' | 'Demo';

/** The logged-in user (GET /api/auth/me, and inside every auth response). */
export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  role: UserRole;
  tenantId: string;
  tenantName: string;
}

/** Returned by register, login and refresh. */
export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: CurrentUser;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  companyName: string;
  displayName: string;
  email: string;
  password: string;
}
