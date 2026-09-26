export const DEFAULT_AFTER_LOGIN = '/dashboard';

/**
 * Where to go after login. Only same-app paths are allowed ("/documents"), never full URLs
 * ("https://evil.com") or protocol-relative ones ("//evil.com") — otherwise a crafted login link
 * could send users to a phishing site right after they sign in (an "open redirect").
 */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return DEFAULT_AFTER_LOGIN;
  }
  // Never bounce back to the auth pages themselves.
  if (value.startsWith('/login') || value.startsWith('/register')) {
    return DEFAULT_AFTER_LOGIN;
  }
  return value;
}
