import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { Role, Store } from '../../core/models/domain.model';

interface Totals { orders: number; delivered: number; inProgress: number; cancelled: number; rejected: number; revenue: number; averageOrder: number; cashRevenue: number; onlineRevenue: number; newCustomers: number; refunds: number; refundedAmount: number; }
interface Day { date: string; orders: number; delivered: number; revenue: number; }
interface Hour { hour: number; orders: number; }
interface Product { productId: string; name: string; quantity: number; revenue: number; }
interface StoreRow { storeId: string; store: string; orders: number; delivered: number; revenue: number; }
interface LowStock { storeId: string; store: string; product: string; size: string; available: number; reorderThreshold: number; }
interface Income { offerFeesPaid: number; offerFeesRefunded: number; placementPaid: number; placementRefunded: number; }
interface PartnerRating { partnerId: string; partner: string; count: number; average: number; }
interface RatingSummary { count: number; averageStore: number; averageDelivery: number | null; stars: number[]; partners: PartnerRating[]; }
interface RatingRow { orderId: string; orderNumber: string; store: string; storeStars: number; deliveryStars: number | null; comment: string | null; createdAt: string; }
interface Ratings { summary: RatingSummary; recent: RatingRow[]; }
interface Report { from: string; to: string; storeId: string | null; storeName: string | null; totals: Totals; days: Day[]; hours: Hour[]; topProducts: Product[]; stores: StoreRow[]; lowStock: LowStock[]; income: Income | null; earnings?: EarningsRow[]; }
interface EarningsRow { storeId: string; store: string; orders: number; sales: number; commission: number; fees: number; net: number; }

/** Today's date in India as yyyy-mm-dd, whatever the computer's own time zone is. */
function indiaToday(): Date {
  const text = new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Kolkata' });
  const [y, m, d] = text.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d));
}
const iso = (date: Date) => date.toISOString().slice(0, 10);
const addDays = (date: Date, days: number) => new Date(date.getTime() + days * 86_400_000);

/**
 * Sales and stock reports. Revenue counts orders that reached the customer; open orders are shown as in progress and cancelled or declined ones
 * as lost. Days are India days. Store staff see their own store; administrators see every store or pick one.
 */
