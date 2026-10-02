import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ClaimDetail } from '../../core/api.models';
import { AuthService } from '../../core/auth';
import { ClaimsApi, describeError, poll } from '../../core/claims-api';
import { formatDate, formatDateTime, formatMoney, StatusBadge, Timeline } from '../../shared/ui';

type Panel = 'reserve' | 'info' | 'approve' | 'reject' | null;

@Component({
  selector: 'app-staff-claim-detail',
  imports: [RouterLink, FormsModule, StatusBadge, Timeline],
  template: `
    <a routerLink="/staff/claims" class="back">← Claims</a>
    @if (error()) {
      <div class="alert error">{{ error() }}</div>
    }

    @if (claim(); as c) {
      <div class="page-head">
        <div>
          <h1>{{ c.reference }}</h1>
          <div class="muted">{{ c.type }} · {{ c.marketCode }} · {{ c.claimantName }}</div>
        </div>
        <app-status [status]="c.status" />
      </div>

      <div class="stats">
        <div class="stat"><span class="muted">Claimed</span><strong>{{ money(c.claimedAmount, c.currency) }}</strong></div>
        <div class="stat"><span class="muted">Reserve (exposure)</span><strong>{{ money(c.reserveAmount, c.currency) }}</strong></div>
        <div class="stat"><span class="muted">Settlement</span><strong>{{ money(c.settlementAmount, c.currency) }}</strong></div>
        <div class="stat"><span class="muted">Handler</span><strong>{{ c.assigneeName ?? 'Unassigned' }}</strong></div>
      </div>

      <!-- Actions: only what the current state machine allows for this user -->
      <section class="card actions-card">
        @if (c.status === 'Submitted') {
          <p>This claim is waiting in the queue.</p>
          <button class="btn primary" [disabled]="busy()" (click)="run(api.pickUp(c))">Pick up claim</button>
        } @else if (!isMine()) {
          <p class="muted">
            @if (isOpen()) {
              Assigned to {{ c.assigneeName }}. Only the assigned handler can act on this claim.
            } @else {
              This claim is closed.
            }
          </p>
        } @else if (isOpen()) {
          <div class="btn-row">
            @if (c.status === 'UnderAssessment' || c.status === 'AwaitingInformation') {
              <button class="btn" [class.active]="panel() === 'reserve'" (click)="toggle('reserve', c)">Adjust reserve</button>
              <button class="btn" [class.active]="panel() === 'info'" (click)="toggle('info', c)">Request information</button>
            }
            @if (c.status === 'UnderAssessment') {
              <button class="btn primary" [class.active]="panel() === 'approve'" (click)="toggle('approve', c)">Approve</button>
            }
            @if (c.status === 'UnderAssessment' || c.status === 'AwaitingInformation') {
              <button class="btn danger" [class.active]="panel() === 'reject'" (click)="toggle('reject', c)">Decline</button>
            }
            @if (c.status === 'Approved') {
              <button class="btn primary" [disabled]="busy()" (click)="run(api.settle(c))">
                Mark settled ({{ money(c.settlementAmount, c.currency) }} paid)
              </button>
            }
          </div>
          @if (c.status === 'AwaitingInformation') {
            <p class="muted small">Approval is unavailable until the claimant answers the open questions.</p>
          }

          @switch (panel()) {
            @case ('reserve') {
              <div class="panel">
                <label>New reserve ({{ c.currency }})
                  <input type="number" min="0" [(ngModel)]="amount" />
                </label>
                <button class="btn primary" [disabled]="busy() || amount === null || amount < 0" (click)="run(api.updateReserve(c, amount!))">Save reserve</button>
              </div>
            }
            @case ('info') {
              <div class="panel">
                <label>Question for the claimant
                  <textarea rows="3" [(ngModel)]="text" placeholder="e.g. Please upload the police report"></textarea>
                </label>
                <button class="btn primary" [disabled]="busy() || !text.trim()" (click)="run(api.requestInformation(c, text.trim()))">Send request</button>
              </div>
            }
            @case ('approve') {
              <div class="panel">
                <label>Settlement amount ({{ c.currency }})
                  <input type="number" min="1" [(ngModel)]="amount" />
                </label>
                <label>Note (optional)
                  <input type="text" [(ngModel)]="text" />
                </label>
                <button class="btn primary" [disabled]="busy() || !amount || amount <= 0" (click)="run(api.approve(c, amount!, text.trim() || null))">Confirm approval</button>
              </div>
            }
            @case ('reject') {
              <div class="panel">
                <label>Reason (shown to the claimant)
                  <textarea rows="3" [(ngModel)]="text"></textarea>
                </label>
                <button class="btn danger" [disabled]="busy() || !text.trim()" (click)="run(api.reject(c, text.trim()))">Confirm decline</button>
              </div>
            }
          }
        } @else {
          <p class="muted">Closed {{ c.closedAt ? dateTime(c.closedAt) : '' }}. {{ c.decisionReason }}</p>
        }
      </section>

      <div class="split">
        <section class="card">
          <h3>Incident</h3>
          <dl class="kv">
            <dt>Incident date</dt><dd>{{ date(c.incidentDate) }}</dd>
            <dt>Location</dt><dd>{{ c.incidentLocation }}</dd>
            <dt>Submitted</dt><dd>{{ dateTime(c.submittedAt) }}</dd>
          </dl>
          <p class="pre">{{ c.description }}</p>

          <h3>Information requests</h3>
          @for (r of c.informationRequests; track r.id) {
            <div class="qa">
              <div><strong>Q:</strong> {{ r.question }} <span class="muted small">({{ r.requestedByName }}, {{ dateTime(r.requestedAt) }})</span></div>
              @if (r.status === 'Answered') {
                <div><strong>A:</strong> {{ r.response }} <span class="muted small">({{ dateTime(r.respondedAt!) }})</span></div>
              } @else {
                <div class="tag warn">Awaiting claimant</div>
              }
            </div>
          } @empty {
            <p class="muted">None.</p>
          }
        </section>
        <section class="card">
          <h3>Audit trail</h3>
          <app-timeline [history]="c.history" />
        </section>
      </div>
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class StaffClaimDetailPage {
  protected readonly api = inject(ClaimsApi);
  private readonly auth = inject(AuthService);
  readonly id = input.required<string>();

  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly panel = signal<Panel>(null);
  protected amount: number | null = null;
  protected text = '';

  protected readonly isMine = computed(() => this.claim()?.assigneeId === this.auth.user()?.id);
  protected readonly isOpen = computed(() => !['Settled', 'Rejected'].includes(this.claim()?.status ?? ''));
  protected readonly date = formatDate;
  protected readonly dateTime = formatDateTime;
  protected readonly money = formatMoney;

  constructor() {
    poll(this.id, (id) => this.api.get(id), (e) => this.error.set(describeError(e))).subscribe((c) => {
      // Don't yank the form out from under a user mid-edit: only refresh while no panel is open.
      if (this.panel() === null || this.claim()?.id !== c.id) this.claim.set(c);
    });
  }

  protected toggle(p: Exclude<Panel, null>, c: ClaimDetail): void {
    const next = this.panel() === p ? null : p;
    this.panel.set(next);
    this.text = '';
    this.amount = p === 'reserve' ? c.reserveAmount : p === 'approve' ? (c.reserveAmount ?? c.claimedAmount) : null;
  }

  protected run(command: Observable<ClaimDetail>): void {
    this.busy.set(true);
    this.error.set(null);
    command.subscribe({
      next: (updated) => {
        this.claim.set(updated);
        this.panel.set(null);
        this.busy.set(false);
      },
      error: (e) => {
        this.error.set(describeError(e));
        this.busy.set(false);
        // 409/412 = someone else changed it; show the latest state so the user can decide again.
        if (e instanceof HttpErrorResponse && (e.status === 409 || e.status === 412)) {
          this.panel.set(null);
          this.api.get(this.id()).subscribe((c) => this.claim.set(c));
        }
      },
    });
  }
}
