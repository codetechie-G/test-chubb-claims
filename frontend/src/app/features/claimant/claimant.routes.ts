import { Routes } from '@angular/router';

export const CLAIMANT_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./my-claims.page').then((m) => m.MyClaimsPage) },
  { path: 'new', loadComponent: () => import('./new-claim.page').then((m) => m.NewClaimPage) },
  { path: 'claims/:id', loadComponent: () => import('./claim-detail.page').then((m) => m.ClaimantClaimDetailPage) },
];