@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <h2>Reports</h2>
      <p class="muted">Revenue is orders that reached the customer. Days run midnight to midnight, India time.</p>

      <div class="bar">
        <div class="presets">
          <button type="button" *ngFor="let p of presets" [class.on]="preset === p.key" (click)="choose(p.key)">{{ p.label }}</button>
        </div>
        <label *ngIf="preset === 'custom'">From<input type="date" name="from" [(ngModel)]="from" /></label>
        <label *ngIf="preset === 'custom'">To<input type="date" name="to" [(ngModel)]="to" /></label>
        <label *ngIf="isAdmin()">Store
          <select name="store" [(ngModel)]="storeId" (ngModelChange)="load()"><option value="">All stores</option><option *ngFor="let s of stores()" [value]="s.id">{{ s.name }}</option></select>
        </label>
        <button class="primary" type="button" [disabled]="loading()" (click)="load()">Show</button>
        <button class="act" type="button" [disabled]="loading() || !report()" (click)="download()">Download orders (CSV)</button>
      </div>
      <p class="error" *ngIf="error()">{{ error() }}</p>
      <p *ngIf="loading()">Loading report...</p>

      <ng-container *ngIf="report() as r">
        <p class="scope">{{ r.storeName || 'All stores' }} · {{ r.from | date:'mediumDate' }}<span *ngIf="r.from !== r.to"> to {{ r.to | date:'mediumDate' }}</span></p>
        <div class="kpis">
          <article class="kpi yellow"><p>Revenue</p><strong>{{ r.totals.revenue | currency:'INR':'symbol':'1.0-0' }}</strong><small>{{ r.totals.delivered }} delivered</small></article>
          <article class="kpi green"><p>Orders placed</p><strong>{{ r.totals.orders }}</strong><small>{{ r.totals.inProgress }} in progress</small></article>
          <article class="kpi lavender"><p>Average order</p><strong>{{ r.totals.averageOrder | currency:'INR':'symbol':'1.0-0' }}</strong><small>of delivered orders</small></article>
          <article class="kpi pink"><p>Lost</p><strong>{{ r.totals.cancelled + r.totals.rejected }}</strong><small>{{ r.totals.cancelled }} cancelled · {{ r.totals.rejected }} declined ({{ lostPercent(r) }}%)</small></article>
          <article class="kpi lavender"><p>Cash on delivery</p><strong>{{ r.totals.cashRevenue | currency:'INR':'symbol':'1.0-0' }}</strong><small>delivered</small></article>
          <article class="kpi green"><p>Paid online</p><strong>{{ r.totals.onlineRevenue | currency:'INR':'symbol':'1.0-0' }}</strong><small>delivered</small></article>
          <article class="kpi yellow"><p>New customers</p><strong>{{ r.totals.newCustomers }}</strong><small>first order in this period</small></article>
          <article class="kpi pink"><p>Refunds</p><strong>{{ r.totals.refunds }}</strong><small>{{ r.totals.refundedAmount | currency:'INR':'symbol':'1.0-0' }} returned</small></article>
        </div>

        <h3>Revenue by day</h3>
        <div class="chart" role="img" aria-label="Revenue by day">
          <div class="col" *ngFor="let d of r.days" [title]="(d.date | date:'mediumDate') + ': ' + d.orders + ' orders, ' + (d.revenue | currency:'INR':'symbol':'1.0-0')">
            <span class="bar-fill" [style.height.%]="height(d.revenue, maxRevenue(r))"></span>
            <small *ngIf="showLabel(r, d)">{{ d.date | date:'d MMM' }}</small>
          </div>
        </div>

        <h3>Busiest hours</h3>
        <div class="chart hours" role="img" aria-label="Orders by hour of the day">
          <div class="col" *ngFor="let h of r.hours" [title]="h.hour + ':00 · ' + h.orders + ' orders'">
            <span class="bar-fill alt" [style.height.%]="height(h.orders, maxHour(r))"></span>
            <small *ngIf="h.hour % 3 === 0">{{ h.hour }}</small>
          </div>
        </div>

        <div class="two">
          <section class="card">
            <h3>Top products</h3>
            <p *ngIf="r.topProducts.length === 0" class="muted-dark">No delivered orders in this period.</p>
            <div class="row head" *ngIf="r.topProducts.length"><span>Product</span><span>Sold</span><span>Revenue</span></div>
            <div class="row" *ngFor="let p of r.topProducts"><strong>{{ p.name }}</strong><span>{{ p.quantity }}</span><span>{{ p.revenue | currency:'INR':'symbol':'1.0-0' }}</span></div>
          </section>
          <section class="card" *ngIf="r.stores.length">
            <h3>By store</h3>
            <div class="row head"><span>Store</span><span>Orders</span><span>Revenue</span></div>
            <div class="row" *ngFor="let s of r.stores"><strong>{{ s.store }}</strong><span>{{ s.orders }} ({{ s.delivered }} delivered)</span><span>{{ s.revenue | currency:'INR':'symbol':'1.0-0' }}</span></div>
          </section>
          <section class="card" *ngIf="r.earnings?.length">
            <h3>Earnings by shop <small>(delivered orders; sales are after discounts, the store keeps delivery and handling fees)</small></h3>
            <div class="row head"><span>Shop</span><span>Sales</span><span>Our commission</span></div>
            <div class="row" *ngFor="let e of r.earnings"><strong>{{ e.store }}<small> {{ e.orders }} orders</small></strong><span>{{ e.sales | currency:'INR':'symbol':'1.0-0' }}</span><span>{{ e.commission | currency:'INR':'symbol':'1.2-2' }}<small>store keeps {{ e.net | currency:'INR':'symbol':'1.0-0' }}</small></span></div>
            <div class="row" *ngIf="r.earnings!.length > 1"><strong>All shops</strong><span>{{ sumOf(r.earnings!, 'sales') | currency:'INR':'symbol':'1.0-0' }}</span><span>{{ sumOf(r.earnings!, 'commission') | currency:'INR':'symbol':'1.2-2' }}</span></div>
          </section>
          <section class="card" *ngIf="r.income as i">
            <h3>Platform income</h3>
            <div class="row"><strong>Offer fees paid</strong><span></span><span>{{ i.offerFeesPaid | currency:'INR':'symbol':'1.0-0' }}</span></div>
            <div class="row"><strong>Offer fees returned</strong><span></span><span>{{ i.offerFeesRefunded | currency:'INR':'symbol':'1.0-0' }}</span></div>
            <div class="row"><strong>Placement bids kept</strong><span></span><span>{{ i.placementPaid | currency:'INR':'symbol':'1.0-0' }}</span></div>
            <div class="row"><strong>Placement bids returned</strong><span></span><span>{{ i.placementRefunded | currency:'INR':'symbol':'1.0-0' }}</span></div>
          </section>
        </div>

        <section class="card wide" *ngIf="ratings() as q">
          <h3>Customer ratings</h3>
          <p *ngIf="q.summary.count === 0" class="muted-dark">No ratings in this period yet.</p>
          <ng-container *ngIf="q.summary.count > 0">
            <div class="rate-head">
              <div><strong class="big">{{ q.summary.averageStore | number:'1.1-1' }}</strong><span class="stars">{{ stars(q.summary.averageStore) }}</span><small>{{ q.summary.count }} ratings</small></div>
              <div *ngIf="q.summary.averageDelivery !== null"><strong class="big">{{ q.summary.averageDelivery | number:'1.1-1' }}</strong><span class="stars">{{ stars(q.summary.averageDelivery) }}</span><small>delivery</small></div>
              <div class="spread">
                <div class="srow" *ngFor="let n of [5,4,3,2,1]"><span>{{ n }}★</span><span class="track"><span class="fill" [style.width.%]="share(q.summary, n)"></span></span><span>{{ q.summary.stars[n - 1] }}</span></div>
              </div>
            </div>
            <div class="row head" *ngIf="q.summary.partners.length"><span>Delivery partner</span><span>Ratings</span><span>Average</span></div>
            <div class="row" *ngFor="let p of q.summary.partners"><strong>{{ p.partner }}</strong><span>{{ p.count }}</span><span>{{ p.average | number:'1.1-1' }} {{ stars(p.average) }}</span></div>
            <h3 class="sub">Latest comments</h3>
            <div class="comment" *ngFor="let c of q.recent"><span class="stars">{{ stars(c.storeStars) }}</span> <strong>{{ c.orderNumber }}</strong> <small>{{ c.store }} · {{ c.createdAt | date:'medium' }}<span *ngIf="c.deliveryStars"> · delivery {{ c.deliveryStars }}★</span></small><p *ngIf="c.comment">{{ c.comment }}</p></div>
          </ng-container>
        </section>

        <section class="card wide">
          <h3>Running low</h3>
          <p *ngIf="r.lowStock.length === 0" class="muted-dark">Nothing is at or below its reorder level.</p>
          <div class="row low head" *ngIf="r.lowStock.length"><span>Product</span><span>Size</span><span>Store</span><span>In stock</span><span>Reorder at</span></div>
          <div class="row low" *ngFor="let l of r.lowStock"><strong>{{ l.product }}</strong><span>{{ l.size }}</span><span>{{ l.store }}</span><span [class.zero]="l.available === 0">{{ l.available }}</span><span>{{ l.reorderThreshold }}</span></div>
        </section>
      </ng-container>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    h3{margin:20px 0 8px;font-size:15px;color:#fff}
    .card h3{color:var(--on-card,#141118);margin-top:0}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 16px}
    .muted-dark{color:var(--on-card-muted,#5E5A66);font-size:12px}
    .page{max-width:1200px}
    .bar{display:flex;gap:12px;flex-wrap:wrap;align-items:end;margin-bottom:12px}
    .presets{display:flex;gap:6px;flex-wrap:wrap}
    .presets button{border:0;border-radius:999px;padding:8px 14px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:rgba(255,255,255,.18);color:#fff}
    .presets button.on{background:#FFD400;color:#141118}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:#E9E4FF}
    input,select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:7px 10px;font:inherit;background:#fff;color:#141118}
    .primary{border:0;border-radius:999px;background:#FFD400;color:#141118;padding:9px 18px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .act{border:0;border-radius:999px;padding:9px 16px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
    .primary:disabled,.act:disabled{opacity:.5;cursor:default}
    .error{color:#FFC6E2;font-weight:700}
    .scope{color:#E9E4FF;font-weight:700;font-size:13px}
    .kpis{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:10px}
    .kpi{border-radius:24px;padding:16px;color:#141118;display:grid;gap:2px}
    .kpi p{margin:0;font-size:11px;font-weight:700;text-transform:uppercase}
    .kpi strong{font-family:'Space Grotesk',sans-serif;font-size:26px}
    .kpi small{font-size:11px;opacity:.75}
    .yellow{background:#FFF1A8}.green{background:#B9FFCF}.lavender{background:#EAE4FF}.pink{background:#FFC6E2}
    .chart{display:flex;gap:3px;align-items:flex-end;height:150px;background:var(--card-bg,#EAE4FF);border-radius:24px;padding:14px 14px 22px;overflow-x:auto}
    .col{flex:1;min-width:8px;height:100%;display:flex;flex-direction:column;justify-content:flex-end;align-items:center;position:relative}
    .col small{position:absolute;bottom:-18px;font-size:9px;color:var(--on-card-muted,#5E5A66);white-space:nowrap}
    .bar-fill{display:block;width:100%;min-height:2px;border-radius:6px 6px 2px 2px;background:#141118}
    .bar-fill.alt{background:#FB1A8E}
    .two{display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:12px;margin-top:20px}
    .card{background:var(--card-bg,#EAE4FF);color:var(--on-card,#141118);border-radius:24px;padding:18px}
    .wide{margin-top:12px}
    .row{display:grid;grid-template-columns:2fr 1.4fr 1fr;gap:10px;padding:8px 0;border-top:1px solid rgba(20,17,24,.12);font-size:12px;color:var(--on-card-muted,#5E5A66)}
    .row.low{grid-template-columns:2fr 1fr 1.4fr .8fr .8fr}
    .row strong{color:var(--on-card,#141118)}
    .row.head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .zero{color:#B00060;font-weight:800}
    .rate-head{display:flex;gap:28px;flex-wrap:wrap;align-items:center;margin-bottom:12px}
    .rate-head .big{font-family:'Space Grotesk',sans-serif;font-size:32px;margin-right:8px}
    .stars{color:#E8A400;letter-spacing:1px}
    .rate-head small{display:block;color:var(--on-card-muted,#5E5A66);font-size:11px}
    .spread{display:grid;gap:3px;min-width:220px;flex:1;max-width:340px}
    .srow{display:grid;grid-template-columns:28px 1fr 28px;gap:8px;align-items:center;font-size:11px}
    .track{height:8px;border-radius:99px;background:rgba(20,17,24,.12);overflow:hidden}
    .fill{display:block;height:100%;background:#141118}
    .sub{margin-top:14px}
    .comment{border-top:1px solid rgba(20,17,24,.12);padding:8px 0;font-size:12px;color:var(--on-card-muted,#5E5A66)}
    .comment p{margin:4px 0 0;color:var(--on-card,#141118);white-space:pre-wrap;word-break:break-word}
  `],
})
export class ReportsComponent implements OnInit {
  private api = inject(ApiService);
  private http = inject(HttpClient);
  private auth = inject(AuthService);

  sumOf(rows: EarningsRow[], key: 'sales' | 'commission'): number { return rows.reduce((total, row) => total + row[key], 0); }
  private config = inject(ConfigService);

  readonly report = signal<Report | null>(null);
  readonly ratings = signal<Ratings | null>(null);
  readonly stores = signal<Store[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly presets = [
    { key: 'today', label: 'Today' },
    { key: '7', label: 'Last 7 days' },
    { key: '30', label: 'Last 30 days' },
    { key: 'custom', label: 'Choose dates' },
  ];
  preset = '7';
  from = iso(addDays(indiaToday(), -6));
  to = iso(indiaToday());
  storeId = '';

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }

  ngOnInit() {
    if (this.isAdmin()) this.api.get<Store[]>('/stores').subscribe({ next: r => this.stores.set((r.data ?? []).filter(s => s.isActive)), error: () => {} });
    this.load();
  }

  choose(key: string) {
    this.preset = key;
    const today = indiaToday();
    if (key === 'today') { this.from = iso(today); this.to = iso(today); }
    else if (key === '7') { this.from = iso(addDays(today, -6)); this.to = iso(today); }
    else if (key === '30') { this.from = iso(addDays(today, -29)); this.to = iso(today); }
    if (key !== 'custom') this.load();
  }

  private params(): HttpParams {
    let params = new HttpParams().set('from', this.from).set('to', this.to);
    if (this.isAdmin() && this.storeId) params = params.set('storeId', this.storeId);
    return params;
  }

  load() {
    if (!this.from || !this.to) { this.error.set('Choose both dates.'); return; }
    this.loading.set(true); this.error.set(null);
    this.api.get<Report>('/reports', { params: { from: this.from, to: this.to, storeId: this.isAdmin() && this.storeId ? this.storeId : null } }).subscribe({
      next: r => { this.report.set(r.data ?? null); this.loading.set(false); },
      error: e => { this.report.set(null); this.ratings.set(null); this.error.set(e?.message ?? 'Could not load the report.'); this.loading.set(false); },
    });
    this.api.get<Ratings>('/reports/ratings', { params: { from: this.from, to: this.to, storeId: this.isAdmin() && this.storeId ? this.storeId : null } }).subscribe({
      next: r => this.ratings.set(r.data ?? null),
      error: () => this.ratings.set(null),
    });
  }

  /** The browser cannot attach the sign-in token to a plain link, so the file is fetched here and saved from memory. */
  download() {
    this.http.get(this.config.getUrl('/reports/orders.csv'), { params: this.params(), responseType: 'blob' }).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `orders-${this.from}-to-${this.to}.csv`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.error.set('Could not download the orders.'),
    });
  }

  /** Five characters of stars for a whole or fractional average: filled up to the nearest whole star. */
  stars(value: number): string { const n = Math.max(0, Math.min(5, Math.round(value))); return '★'.repeat(n) + '☆'.repeat(5 - n); }
  share(summary: RatingSummary, star: number): number { return summary.count === 0 ? 0 : Math.round((summary.stars[star - 1] * 100) / summary.count); }

  lostPercent(r: Report): number { return r.totals.orders === 0 ? 0 : Math.round(((r.totals.cancelled + r.totals.rejected) * 100) / r.totals.orders); }
  maxRevenue(r: Report): number { return Math.max(...r.days.map(d => d.revenue), 0); }
  maxHour(r: Report): number { return Math.max(...r.hours.map(h => h.orders), 0); }
  height(value: number, max: number): number { return max <= 0 ? 0 : Math.max(2, Math.round((value / max) * 100)); }

  /** Few labels on a long range, so they do not run into each other. */
  showLabel(r: Report, d: Day): boolean {
    const step = r.days.length <= 10 ? 1 : r.days.length <= 31 ? 3 : 7;
    return r.days.indexOf(d) % step === 0;
  }
}
