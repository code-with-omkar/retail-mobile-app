import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { storeChoices } from '../../shared/utils/stores';
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
          <label *ngIf="canChooseStore()">Store
            <select name="store" [ngModel]="storeId" (ngModelChange)="pickStore($event)">
              <option value="">All stores</option>
              <option *ngFor="let s of stores()" [value]="s.id">{{ s.name }}</option>
            </select>
          </label>
        </div>
      </div>

      <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
      <p *ngIf="loading() && !report()">Loading…</p>

      <ng-container *ngIf="report() as r">
        <section class="tiles">
          <article class="tile hero"><p>Bought something</p><strong>{{ conversion(r) }}%</strong><small>of the people who opened the app</small></article>
          <article class="tile"><p>Sessions</p><strong>{{ r.funnel[0].sessions }}</strong><small>app opens in the period</small></article>
          <article class="tile"><p>Orders placed</p><strong>{{ last(r)?.sessions ?? 0 }}</strong><small>sessions that ordered</small></article>
          <article class="tile"><p>Biggest drop</p><strong class="word">{{ worstStep(r) }}</strong><small>where most people stop</small></article>
        </section>

        <section class="card">
          <h3>Buying funnel</h3>
          <p class="muted-card" *ngIf="r.funnel[0].sessions === 0">No sessions were recorded in this period yet.</p>
          <div class="bar-row" *ngFor="let s of r.funnel; let i = index">
            <span class="step-no">{{ i + 1 }}</span>
            <span class="bar-label">{{ label(s.name) }}</span>
            <div class="bar"><div class="fill" [style.width.%]="s.percentOfFirst" [attr.data-step]="s.name"></div></div>
            <span class="bar-num"><strong>{{ s.sessions }}</strong> <small>{{ s.percentOfFirst }}% of openers<ng-container *ngIf="s.name !== 'app_open'"> · {{ s.percentOfPrevious }}% of the step before</ng-container></small></span>
          </div>
        </section>

        <section class="card">
          <h3>Sessions per day</h3>
          <p class="muted-card" *ngIf="!r.dailySessions.length">No days to show.</p>
          <div class="days">
            <div class="day" *ngFor="let d of r.dailySessions" [title]="d.day + ': ' + d.sessions + ' sessions'">
              <span class="value">{{ d.sessions }}</span>
              <div class="col" [style.height.px]="height(d.sessions, r)"></div>
              <small>{{ d.day.slice(8) }}</small>
            </div>
          </div>
        </section>

        <div class="two">
          <section class="card">
            <h3>Top searches</h3>
            <p class="muted-card" *ngIf="r.topSearches.length === 0">No searches yet.</p>
            <div class="rank" *ngFor="let t of r.topSearches; let i = index"><span class="no">{{ i + 1 }}</span><div><span>{{ t.detail }}</span><i [style.width.%]="share(t.count, r.topSearches)"></i></div><strong>{{ t.count }}</strong></div>
          </section>
          <section class="card">
            <h3>Why coupons were refused</h3>
            <p class="muted-card" *ngIf="r.couponRefusals.length === 0">No refusals.</p>
            <div class="rank pink" *ngFor="let t of r.couponRefusals; let i = index"><span class="no">{{ i + 1 }}</span><div><span>{{ reason(t.detail) }}</span><i [style.width.%]="share(t.count, r.couponRefusals)"></i></div><strong>{{ t.count }}</strong></div>
          </section>
        </div>

        <section class="card">
          <h3>Every step</h3>
          <div class="steps">
            <div class="row head-row"><span>#</span><span>Step</span><span>Times</span><span>Sessions</span></div>
            <div class="row" *ngFor="let s of r.steps; let i = index"><span>{{ i + 1 }}</span><span>{{ label(s.name) }}</span><span>{{ s.events }}</span><span>{{ s.sessions }}</span></div>
          </div>
        </section>
      </ng-container>
    </section>
  `,
  styles: [`
    :host{display:block;color:var(--ink)}
    h2{font-family:var(--display);font-size:28px;letter-spacing:-1px;margin:0 0 4px}
    .muted{color:var(--muted);font-size:12px;margin:0;max-width:600px}
    .page{max-width:1280px;display:grid;gap:14px;padding-bottom:20px}
    .head{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;flex-wrap:wrap}
    .controls{display:flex;gap:10px;flex-wrap:wrap}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:var(--muted)}
    select{border:1.5px solid #D9D7DF;border-radius:12px;padding:9px 12px;font:inherit;font-size:12px;background:#fff;color:var(--ink);min-width:170px}
    .tiles{display:grid;grid-template-columns:1.4fr 1fr 1fr 1.4fr;gap:12px}
    .tile{background:#fff;border:1px solid var(--line);border-radius:18px;padding:16px;display:grid;gap:4px}
    .tile.hero{background:var(--yellow);border-color:var(--yellow)}
    .tile p{margin:0;font-size:11px;font-weight:700}
    .tile strong{font-family:var(--display);font-size:30px;letter-spacing:-1px}
    .tile strong.word{font-size:17px;letter-spacing:-.3px;line-height:1.35}
    .tile small{color:var(--muted);font-size:11px}
    .tile.hero small{color:var(--ink)}
    .card{background:#fff;border:1px solid var(--line);border-radius:20px;padding:18px;display:grid;gap:10px;align-content:start}
    h3{margin:0;font:800 14px 'Plus Jakarta Sans',sans-serif}
    .two{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:14px}
    .muted-card{margin:0;color:var(--muted);font-size:12px}
    .bar-row{display:grid;grid-template-columns:26px 170px 1fr 280px;gap:12px;align-items:center;font-size:12px}
    .step-no{width:24px;height:24px;border-radius:50%;background:var(--soft);display:grid;place-items:center;font-weight:800;font-size:11px}
    .bar-label{font-weight:700}
    .bar{background:var(--card);border-radius:999px;height:22px;overflow:hidden}
    .fill{height:100%;background:linear-gradient(90deg,#8B7CF6,#F59ACB,#FFD400);border-radius:999px;min-width:3px;transition:width .5s ease}
    .bar-num small{color:var(--muted);font-size:11px}
    .days{display:flex;gap:6px;align-items:flex-end;height:160px;overflow-x:auto;padding-bottom:4px}
    .day{display:grid;justify-items:center;gap:3px;min-width:24px;flex:1}
    .value{font-size:10px;font-weight:700;color:var(--muted)}
    .col{width:100%;max-width:34px;background:linear-gradient(180deg,#8B7CF6,#C9C0F0);border-radius:8px 8px 2px 2px;min-height:2px}
    .day small{font-size:10px;color:var(--muted)}
    .rank{display:grid;grid-template-columns:24px 1fr 44px;gap:10px;align-items:center;font-size:12px}
    .rank .no{color:var(--muted);font-weight:700;text-align:right}
    .rank div{display:grid;gap:4px}
    .rank i{display:block;height:6px;border-radius:999px;background:#8B7CF6}
    .rank.pink i{background:#F59ACB}
    .rank strong{text-align:right}
    .steps{max-height:300px;overflow:auto;border:1px solid var(--line);border-radius:14px}
    .steps .row{display:grid;grid-template-columns:44px 1fr 90px 90px;gap:10px;padding:9px 12px;border-top:1px solid var(--line);font-size:12px}
    .steps .row span:nth-child(n+3){text-align:right}
    .steps .head-row{position:sticky;top:0;background:var(--card);border-top:0;font-size:10px;font-weight:800;text-transform:uppercase}
    .error{color:var(--danger);font-weight:700}
    @media(max-width:1000px){.tiles{grid-template-columns:1fr 1fr}.bar-row{grid-template-columns:26px 1fr}.bar-row .bar,.bar-row .bar-num{grid-column:2}}
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

  /** A store list to pick from: always for administrators, and for a manager who has more than one store. */
  canChooseStore(): boolean { return this.isAdmin() || this.stores().length > 1; }

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }
  label(name: string): string { return LABELS[name] ?? name; }
  reason(detail: string): string { return REASONS[detail] ?? detail; }

  /** Bar heights for the sessions-per-day chart: the busiest day is 100 pixels. */
  height(sessions: number, r: Report): number {
    const max = Math.max(...r.dailySessions.map(d => d.sessions), 0);
    return max <= 0 ? 2 : Math.max(2, Math.round((sessions / max) * 100));
  }

  last(r: Report) { return r.funnel[r.funnel.length - 1]; }
  /** The share of openers who reached the last step. */
  conversion(r: Report): number { return this.last(r)?.percentOfFirst ?? 0; }
  /** The step that keeps the smallest part of the step before it. */
  worstStep(r: Report): string {
    const later = r.funnel.slice(1).filter(() => r.funnel[0].sessions > 0);
    return later.length ? this.label(later.reduce((a, b) => (b.percentOfPrevious < a.percentOfPrevious ? b : a)).name) : '—';
  }
  share(count: number, all: { count: number }[]): number { return Math.round((count / Math.max(1, ...all.map(x => x.count))) * 100); }

  ngOnInit() {
    this.api.get<Store[]>('/stores').subscribe({
      next: r => { this.stores.set(storeChoices(r.data)); if (!this.storeId) this.storeId = this.stores()[0]?.id ?? ''; this.load(); },
      error: () => this.load(),
    });
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
    this.api.get<Report>('/reports/behaviour', { params: { ...this.range(), storeId: this.storeId ? this.storeId : null } }).subscribe({
      next: r => { this.report.set(r.data ?? null); this.loading.set(false); },
      error: e => { this.report.set(null); this.error.set(e?.message ?? 'Could not load customer behaviour.'); this.loading.set(false); },
    });
  }
}
