import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.accessToken();

  let outgoing = req.clone({ withCredentials: true });
  if (token && !req.url.includes('/api/auth/')) {
    outgoing = outgoing.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }

  return next(outgoing).pipe(
    catchError((err: HttpErrorResponse) => {
      const isAuthCall = req.url.includes('/api/auth/');
      if (err.status !== 401 || isAuthCall) {
        return throwError(() => err);
      }

      return auth.refresh().pipe(
        switchMap(() => {
          const retry = req.clone({
            withCredentials: true,
            setHeaders: { Authorization: `Bearer ${auth.accessToken() ?? ''}` },
          });
          return next(retry);
        }),
        catchError((refreshErr) => {
          auth.clear();
          void router.navigate(['/login']);
          return throwError(() => refreshErr);
        }),
      );
    }),
  );
};
