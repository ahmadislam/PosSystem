import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isSignedIn()) return true;
  return router.createUrlTree(['/login']);
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isSignedIn()) return true;
  return router.createUrlTree([auth.isStaff() ? '/dashboard' : '/pos']);
};

export const roleGuard =
  (...roles: string[]): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (auth.hasRole(...roles)) return true;
    return router.createUrlTree(['/pos']);
  };
