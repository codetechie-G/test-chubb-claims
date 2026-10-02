import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <div class="brand">
        <span class="logo">◆</span>
        <span>Claims<span class="muted"> · APAC</span></span>
      </div>
      @if (auth.user(); as user) {
        <nav>
          @if (user.role === 'Claimant') {
            <a routerLink="/claimant" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">My claims</a>
            <a routerLink="/claimant/new" routerLinkActive="active">Report an incident</a>
          } @else {
            <a routerLink="/staff/claims" routerLinkActive="active">Claims</a>
            <a routerLink="/staff/dashboard" routerLinkActive="active">Team dashboard</a>
          }
        </nav>
        <div class="who">
          <span>{{ user.name }} <span class="muted">({{ user.role === 'Handler' ? 'Claims staff' : 'Claimant' }})</span></span>
          <button class="btn ghost" (click)="auth.logout()">Sign out</button>
        </div>
      }
    </header>
    <main class="container">
      <router-outlet />
    </main>
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
}
