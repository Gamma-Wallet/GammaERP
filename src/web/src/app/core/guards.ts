import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth';
import { itemAt } from './menu';
import { SessionService } from './session';

/** Signed in and the user known; otherwise off to the sign-in page, coming back here. */
export const signedInGuard: CanActivateFn = async (_, state) => {
  const auth = inject(AuthService);
  const session = inject(SessionService);
  if (!auth.isSignedIn()) {
    await auth.signIn(state.url);
    return false;
  }
  if (!session.me()) {
    await session.load();
  }
  return true;
};

/** The screens: signed in, at a cost centre (else choosing one), with that cost centre's menu loaded. */
export const shellGuard: CanActivateFn = async (route, state) => {
  // Everything injected before the first await (the injection context ends there).
  const session = inject(SessionService);
  const router = inject(Router);
  if (!(await signedInGuard(route, state))) {
    return false;
  }
  if (!session.node()) {
    return router.parseUrl('/choose');
  }
  if (!session.menuLoaded()) {
    await session.loadMenu();
  }
  return true;
};

/** An address the menu does not have is no screen: back to the home screen. */
export const menuScreenGuard: CanActivateFn = (_, state) =>
  itemAt(inject(SessionService).menu(), state.url) ? true : inject(Router).parseUrl('/');
