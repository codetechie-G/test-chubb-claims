import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { catchError, of } from 'rxjs';
import { User } from '../../core/api.models';
import { AuthService } from '../../core/auth';
import { describeError } from '../../core/claims-api';

@Component({
  selector: 'app-login',
  template: `
    <section class="login">
      <h1>Sign in</h1>
      <p class="muted">Demo identities: pick a claimant or a member of claims staff. Real deployments would use SSO / a customer identity provider.</p>
      @if (error()) {
        <div class="alert error">{{ error() }}</div>
      }
      <div class="login-grid">
        <div>
          <h3>Claimants</h3>
          @for (u of byRole('Claimant'); track u.id) {
            <button class="user-tile" (click)="signIn(u)" [disabled]="busy()">{{ u.name }}</button>
          }
        </div>
        <div>
          <h3>Claims staff</h3>
          @for (u of byRole('Handler'); track u.id) {
            <button class="user-tile" (click)="signIn(u)" [disabled]="busy()">{{ u.name }}</button>
          }
        </div>
      </div>
    </section>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly users = toSignal(
    this.auth.demoUsers().pipe(catchError((e) => (this.error.set(describeError(e)), of([] as User[])))),
    { initialValue: [] as User[] },
  );

  protected byRole(role: User['role']): User[] {
    return this.users().filter((u) => u.role === role);
  }

  protected signIn(u: User): void {
    this.busy.set(true);
    this.error.set(null);
    this.auth.login(u.username).subscribe({
      next: (res) => this.router.navigateByUrl(this.auth.homeFor(res.user.role)),
      error: (e) => {
        this.error.set(describeError(e));
        this.busy.set(false);
      },
    });
  }
}
