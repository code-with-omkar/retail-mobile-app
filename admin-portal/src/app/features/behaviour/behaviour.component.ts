import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role, Store } from '../../core/models/domain.model';

interface FunnelStep { name: string; sessions: number; percentOfFirst: number; percentOfPrevious: number; }
interface StepTotal { name: string; events: number; sessions: number; }
interface DetailCount { detail: string; count: number; }
interface DayCount { day: string; sessions: number; }
interface Report { from: string; to: string; storeId: string | null; funnel: FunnelStep[]; steps: StepTotal[]; topSearches: DetailCount[]; couponRefusals: DetailCount[]; dailySessions: DayCount[]; }

const LABELS: Record<string, string> = {
  app_open: 'Opened the app', search: 'Searched', product_view: 'Viewed a product', add_to_cart: 'Added to cart', cart_open: 'Opened the cart',
  checkout_start: 'Started checkout', coupon_applied: 'Coupon applied', coupon_refused: 'Coupon refused', slot_chosen: 'Chose a delivery time',
  order_placed: 'Placed an order', store_chosen: 'Chose a store', out_of_service_area: 'Address outside the service area',
};

const REASONS: Record<string, string> = {
  notfound: 'Code not valid', inactive: 'Code switched off', notstarted: 'Not started yet', expired: 'Expired', belowminimum: 'Order below the minimum',
  totallimitreached: 'Code used up', customerlimitreached: 'Already used by the customer', firstorderonly: 'First order only', nothingtodiscount: 'Would change nothing', emptycart: 'Cart was empty',
};

const iso = (d: Date) => d.toISOString().slice(0, 10);

/**
 * How customers move through the app: the buying funnel (opened the app, viewed a product, added to cart, started checkout, ordered), what they
 * search for, why coupons are refused, and sessions per day. Counts are of anonymous sessions: nothing identifies a person. Store staff see
 * their own store; an administrator can pick one or see all.
 */
