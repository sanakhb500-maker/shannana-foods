import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Auth } from './auth';

function readCookie(name: string): string {
  const m = document.cookie.split('; ').find((c) => c.startsWith(name + '='));
  return m ? decodeURIComponent(m.substring(name.length + 1)) : '';
}

/** Sends the auth cookie, adds the CSRF header on writes, and bounces to /login on 401. */
export const apiInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const auth = inject(Auth);
  if (!req.url.startsWith('/api/')) return next(req);

  const unsafe = !['GET', 'HEAD', 'OPTIONS'].includes(req.method);
  const csrf = readCookie('erp_csrf');
  const outgoing = req.clone({
    withCredentials: true,
    setHeaders: unsafe && csrf ? { 'X-CSRF-Token': csrf } : {},
  });

  return next(outgoing).pipe(
    catchError((err: HttpErrorResponse) => {
      const isAuthCall = req.url.includes('/auth/');
      if (err.status === 401 && !isAuthCall) {
        auth.session.set(null);
        void router.navigateByUrl('/login');
      }
      return throwError(() => err);
    }),
  );
};

export const authGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  return auth.isLoggedIn() ? true : inject(Router).parseUrl('/login');
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  return auth.isLoggedIn() ? inject(Router).parseUrl('/') : true;
};
