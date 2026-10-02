import { Component, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { Exposure, Workload } from '../../core/api.models';
import { ClaimsApi, describeError, NO_KEY, poll } from '../../core/claims-api';
import { formatDateTime, formatMoney, statusLabel } from '../../shared/ui';

/**
 * Team workload + outstanding liability. Served from the event-fed read model, so figures are
 * eventually consistent; "as of" makes that explicit rather than pretending it's real-time.
 */
@Component({
  selector: 'app-dashboard',
  template: `
    <div class="page-head">
      <h1>Team dashboard</h1>
      @if (exposure()?.asOf; as asOf) {
        <span class="muted small">Data as of {{ dateTime(asOf) }} · refreshes every 15s</span>
      }
    </div>
    @if (error()) {
      <div class="alert error">{{ error() }}</div>
    }

    @if (workload() && exposure()) {
      <div class="tiles">
        <div class="tile">
          <span class="tile-label">Open claims</span>
          <span class="tile-value">{{ exposure()!.openClaims }}</span>
        </div>
        <div class="tile">
          <span class="tile-label">Unassigned in queue</span>
          <span class="tile-value">{{ workload()!.queue.unassigned }}</span>
          @if (workload()!.queue.oldestUnassignedAgeDays !== null) {
            <span class="tile-sub" [class.overdue]="workload()!.queue.oldestUnassignedAgeDays! > 5">
              oldest waiting {{ workload()!.queue.oldestUnassignedAgeDays }} days
            </span>
          }
        </div>
        <div class="tile">
          <span class="tile-label">Outstanding liability ({{ exposure()!.reportingCurrency }})</span>
          <span class="tile-value">{{ compact(exposure()!.totalReserveInReportingCurrency, exposure()!.reportingCurrency) }}</span>
          <span class="tile-sub">sum of open-claim reserves</span>
        </div>
      </div>

      <section class="card">
        <h3>Workload by handler</h3>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Handler</th>
                <th class="num">Open</th>
                <th class="num">Assessing</th>
                <th class="num">Awaiting info</th>
                <th class="num">Approved (to pay)</th>
                <th class="num">Oldest open</th>
                <th class="num">Closed (30d)</th>
                <th class="num">Avg days to close</th>
              </tr>
            </thead>
            <tbody>
              @for (h of workload()!.handlers; track h.handlerId) {
                <tr>
                  <td>{{ h.handlerName }}</td>
                  <td class="num"><strong>{{ h.open }}</strong></td>
                  <td class="num">{{ h.underAssessment }}</td>
                  <td class="num">{{ h.awaitingInformation }}</td>
                  <td class="num">{{ h.approved }}</td>
                  <td class="num" [class.overdue]="(h.oldestOpenAgeDays ?? 0) > 14">{{ days(h.oldestOpenAgeDays) }}</td>
                  <td class="num">{{ h.closedLast30Days }}</td>
                  <td class="num">{{ days(h.avgDaysToCloseLast30Days) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <section class="card">
        <h3>Outstanding liability by market</h3>
        <p class="muted small">{{ exposure()!.fxNote }}</p>
        <div class="bars" role="img" [attr.aria-label]="'Outstanding reserve by market in ' + exposure()!.reportingCurrency">
          @for (m of exposure()!.markets; track m.marketCode) {
            <div class="bar-row">
              <span class="bar-label">{{ m.marketName }}</span>
              <div class="bar-track">
                @if (m.reserveInReportingCurrency > 0) {
                  <div class="bar" [style.width.%]="(m.reserveInReportingCurrency / maxMarket()) * 100">
                    <span class="bar-tip">
                      {{ m.marketName }}: {{ money(m.reserveInReportingCurrency, exposure()!.reportingCurrency) }}
                      ({{ money(m.reserve, m.currency) }}) · {{ m.openClaims }} open
                    </span>
                  </div>
                }
              </div>
              <span class="bar-value">{{ compact(m.reserveInReportingCurrency, exposure()!.reportingCurrency) }}</span>
            </div>
          }
        </div>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Market</th>
                <th class="num">Open claims</th>
                <th class="num">Motor reserve</th>
                <th class="num">Property reserve</th>
                <th class="num">Total (local)</th>
                <th class="num">Total ({{ exposure()!.reportingCurrency }})</th>
              </tr>
            </thead>
            <tbody>
              @for (m of exposure()!.markets; track m.marketCode) {
                <tr>
                  <td>{{ m.marketName }}</td>
                  <td class="num">{{ m.openClaims }}</td>
                  <td class="num">{{ money(typeReserve(m, 'Motor'), m.currency) }}</td>
                  <td class="num">{{ money(typeReserve(m, 'Property'), m.currency) }}</td>
                  <td class="num"><strong>{{ money(m.reserve, m.currency) }}</strong></td>
                  <td class="num">{{ money(m.reserveInReportingCurrency, exposure()!.reportingCurrency) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <h4>By claim stage</h4>
        <div class="chips">
          @for (s of exposure()!.byStatus; track s.status) {
            <span class="chip">
              {{ label(s.status) }}: {{ s.openClaims }} · {{ compact(s.reserveInReportingCurrency, exposure()!.reportingCurrency) }}
            </span>
          }
        </div>
      </section>
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class DashboardPage {
  private readonly api = inject(ClaimsApi);
  protected readonly workload = signal<Workload | null>(null);
  protected readonly exposure = signal<Exposure | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly maxMarket = computed(() => Math.max(1, ...(this.exposure()?.markets ?? []).map((m) => m.reserveInReportingCurrency)));

  protected readonly money = formatMoney;
  protected readonly dateTime = formatDateTime;
  protected readonly label = statusLabel;

  constructor() {
    poll(NO_KEY, () => forkJoin({ w: this.api.workload(), e: this.api.exposure() }), (e) => this.error.set(describeError(e)), 15_000)
      .subscribe(({ w, e }) => {
        this.workload.set(w);
        this.exposure.set(e);
        this.error.set(null);
      });
  }

  protected compact(amount: number, currency: string): string {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 1 }).format(amount);
  }

  protected days(v: number | null): string {
    return v === null ? '—' : `${v}d`;
  }

  protected typeReserve(m: Exposure['markets'][number], type: 'Motor' | 'Property'): number {
    return m.byType.find((t) => t.type === type)?.reserve ?? 0;
  }
}
