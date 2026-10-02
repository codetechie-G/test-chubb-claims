import { Component, computed, input } from '@angular/core';
import { ClaimStatus } from '../core/api.models';

const STATUS_LABELS: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderAssessment: 'Under assessment',
  AwaitingInformation: 'Awaiting information',
  Approved: 'Approved',
  Settled: 'Settled',
  Rejected: 'Declined',
};

export const ALL_STATUSES = Object.keys(STATUS_LABELS) as ClaimStatus[];

export function statusLabel(s: ClaimStatus): string {
  return STATUS_LABELS[s] ?? s;
}

const EVENT_LABELS: Record<string, string> = {
  ClaimSubmitted: 'Submitted',
  ClaimAssigned: 'Assigned',
  ClaimReserveChanged: 'Reserve changed',
  ClaimInformationRequested: 'Information requested',
  ClaimInformationProvided: 'Information provided',
  ClaimApproved: 'Approved',
  ClaimSettled: 'Settled',
  ClaimRejected: 'Declined',
};

export function eventLabel(e: string): string {
  return EVENT_LABELS[e] ?? e;
}

@Component({
  selector: 'app-status',
  template: `<span class="badge" [class]="'badge status-' + status()">{{ label() }}</span>`,
})
export class StatusBadge {
  readonly status = input.required<ClaimStatus>();
  protected readonly label = computed(() => statusLabel(this.status()));
}

/** Timeline of a claim's history, newest first. */
@Component({
  selector: 'app-timeline',
  imports: [],
  template: `
    <ol class="timeline">
      @for (h of reversed(); track h.version) {
        <li>
          <div class="tl-head">
            <strong>{{ label(h.eventType) }}</strong>
            <span class="muted">{{ h.actorName }} · {{ fmt(h.occurredAt) }}</span>
          </div>
          @if (h.detail) {
            <div class="tl-detail">{{ h.detail }}</div>
          }
        </li>
      }
    </ol>
  `,
})
export class Timeline {
  readonly history = input.required<{ version: number; eventType: string; actorName: string; occurredAt: string; detail: string | null }[]>();
  protected readonly reversed = computed(() => [...this.history()].reverse());
  protected label = eventLabel;
  protected fmt = (iso: string) => formatDateTime(iso);
}

/** API timestamps are UTC; tolerate a missing offset rather than misreading them as local time. */
export function parseUtc(iso: string): Date {
  return new Date(/(Z|[+-]\d\d:\d\d)$/i.test(iso) ? iso : iso + 'Z');
}

/** Displayed in the viewer's local time zone. */
export function formatDateTime(iso: string): string {
  const d = parseUtc(iso);
  return d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

export function formatDate(iso: string): string {
  // Date-only values (incident date) are calendar dates, not instants: don't shift them.
  const d = iso.length === 10 ? new Date(iso + 'T00:00:00') : parseUtc(iso);
  return d.toLocaleDateString(undefined, { dateStyle: 'medium' });
}

export function formatMoney(amount: number | null | undefined, currency: string): string {
  if (amount === null || amount === undefined) return '—';
  return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 0 }).format(amount);
}
