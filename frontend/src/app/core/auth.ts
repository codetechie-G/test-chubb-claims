import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { catchError, Observable, tap, throwError } from 'rxjs';
import { LoginResponse, Role, User } from './api.models';

const STORAGE_KEY = 'claims.session';

/** Holds the signed-in user. Session-scoped storage so closing the tab signs out. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<LoginResponse | null>(this.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly isHandler = computed(() => this.user()?.role === 'Handler');

  demoUsers(): Observable<User[]> {
    return this.http.get<User[]>('/api/auth/demo-users');
  }

  login(username: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', { username }).pipe(
      tap((res) => {
        this.session.set(res);
        try {
          sessionStorage.setItem(STORAGE_KEY, JSON.stringify(res));
        } catch {
          /* storage unavailable: session lives in memory only */
        }
      }),
    );
  }

  logout(): void {
    this.session.set(null);
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      /* ignore */
    }
    this.router.navigate(['/login']);
  }

  homeFor(role: Role | undefined): string {
    return role === 'Handler' ? '/staff' : role === 'Claimant' ? '/claimant' : '/login';
  }

  private restore(): LoginResponse | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as LoginResponse) : null;
    } catch {
      return null;
    }
  }
}

/** Attaches the bearer token; an expired/invalid token sends the user back to login. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token();
  const authed = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
  return next(authed).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && token) auth.logout();
      return throwError(() => err);
    }),
  );
};

/**
 * Route guard: only users with the given role can even load that feature area. This is UX only;
 * the API enforces the same rules independently.
 */
export const requireRole =
  (role: Role): CanMatchFn =>
  () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const user = auth.user();
    if (user?.role === role) return true;
    return router.parseUrl(user ? auth.homeFor(user.role) : '/login');
  };