@Component({
  selector: 'app-behaviour',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <div class="head">
        <div>
          <h2>Customer behaviour</h2>
          <p class="muted">What customers do in the app, counted by anonymous sessions (one app start is one session). Nothing here identifies a person.</p>
        </div>
        <div class="controls">
          <label>Period
            <select name="preset" [ngModel]="preset" (ngModelChange)="choose($event)">
              <option *ngFor="let p of presets" [value]="p.key">{{ p.label }}</option>
            </select>
          </label>
          <label *ngIf="isAdmin()">Store
            <select name="store" [ngModel]="storeId" (ngModelChange)="pickStore($event)">
              <option value="">All stores</option>
              <option *ngFor="let s of stores()" [value]="s.id">{{ s.name }}</option>
            </select>
          </label>
        </div>
      </div>

      <p *ngIf="error()" class="error">{{ error() }}</p>
      <p *ngIf="loading() && !report()">Loading…</p>

      <ng-container *ngIf="report() as r">
        <section class="card">
          <h3>Buying funnel</h3>
          <p class="muted-card" *ngIf="r.funnel[0].sessions === 0">No sessions were recorded in this period yet.</p>
          <div class="bar-row" *ngFor="let s of r.funnel">
            <span class="bar-label">{{ label(s.name) }}</span>
            <div class="bar"><div class="fill" [style.width.%]="s.percentOfFirst" [attr.data-step]="s.name"></div></div>
            <span class="bar-num"><strong>{{ s.sessions }}</strong> <small>{{ s.percentOfFirst }}% of openers<ng-container *ngIf="s.name !== 'app_open'"> · {{ s.percentOfPrevious }}% of the step before</ng-container></small></span>
          </div>
        </section>

        <div class="two">
          <section class="card">
            <h3>Top searches</h3>
            <p class="muted-card" *ngIf="r.topSearches.length === 0">No searches yet.</p>
            <div class="row" *ngFor="let t of r.topSearches"><span>{{ t.detail }}</span><strong>{{ t.count }}</strong></div>
          </section>
          <section class="card">
            <h3>Why coupons were refused</h3>
            <p class="muted-card" *ngIf="r.couponRefusals.length === 0">No refusals.</p>
            <div class="row" *ngFor="let t of r.couponRefusals"><span>{{ reason(t.detail) }}</span><strong>{{ t.count }}</strong></div>
          </section>
        </div>

        <section class="card">
          <h3>Sessions per day</h3>
          <div class="days">
            <div class="day" *ngFor="let d of r.dailySessions" [title]="d.day + ': ' + d.sessions + ' sessions'">
              <div class="col" [style.height.px]="height(d.sessions, r)"></div>
              <small>{{ d.day.slice(8) }}</small>
            </div>
          </div>
        </section>

        <section class="card">
          <h3>Every step</h3>
          <div class="row head-row"><span>Step</span><span>Times</span><span>Sessions</span></div>
          <div class="row" *ngFor="let s of r.steps"><span>{{ label(s.name) }}</span><span>{{ s.events }}</span><span>{{ s.sessions }}</span></div>
        </section>
      </ng-container>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 12px;max-width:560px}
    .page{max-width:1100px;display:grid;gap:16px}
    .head{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;flex-wrap:wrap}
    .controls{display:flex;gap:12px;flex-wrap:wrap}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:#E9E4FF}
    select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118;min-width:170px}
    .card{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:20px;display:grid;gap:10px;color:var(--on-card,#141118)}
    h3{margin:0;font:800 16px 'Space Grotesk',sans-serif}
    .two{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:16px}
    .muted-card{margin:0;color:var(--on-card-muted,#5E5A66);font-size:12px}
    .bar-row{display:grid;grid-template-columns:170px 1fr 260px;gap:12px;align-items:center;font-size:12px}
    .bar{background:rgba(20,17,24,.1);border-radius:999px;height:16px;overflow:hidden}
    .fill{height:100%;background:#FB1A8E;border-radius:999px;min-width:2px}
    .bar-num small{color:var(--on-card-muted,#5E5A66);font-size:10px}
    .row{display:flex;justify-content:space-between;gap:12px;padding:6px 0;border-top:1px solid rgba(20,17,24,.1);font-size:12px}
    .head-row{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .head-row span:nth-child(n+2),.row>span:nth-child(n+2){min-width:70px;text-align:right}
    .days{display:flex;gap:4px;align-items:flex-end;height:120px;overflow-x:auto}
    .day{display:grid;justify-items:center;gap:2px}
    .col{width:14px;background:#B9A8FF;border-radius:4px 4px 0 0;min-height:2px}
    .day small{font-size:9px;color:var(--on-card-muted,#5E5A66)}
    .error{color:#FFB3D9;font-weight:700}
    @media(max-width:700px){.bar-row{grid-template-columns:1fr}}
  `],
})
export class BehaviourComponent implements OnInit {
  private api = inject(ApiService);
  private auth = inject(AuthService);

  readonly report = signal<Report | null>(null);
  readonly stores = signal<Store[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly presets = [{ key: '7', label: 'Last 7 days' }, { key: '30', label: 'Last 30 days' }, { key: '90', label: 'Last 90 days' }];
  preset = '7';
  storeId = '';

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }
  label(name: string): string { return LABELS[name] ?? name; }
  reason(detail: string): string { return REASONS[detail] ?? detail; }

  /** Bar heights for the sessions-per-day chart: the busiest day is 100 pixels. */
  height(sessions: number, r: Report): number {
    const max = Math.max(...r.dailySessions.map(d => d.sessions), 0);
    return max <= 0 ? 2 : Math.max(2, Math.round((sessions / max) * 100));
  }

  ngOnInit() {
    if (this.isAdmin()) this.api.get<Store[]>('/stores').subscribe({ next: r => this.stores.set((r.data ?? []).filter(s => s.isActive)), error: () => {} });
    this.load();
  }

  choose(key: string) { this.preset = key; this.load(); }
  pickStore(id: string) { this.storeId = id; this.load(); }

  /** India days: today and the days before it, both ends included (the longest period the server allows is 92 days). */
  range(): { from: string; to: string } {
    const india = new Date(Date.now() + 5.5 * 3600_000);
    const to = new Date(Date.UTC(india.getUTCFullYear(), india.getUTCMonth(), india.getUTCDate()));
    const from = new Date(to.getTime() - (Number(this.preset) - 1) * 86_400_000);
    return { from: iso(from), to: iso(to) };
  }

  load() {
    this.loading.set(true); this.error.set(null);
    this.api.get<Report>('/reports/behaviour', { params: { ...this.range(), storeId: this.isAdmin() && this.storeId ? this.storeId : null } }).subscribe({
      next: r => { this.report.set(r.data ?? null); this.loading.set(false); },
      error: e => { this.report.set(null); this.error.set(e?.message ?? 'Could not load customer behaviour.'); this.loading.set(false); },
    });
  }
}
