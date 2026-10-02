import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ClaimDetail, ClaimStatus } from '../../core/api.models';
import { ClaimsApi, describeError, poll } from '../../core/claims-api';
import { formatDate, formatDateTime, formatMoney, StatusBadge, Timeline } from '../../shared/ui';

const PROGRESS: ClaimStatus[] = ['Submitted', 'UnderAssessment', 'Approved', 'Settled'];

@Component({
  selector: 'app-claimant-claim-detail',
  imports: [RouterLink, FormsModule, StatusBadge, Timeline],
  template: `
    <a routerLink="/claimant" class="back">← My claims</a>
    @if (error()) {
      <div class="alert error">{{ error() }}</div>
    }
    @if (justSubmitted) {
      <div class="alert ok">Thanks — your claim has been submitted. We'll notify you here as it progresses.</div>
    }

    @if (claim(); as c) {
      <div class="page-head">
        <h1>{{ c.reference }}</h1>
        <app-status [status]="c.status" />
      </div>

      @if (c.status !== 'Rejected') {
        <ol class="progress">
          @for (step of progress; track step; let i = $index) {
            <li [class.done]="i <= stepIndex()" [class.current]="i === stepIndex()">{{ stepLabel(step) }}</li>
          }
        </ol>
      }

      @if (c.status === 'Approved') {
        <div class="alert ok">Approved for {{ money(c.settlementAmount, c.currency) }}. Payment is being processed.</div>
      } @else if (c.status === 'Settled') {
        <div class="alert ok">Paid: {{ money(c.settlementAmount, c.currency) }}.</div>
      } @else if (c.status === 'Rejected') {
        <div class="alert error"><strong>Your claim was declined.</strong> {{ c.decisionReason }}</div>
      }

      @for (r of openRequests(); track r.id) {
        <div class="card highlight">
          <h3>We need more information</h3>
          <p>{{ r.question }}</p>
          <p class="muted small">Asked by {{ r.requestedByName }} · {{ dateTime(r.requestedAt) }}</p>
          <textarea rows="3" [(ngModel)]="answers[r.id]" placeholder="Your answer"></textarea>
          <div class="actions">
            <button class="btn primary" [disabled]="busy() || !answers[r.id]?.trim()" (click)="respond(c, r.id)">Send answer</button>
          </div>
        </div>
      }

      <div class="split">
        <section class="card">
          <h3>Claim details</h3>
          <dl class="kv">
            <dt>Type</dt><dd>{{ c.type }}</dd>
            <dt>Country</dt><dd>{{ c.marketCode }}</dd>
            <dt>Incident date</dt><dd>{{ date(c.incidentDate) }}</dd>
            <dt>Location</dt><dd>{{ c.incidentLocation }}</dd>
            <dt>Your estimate</dt><dd>{{ money(c.claimedAmount, c.currency) }}</dd>
            <dt>Handled by</dt><dd>{{ c.assigneeName ?? 'Waiting to be assigned' }}</dd>
          </dl>
          <p class="pre">{{ c.description }}</p>
          @if (answered().length) {
            <h4>Information you've provided</h4>
            @for (r of answered(); track r.id) {
              <div class="qa"><div class="muted">{{ r.question }}</div><div>{{ r.response }}</div></div>
            }
          }
        </section>
        <section class="card">
          <h3>History</h3>
          <app-timeline [history]="c.history" />
        </section>
      </div>
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class ClaimantClaimDetailPage {
  private readonly api = inject(ClaimsApi);
  readonly id = input.required<string>();

  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly answers: Record<string, string> = {};
  protected readonly justSubmitted = !!history.state?.justSubmitted;
  protected readonly progress = PROGRESS;

  protected readonly openRequests = computed(() => this.claim()?.informationRequests.filter((r) => r.status === 'Open') ?? []);
  protected readonly answered = computed(() => this.claim()?.informationRequests.filter((r) => r.status === 'Answered') ?? []);
  protected readonly stepIndex = computed(() => {
    const s = this.claim()?.status;
    return s === 'AwaitingInformation' ? 1 : PROGRESS.indexOf(s as ClaimStatus);
  });

  protected readonly date = formatDate;
  protected readonly dateTime = formatDateTime;
  protected readonly money = formatMoney;
  protected stepLabel = (s: ClaimStatus) =>
    ({ Submitted: 'Submitted', UnderAssessment: 'Being assessed', Approved: 'Approved', Settled: 'Paid' })[s as string] ?? s;

  constructor() {
    poll(this.id, (id) => this.api.get(id), (e) => this.error.set(describeError(e))).subscribe((c) => {
      this.claim.set(c);
      this.error.set(null);
    });
  }

  protected respond(c: ClaimDetail, requestId: string): void {
    this.busy.set(true);
    this.api.respond(c.id, requestId, this.answers[requestId].trim()).subscribe({
      next: (updated) => {
        this.claim.set(updated);
        delete this.answers[requestId];
        this.busy.set(false);
      },
      error: (e) => {
        this.error.set(describeError(e));
        this.busy.set(false);
      },
    });
  }
}
