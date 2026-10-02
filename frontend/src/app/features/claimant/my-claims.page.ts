import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ClaimSummary, Notification } from '../../core/api.models';
import { ClaimsApi, describeError, NO_KEY, poll } from '../../core/claims-api';
import { formatDate, formatDateTime, StatusBadge } from '../../shared/ui';

@Component({
  selector: 'app-my-claims',
  imports: [RouterLink, StatusBadge],
  template: `
    <div class="page-head">
      <h1>My claims</h1>
      <a class="btn primary" routerLink="/claimant/new">Report an incident</a>
    </div>
    @if (error()) {
      <div class="alert error">{{ error() }}</div>
    }

    <div class="split">
      <section>
        @if (actionNeeded().length) {
          <div class="alert warn">
            We need more information on {{ actionNeeded().length }} claim(s). Open the claim to respond.
          </div>
        }
        @if (claims() === null) {
          <p class="muted">Loading…</p>
        } @else if (claims()!.length === 0) {
          <div class="card empty">
            <p>You haven't reported any incidents yet.</p>
            <a class="btn primary" routerLink="/claimant/new">Report an incident</a>
          </div>
        } @else {
          <div class="card-list">
            @for (c of claims(); track c.id) {
              <a class="card claim-card" [routerLink]="['/claimant/claims', c.id]">
                <div class="row">
                  <strong>{{ c.reference }}</strong>
                  <app-status [status]="c.status" />
                </div>
                <div class="muted">
                  {{ c.type }} · {{ c.marketCode }} · submitted {{ date(c.submittedAt) }}
                  @if (c.assigneeName) {
                    · handled by {{ c.assigneeName }}
                  }
                </div>
                @if (c.openInformationRequests > 0) {
                  <div class="tag warn">Action needed: {{ c.openInformationRequests }} question(s) for you</div>
                }
              </a>
            }
          </div>
        }
      </section>

      <aside class="card updates">
        <div class="row">
          <h3>Updates</h3>
          @if (unread() > 0) {
            <button class="btn ghost small" (click)="markRead()">Mark all read</button>
          }
        </div>
        <div class="updates-list">
        @for (n of notifications(); track n.id) {
          <div class="note" [class.unread]="!n.read">
            <a [routerLink]="['/claimant/claims', n.claimId]">{{ n.message }}</a>
            <div class="muted small">{{ dateTime(n.createdAt) }}</div>
          </div>
        } @empty {
          <p class="muted">No updates yet.</p>
        }
        </div>
      </aside>
    </div>
  `,
})
export class MyClaimsPage {
  private readonly api = inject(ClaimsApi);
  protected readonly claims = signal<ClaimSummary[] | null>(null);
  protected readonly notifications = signal<Notification[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly unread = computed(() => this.notifications().filter((n) => !n.read).length);
  protected readonly actionNeeded = computed(() => (this.claims() ?? []).filter((c) => c.openInformationRequests > 0));
  protected readonly date = formatDate;
  protected readonly dateTime = formatDateTime;

  constructor() {
    const onError = (e: unknown) => this.error.set(describeError(e));
    poll(NO_KEY, () => this.api.list(), onError).subscribe((r) => {
      this.claims.set(r.items);
      this.error.set(null);
    });
    poll(NO_KEY, () => this.api.notifications(), onError).subscribe((n) => this.notifications.set(n));
  }

  protected markRead(): void {
    this.api.markAllRead().subscribe(() => this.notifications.update((ns) => ns.map((n) => ({ ...n, read: true }))));
  }
}
