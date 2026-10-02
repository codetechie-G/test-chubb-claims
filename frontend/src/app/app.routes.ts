import { inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { AuthService, requireRole } from './core/auth';

// One Angular app, two audiences: each audience is a lazily loaded feature area behind a
// role guard, so a claimant never downloads the staff bundle and vice versa.
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'claimant',
    canMatch: [requireRole('Claimant')],
    loadChildren: () => import('./features/claimant/claimant.routes').then((m) => m.CLAIMANT_ROUTES),
  },
  {
    path: 'staff',
    canMatch: [requireRole('Handler')],
    loadChildren: () => import('./features/staff/staff.routes').then((m) => m.STAFF_ROUTES),
  },
  {
    path: '**',
    redirectTo: () => {
      const auth = inject(AuthService);
      return inject(Router).parseUrl(auth.homeFor(auth.user()?.role));
    },
  },
];
