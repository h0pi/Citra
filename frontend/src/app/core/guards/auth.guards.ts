import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { DEFAULT_AFTER_LOGIN } from '@core/auth/return-url';

// Both guards run after the app initializer has restored and verified the saved session,
// so a logged-in user is never briefly treated as logged out.

/** App pages: logged-out users go to /login, remembering where they wanted to go. */
export const authGuard: CanActivateFn = (_route, state) => requireLogin(state);

/** Same check when moving between pages inside the app shell. */
export const authChildGuard: CanActivateChildFn = (_route, state) => requireLogin(state);

/** Login/register: already logged-in users go straight to the app. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? inject(Router).parseUrl(DEFAULT_AFTER_LOGIN) : true;
};

function requireLogin(state: RouterStateSnapshot) {
  if (inject(AuthService).isAuthenticated()) {
    return true;
  }
  return inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
}
