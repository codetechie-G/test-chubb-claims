import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal, Signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, map, Observable, switchMap, timer } from 'rxjs';
import {
  ClaimDetail,
  ClaimStatus,
  ClaimSummary,
  Exposure,
  Market,
  Notification,
  PagedResult,
  Problem,
  SubmitClaimRequest,
  Workload,
} from './api.models';

/** Anything that identifies a claim at a specific version (detail or list row). */
export type Versioned = Pick<ClaimDetail, 'id' | 'version'>;

export interface ClaimQuery {
  scope?: 'queue' | 'mine' | 'all';
  status?: ClaimStatus | '';
  market?: string;
  page?: number;
}

@Injectable({ providedIn: 'root' })
export class ClaimsApi {
  private readonly http = inject(HttpClient);

  markets(): Observable<Market[]> {
    return this.http.get<Market[]>('/api/markets');
  }

  list(query: ClaimQuery = {}): Observable<PagedResult<ClaimSummary>> {
    let params = new HttpParams();
    for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== '') params = params.set(k, String(v));
    return this.http.get<PagedResult<ClaimSummary>>('/api/claims', { params });
  }

  get(id: string): Observable<ClaimDetail> {
    return this.http.get<ClaimDetail>(`/api/claims/${id}`);
  }

  submit(body: SubmitClaimRequest): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>('/api/claims', body);
  }

  respond(claimId: string, requestId: string, response: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`/api/claims/${claimId}/information-requests/${requestId}/response`, { response });
  }

  // Staff commands send If-Match with the version the user was looking at, so acting on a
  // stale screen fails with 412 instead of silently overwriting someone else's change.
  pickUp(c: Versioned) {
    return this.command(c, 'post', 'pickup', {});
  }
  updateReserve(c: Versioned, amount: number) {
    return this.command(c, 'put', 'reserve', { amount });
  }
  requestInformation(c: Versioned, question: string) {
    return this.command(c, 'post', 'information-requests', { question });
  }
  approve(c: Versioned, settlementAmount: number, note: string | null) {
    return this.command(c, 'post', 'approve', { settlementAmount, note });
  }
  settle(c: Versioned) {
    return this.command(c, 'post', 'settle', {});
  }
  reject(c: Versioned, reason: string) {
    return this.command(c, 'post', 'reject', { reason });
  }

  notifications(): Observable<Notification[]> {
    return this.http.get<Notification[]>('/api/notifications');
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>('/api/notifications/read-all', {});
  }

  workload(): Observable<Workload> {
    return this.http.get<Workload>('/api/dashboard/workload');
  }

  exposure(): Observable<Exposure> {
    return this.http.get<Exposure>('/api/dashboard/exposure');
  }

  private command(c: Versioned, method: 'post' | 'put', action: string, body: unknown): Observable<ClaimDetail> {
    const headers = new HttpHeaders({ 'If-Match': `"${c.version}"` });
    return this.http.request<ClaimDetail>(method.toUpperCase(), `/api/claims/${c.id}/${action}`, { body, headers });
  }
}

/** Turns an API error into a sentence for the user (ProblemDetails aware). */
export function describeError(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const p = err.error as Problem | null;
    if (p?.errors) return Object.values(p.errors).flat().join(' ');
    if (p?.detail) return p.detail;
    if (p?.title) return p.title;
    if (err.status === 0) return 'Cannot reach the server. Is the API running?';
    return `Request failed (${err.status}).`;
  }
  return 'Something went wrong.';
}

/**
 * Re-runs a request on an interval while the component is alive. Claims change on human
 * timescales (minutes/hours), so a short poll is a deliberate, simple choice over SSE/WebSockets.
 * Pass the source's input as a signal so the poll restarts immediately when it changes
 * (e.g. navigating between claims reuses the same detail component).
 */
export function poll<K, T>(
  key: Signal<K>,
  source: (key: K) => Observable<T>,
  onError?: (e: unknown) => void,
  intervalMs = 10_000,
): Observable<T> {
  const destroyRef = inject(DestroyRef);
  return toObservable(key).pipe(
    switchMap((k) => timer(0, intervalMs).pipe(map(() => k))),
    // A failed tick is reported but doesn't kill the loop; the next tick retries.
    switchMap((k) => source(k).pipe(catchError((e) => (onError?.(e), EMPTY)))),
    takeUntilDestroyed(destroyRef),
  );
}

/** For polls that don't depend on any input. */
export const NO_KEY: Signal<null> = signal(null).asReadonly();
