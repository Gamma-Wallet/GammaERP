import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { EMPTY, catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth';
import { language } from './i18n';
import { NodeStore } from './node-store';

/** The API's header naming the cost centre a request works at. */
export const NODE_HEADER = 'X-Gamma-Node';

/** The calls made signed in. */
const signedIn = (url: string) => url.startsWith('/api/') || url === '/account/me';

/** Every call to the API carries the access token and, under /api, the chosen cost centre; a refused token starts a new sign-in. */
export const apiInterceptor: HttpInterceptorFn = (request, next) => {
  if (!signedIn(request.url)) {
    return next(request);
  }
  const auth = inject(AuthService);
  const node = inject(NodeStore).node();
  const router = inject(Router);
  return from(auth.accessToken()).pipe(
    switchMap(token => {
      if (!token) {
        void auth.signIn(router.url);
        return EMPTY;
      }
      const headers: Record<string, string> = { Authorization: `Bearer ${token}` };
      // A request naming its cost centre itself (adding a company works at the group) keeps it.
      if (node && request.url.startsWith('/api/') && !request.headers.has(NODE_HEADER)) {
        headers[NODE_HEADER] = node;
      }
      // The answers' names and messages in the screens' language.
      const inLanguage = language() !== 'en' && request.url.startsWith('/api/') && !request.params.has('language');
      return next(request.clone({ setHeaders: headers, ...(inLanguage ? { setParams: { language: language() } } : {}) }));
    }),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        auth.clear();
        void auth.signIn(router.url);
        return EMPTY;
      }
      return throwError(() => error);
    }),
  );
};
