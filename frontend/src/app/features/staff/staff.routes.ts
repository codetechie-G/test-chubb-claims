import { Routes } from '@angular/router';

export const STAFF_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'claims' },
  { path: 'claims', loadComponent: () => import('./claims-list.page').then((m) => m.StaffClaimsListPage) },
  { path: 'claims/:id', loadComponent: () => import('./claim-detail.page').then((m) => m.StaffClaimDetailPage) },
  { path: 'dashboard', loadComponent: () => import('./dashboard.page').then((m) => m.DashboardPage) },
];
