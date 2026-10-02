import { Component, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ClaimStatus, ClaimSummary, Market, PagedResult } from '../../core/api.models';
import { ClaimQuery, ClaimsApi, describeError, poll } from '../../core/claims-api';
import { ALL_STATUSES, formatMoney, parseUtc, StatusBadge, statusLabel } from '../../shared/ui';

type Tab = 'queue' | 'mine' | 'all';

@Component({
  selector: 'app-staff-claims',
  imports: [RouterLink, StatusBadge],
  template: `
    <div class="page-head">
      <h1>Claims</h1>
    </div>

    <div class="tabs">
      <button [class.active]="tab() === 'queue'" (click)="setTab('queue')">Unassigned queue</button>
      <button [class.active]="tab() === 'mine'" (click)="setTab('mine')">My open claims</button>
      <button [class.active]="tab() === 'all'" (click)="setTab('all')">All claims</button>
    </div>

    <div class="filters">
      @if (tab() === 'all') {
        <select [value]="status()" (change)="status.set($any($event.target).value); page.set(1)">
          <option value="">Any status</option>
          @for (s of statuses; track s) {
            <option [value]="s">{{ label(s) }}</option>
          }
        </select>
      }
      <select [value]="market()" (change)="market.set($any($event.target).value); page.set(1)">
        <option value="">All markets</option>
        @for (m of markets(); track m.code) {
          <option [value]="m.code">{{ m.name }}</option>
        }
      </select>
      <span class="muted">{{ result()?.total ?? '…' }} claims · refreshes every 10s</span>
    </div>

    @if (error()) {
      <div class="alert error">{{ error() }}</div>
    }

    <div class="card table-wrap">
      <table>
        <thead>
          <tr>
            <th>Reference</th>
            <th>Type</th>
            <th>Market</th>
            <th>Claimant</th>
            <th>Status</th>
            <th>Handler</th>
            <th class="num">Reserve</th>
            <th class="num">Age</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          @for (c of result()?.items ?? []; track c.id) {
            <tr>
              <td><a [routerLink]="['/staff/claims', c.id]">{{ c.reference }}</a></td>
              <td>{{ c.type }}</td>
              <td>{{ c.marketCode }}</td>
              <td>{{ c.claimantName }}</td>
              <td>
                <app-status [status]="c.status" />
                @if (c.openInformationRequests > 0) {
                  <span class="muted small"> ({{ c.openInformationRequests }} open)</span>
                }
              </td>
              <td>{{ c.assigneeName ?? '—' }}</td>
              <td class="num">{{ money(c.reserveAmount, c.currency) }}</td>
              <td class="num" [class.overdue]="age(c) > 7 && c.status === 'Submitted'">{{ age(c) }}d</td>
              <td class="num">
                @if (c.status === 'Submitted') {
                  <button class="btn small primary" [disabled]="busyId() === c.id" (click)="pickUp(c)">Pick up</button>
                }
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="9" class="muted center">
                {{ result() ? (tab() === 'queue' ? 'Queue is empty. Nice work.' : 'No claims match.') : 'Loading…' }}
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>

    @if ((result()?.total ?? 0) > pageSize) {
      <div class="pager">
        <button class="btn ghost small" [disabled]="page() === 1" (click)="page.set(page() - 1)">Previous</button>
        <span class="muted">Page {{ page() }} of {{ pages() }}</span>
        <button class="btn ghost small" [disabled]="page() >= pages()" (click)="page.set(page() + 1)">Next</button>
      </div>
    }
  `,
})
export class StaffClaimsListPage {
  private readonly api = inject(ClaimsApi);
  private readonly router = inject(Router);

  /** Bound from ?tab= so the selected tab survives navigating into a claim and back. */
  readonly initialTab = input<Tab | undefined>(undefined, { alias: 'tab' });
  protected readonly tabOverride = signal<Tab | null>(null);
  protected readonly tab = computed<Tab>(() => this.tabOverride() ?? this.initialTab() ?? 'queue');
  protected readonly status = signal<ClaimStatus | ''>('');
  protected readonly market = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = 25;

  protected readonly result = signal<PagedResult<ClaimSummary> | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busyId = signal<string | null>(null);
  protected readonly markets = toSignal(this.api.markets(), { initialValue: [] as Market[] });
  protected readonly pages = computed(() => Math.ceil((this.result()?.total ?? 0) / this.pageSize));
  protected readonly statuses = ALL_STATUSES;
  protected readonly label = statusLabel;
  protected readonly money = formatMoney;

  private readonly query = computed<ClaimQuery>(() => ({
    scope: this.tab(),
    status: this.tab() === 'all' ? this.status() : '',
    market: this.market(),
    page: this.page(),
  }));

  constructor() {
    poll(this.query, (q) => this.api.list(q), (e) => this.error.set(describeError(e))).subscribe((r) => {
      this.result.set(r);
      this.error.set(null);
    });
  }

  protected setTab(tab: Tab): void {
    this.tabOverride.set(tab);
    this.page.set(1);
    this.result.set(null);
    this.router.navigate([], { queryParams: { tab }, replaceUrl: true });
  }

  protected age(c: ClaimSummary): number {
    return Math.floor((Date.now() - parseUtc(c.submittedAt).getTime()) / 86_400_000);
  }

  protected pickUp(c: ClaimSummary): void {
    this.busyId.set(c.id);
    this.api.pickUp(c).subscribe({
      next: () => this.router.navigate(['/staff/claims', c.id]),
      error: (e) => {
        this.error.set(describeError(e));
        this.busyId.set(null);
        // Most likely someone else picked it up first: drop it from the queue view.
        this.result.update((r) => (r ? { ...r, items: r.items.filter((i) => i.id !== c.id) } : r));
      },
    });
  }
}
