import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const apiOrigin = new URL(environment.apiUrl || document.baseURI, document.baseURI).origin;
  const requestUrl = new URL(req.url, document.baseURI);
  const isApiRequest = requestUrl.origin === apiOrigin && requestUrl.pathname.startsWith('/api/');
  if (!isApiRequest) return next(req);

  const operation = requestUrl.pathname.replace(/\/+$/, '');
  const isPublicAuth = operation === '/api/auth/login'
    || operation === '/api/auth/refresh'
    || operation === '/api/auth/logout';
  const token = auth.accessToken();
  let outgoing = req.clone({ withCredentials: true });
  if (token && !isPublicAuth) {
    outgoing = outgoing.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }

  return next(outgoing).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 || isPublicAuth) return throwError(() => err);

      return auth.refresh().pipe(
        switchMap(() => {
          const retry = outgoing.clone({
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
