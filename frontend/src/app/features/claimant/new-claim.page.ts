import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ClaimType, Market } from '../../core/api.models';
import { ClaimsApi, describeError } from '../../core/claims-api';

@Component({
  selector: 'app-new-claim',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="page-head">
      <h1>Report an incident</h1>
      <a routerLink="/claimant" class="btn ghost">Cancel</a>
    </div>

    <form class="card form" [formGroup]="form" (ngSubmit)="submit()">
      @if (error()) {
        <div class="alert error">{{ error() }}</div>
      }

      <fieldset class="choice">
        <legend>What happened?</legend>
        <label [class.selected]="form.value.type === 'Motor'">
          <input type="radio" formControlName="type" value="Motor" /> Motor (vehicle)
        </label>
        <label [class.selected]="form.value.type === 'Property'">
          <input type="radio" formControlName="type" value="Property" /> Property (home / contents)
        </label>
      </fieldset>

      <div class="grid2">
        <label>
          Country of policy
          <select formControlName="marketCode">
            @for (m of markets(); track m.code) {
              <option [value]="m.code">{{ m.name }} ({{ m.currency }})</option>
            }
          </select>
        </label>
        <label>
          Date of incident
          <input type="date" formControlName="incidentDate" [max]="today" />
          @if (invalid('incidentDate')) {
            <span class="field-error">Required, and cannot be in the future.</span>
          }
        </label>
      </div>

      <label>
        Where did it happen?
        <input type="text" formControlName="incidentLocation" placeholder="Street, suburb or landmark" />
        @if (invalid('incidentLocation')) {
          <span class="field-error">Please tell us where it happened.</span>
        }
      </label>

      <label>
        Describe what happened
        <textarea rows="5" formControlName="description" placeholder="What happened, who was involved, what was damaged"></textarea>
        @if (invalid('description')) {
          <span class="field-error">Please give us at least a short description (10+ characters).</span>
        }
      </label>

      <label>
        Estimated cost of damage <span class="muted">(optional, {{ currency() }})</span>
        <input type="number" min="0" step="1" formControlName="claimedAmount" />
      </label>

      <div class="actions">
        <button class="btn primary" type="submit" [disabled]="busy()">{{ busy() ? 'Submitting…' : 'Submit claim' }}</button>
      </div>
    </form>
  `,
})
export class NewClaimPage {
  private readonly api = inject(ClaimsApi);
  private readonly router = inject(Router);
  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly markets = toSignal(this.api.markets(), { initialValue: [] as Market[] });

  protected readonly form = inject(FormBuilder).nonNullable.group({
    type: ['Motor' as ClaimType, Validators.required],
    marketCode: ['SG', Validators.required],
    incidentDate: ['', [Validators.required, (c: { value: string }) => (c.value > this.today ? { future: true } : null)]],
    incidentLocation: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(4000)]],
    claimedAmount: [null as number | null, Validators.min(0)],
  });

  private readonly marketCode = toSignal(this.form.controls.marketCode.valueChanges, { initialValue: 'SG' });
  protected readonly currency = computed(() => this.markets().find((m) => m.code === this.marketCode())?.currency ?? '');

  protected invalid(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return c.invalid && (c.touched || c.dirty);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.submit(this.form.getRawValue()).subscribe({
      next: (claim) => this.router.navigate(['/claimant/claims', claim.id], { state: { justSubmitted: true } }),
      error: (e) => {
        this.error.set(describeError(e));
        this.busy.set(false);
      },
    });
  }
}
