import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'pos' },
      { path: 'pos', loadComponent: () => import('./features/pos/pos').then((m) => m.Pos) },
      { path: 'shifts', loadComponent: () => import('./features/shifts/shifts').then((m) => m.Shifts) },
      {
        path: 'dashboard',
        canActivate: [roleGuard('Admin', 'Manager')],
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'inventory',
        canActivate: [roleGuard('Admin', 'Manager')],
        loadComponent: () => import('./features/inventory/inventory').then((m) => m.Inventory),
      },
      {
        path: 'history',
        canActivate: [roleGuard('Admin', 'Manager')],
        loadComponent: () => import('./features/history/history').then((m) => m.History),
      },
      {
        path: 'settings',
        canActivate: [roleGuard('Admin', 'Manager')],
        loadComponent: () => import('./features/settings/settings').then((m) => m.Settings),
      },
      {
        path: 'sales',
        loadComponent: () => import('./features/sales/sales').then((m) => m.Sales),
      },
    ],
  },
  { path: '**', redirectTo: 'pos' },
];
