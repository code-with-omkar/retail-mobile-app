import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { storeChoices } from '../../shared/utils/stores';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role, Store } from '../../core/models/domain.model';

interface Counts { placed: number; new: number; packing: number; ready: number; outForDelivery: number; delivered: number; cancelled: number; revenue: number; }
interface Alert { kind: string; severity: 'warning' | 'urgent'; title: string; detail: string; orderId?: string | null; }
interface Upcoming { orderId: string; orderNumber: string; startUtc: string; endUtc: string; label: string; status: string; }
interface Summary { storeId: string; storeName: string; asOf: string; counts: Counts; alerts: Alert[]; upcoming: Upcoming[]; }

/**
 * One store's day at a glance: what is waiting, what needs attention (a new order not answered, an order ready but not collected, stock running
 * out, delivery windows filling up) and the scheduled orders coming up. Refreshes by itself every 30 seconds while it is open.
 * The limits (5 and 10 minutes for new orders, 20 and 40 for ready ones, 80% for a window) are fixed on the server.
 */
@Component({
  selector: 'app-today',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <section class="page">
      <div class="head">
        <div>
          <h2>Today<span *ngIf="summary() as s"> · {{ s.storeName }}</span></h2>
          <p class="muted" *ngIf="summary() as s">Updated {{ s.asOf | date:'shortTime' }}. It refreshes by itself every 30 seconds.</p>
        </div>
        <label class="pick" *ngIf="canChooseStore()">Store
          <select name="store" [ngModel]="storeId" (ngModelChange)="choose($event)">
            <option value="" disabled>Choose a store</option>
            <option *ngFor="let s of stores()" [value]="s.id">{{ s.name }}</option>
          </select>
        </label>
      </div>

      <p *ngIf="error()" class="error">{{ error() }}</p>
      <p *ngIf="loading() && !summary()">Loading…</p>
      <p *ngIf="isAdmin() && !storeId && !loading()" class="muted">Choose a store to see its day.</p>

      <ng-container *ngIf="summary() as s">
        <div class="kpis">
          <article class="kpi yellow"><p>New</p><strong>{{ s.counts.new }}</strong><small>waiting to be accepted</small></article>
          <article class="kpi green"><p>Packing</p><strong>{{ s.counts.packing }}</strong><small>accepted or being packed</small></article>
          <article class="kpi lavender"><p>Ready</p><strong>{{ s.counts.ready }}</strong><small>waiting for pickup</small></article>
          <article class="kpi pink"><p>On the way</p><strong>{{ s.counts.outForDelivery }}</strong><small>out for delivery</small></article>
          <article class="kpi yellow"><p>Delivered today</p><strong>{{ s.counts.delivered }}</strong><small>of {{ s.counts.placed }} placed today</small></article>
          <article class="kpi green"><p>Sales today</p><strong>{{ s.counts.revenue | currency:'INR':'symbol':'1.0-0' }}</strong><small>delivered orders</small></article>
        </div>

        <section class="card">
          <h3>Needs attention</h3>
          <p class="muted-card" *ngIf="s.alerts.length === 0">Nothing needs attention right now.</p>
          <div class="alert" *ngFor="let a of s.alerts" [class.urgent]="a.severity === 'urgent'">
            <span class="dot" aria-hidden="true"></span>
            <div>
              <strong>{{ a.title }}</strong>
              <small>{{ a.detail }}</small>
            </div>
            <a class="go" *ngIf="a.orderId" routerLink="/orders">Open orders</a>
            <a class="go" *ngIf="!a.orderId && (a.kind === 'OutOfStock' || a.kind === 'LowStock')" routerLink="/stock">Open stock</a>
          </div>
          <p class="muted-card limits">New orders are flagged after 5 minutes (urgent at 10), ready orders after 20 (urgent at 40), delivery windows at 80% full.</p>
        </section>

        <section class="card">
          <h3>Coming up</h3>
          <p class="muted-card" *ngIf="s.upcoming.length === 0">No scheduled deliveries are waiting.</p>
          <div class="row" *ngFor="let u of s.upcoming">
            <strong>{{ u.label }}</strong>
            <span>{{ u.orderNumber }}</span>
            <span class="badge">{{ u.status }}</span>
          </div>
        </section>
      </ng-container>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Plus Jakarta Sans',sans-serif;color:var(--ink)}
    h2{font-family:'Bricolage Grotesque',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:var(--ink)}
    .muted{color:var(--muted);font-size:12px;margin:0 0 12px}
    .page{max-width:1100px;display:grid;gap:16px}
    .head{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;flex-wrap:wrap}
    .pick{display:grid;gap:4px;font-size:11px;font-weight:700;color:var(--muted)}
    select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118;min-width:200px}
    .kpis{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:12px}
    .kpi{border-radius:24px;padding:16px 18px;color:#141118}
    .kpi.yellow{background:#FFF1A8}.kpi.green{background:#B9FFCF}.kpi.lavender{background:#F5F5F7}.kpi.pink{background:#FFC6E2}
    .kpi p{margin:0 0 4px;font-size:11px;font-weight:700;color:rgba(20,17,24,.7)}
    .kpi strong{display:block;font:800 26px 'Bricolage Grotesque',sans-serif;letter-spacing:-.8px}
    .kpi small{font-size:10px;color:rgba(20,17,24,.6)}
    .card{background:var(--card-bg,#F5F5F7);border-radius:24px;padding:20px;display:grid;gap:10px;color:var(--on-card,#141118)}
    h3{margin:0;font:800 16px 'Bricolage Grotesque',sans-serif}
    .muted-card{margin:0;color:var(--on-card-muted,#5E5A66);font-size:12px}
    .limits{font-size:11px}
    .alert{display:flex;gap:12px;align-items:center;padding:10px 12px;border-radius:16px;background:#FFF1A8;font-size:12px}
    .alert.urgent{background:#FFC6E2}
    .alert>div{flex:1}
    .alert strong{display:block;font-size:13px}
    .alert small{display:block;color:rgba(20,17,24,.7);margin-top:2px}
    .dot{width:10px;height:10px;border-radius:50%;background:#FFD400;flex-shrink:0}
    .alert.urgent .dot{background:#C42F1D}
    .go{font-weight:800;font-size:11px;color:#141118;text-decoration:underline;white-space:nowrap}
    .row{display:grid;grid-template-columns:1.6fr 1fr auto;gap:12px;align-items:center;padding:8px 0;border-top:1px solid rgba(20,17,24,.1);font-size:12px}
    .badge{border-radius:999px;padding:3px 10px;font-size:10px;font-weight:800;background:#ECECF0}
    .error{color:#C42F1D;font-weight:700}
  `],
})
export class TodayComponent implements OnInit, OnDestroy {
  private api = inject(ApiService);
  private auth = inject(AuthService);
  private timer: ReturnType<typeof setInterval> | null = null;

  readonly summary = signal<Summary | null>(null);
  readonly stores = signal<Store[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  storeId = '';

  /** A store list to pick from: always for administrators, and for a manager who has more than one store. */
  canChooseStore(): boolean { return this.isAdmin() || this.stores().length > 1; }

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }

  ngOnInit() {
    this.api.get<Store[]>('/stores').subscribe({
      next: r => { this.stores.set(storeChoices(r.data)); if (!this.storeId) this.storeId = this.stores()[0]?.id ?? ''; if (this.storeId || !this.isAdmin()) this.load(); },
      error: e => { this.error.set(e?.message ?? 'Could not load the stores.'); if (!this.isAdmin()) this.load(); },
    });
    // Only while this screen is open: the timer stops with it.
    this.timer = setInterval(() => { if (this.isAdmin() ? !!this.storeId : true) this.load(true); }, 30000);
  }

  ngOnDestroy() { if (this.timer) clearInterval(this.timer); }

  choose(storeId: string) { this.storeId = storeId; this.summary.set(null); this.load(); }

  load(quiet = false) {
    if (!quiet) this.loading.set(true);
    this.error.set(null);
    this.api.get<Summary>('/store-today', { params: { storeId: this.storeId || null } }).subscribe({
      next: r => { this.summary.set(r.data ?? null); this.loading.set(false); },
      error: e => { this.error.set(e?.message ?? 'Could not load today.'); this.loading.set(false); },
    });
  }
}
