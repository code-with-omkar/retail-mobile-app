import { LoaderComponent } from '../../shared/components/loader/loader.component';
import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection, cached, filterState } from '../../shared/components/filter-layout/filter-state';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { storeChoices } from '../../shared/utils/stores';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role, Store } from '../../core/models/domain.model';
import { iconUrl, statusIcon } from '../../layout/shell/decorate';

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
  imports: [CommonModule, FormsModule, RouterLink, FilterLayoutComponent, LoaderComponent],
  template: `
    <section class="page">
      <div class="head">
        <div>
          <h2>Today<span *ngIf="summary() as s"> · {{ s.storeName }}</span></h2>
          <p class="muted" *ngIf="summary() as s">Updated {{ s.asOf | date:'shortTime' }}. It refreshes by itself every 30 seconds.</p>
        </div>
      </div>

      <app-filter-layout *ngIf="canChooseStore(); else body" [groups]="panel().groups" [selected]="panel().selected" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <ng-container *ngTemplateOutlet="body"></ng-container>
      </app-filter-layout>
      <ng-template #body>
      <p *ngIf="error()" class="error">{{ error() }}</p>
      <app-loader *ngIf="loading() && !summary()" message="Loading…"></app-loader>
      <p *ngIf="isAdmin() && !storeId && !loading()" class="muted">Choose a store to see its day.</p>

      <ng-container *ngIf="summary() as s">
        <div class="kpis">
          <article class="kpi" *ngFor="let t of tiles(s); trackBy: byKind" [attr.data-kind]="t.kind" [class.quiet]="!t.busy">
            <div class="kpi-top">
              <span class="icon" aria-hidden="true"><i [style.--ic]="icon(t.icon)"></i></span>
              <span class="state">{{ t.state }}</span>
            </div>
            <p>{{ t.title }}</p>
            <strong>{{ t.value }}</strong>
            <small>{{ t.note }}</small>
          </article>
        </div>

        <section class="card">
          <header>
            <span class="icon attention" aria-hidden="true"><i [style.--ic]="icon('shield')"></i></span>
            <h3>Needs attention</h3>
            <span class="state" [attr.data-state]="attention(s).tone">{{ attention(s).text }}</span>
          </header>
          <div class="empty" *ngIf="s.alerts.length === 0">
            <span class="icon done" aria-hidden="true"><i [style.--ic]="icon('circleCheck')"></i></span>
            <p class="muted-card">Nothing needs attention right now.</p>
          </div>
          <div class="alert" *ngFor="let a of s.alerts" [class.urgent]="a.severity === 'urgent'">
            <span class="icon" aria-hidden="true"><i [style.--ic]="icon(alertIcon(a.kind))"></i></span>
            <div>
              <strong>{{ a.title }}</strong>
              <small>{{ a.detail }}</small>
            </div>
            <span class="state" [attr.data-state]="a.severity === 'urgent' ? 'bad' : 'wait'">{{ a.severity === 'urgent' ? 'Urgent' : 'Check' }}</span>
            <a class="go" *ngIf="a.orderId" routerLink="/orders">Open orders</a>
            <a class="go" *ngIf="!a.orderId && (a.kind === 'OutOfStock' || a.kind === 'LowStock')" routerLink="/stock">Open stock</a>
          </div>
          <p class="muted-card limits"><i [style.--ic]="icon('clock')" aria-hidden="true"></i>New orders are flagged after 5 minutes (urgent at 10), ready orders after 20 (urgent at 40), delivery windows at 80% full.</p>
        </section>

        <section class="card">
          <header>
            <span class="icon coming" aria-hidden="true"><i [style.--ic]="icon('truck')"></i></span>
            <h3>Coming up</h3>
            <span class="state" [attr.data-state]="s.upcoming.length ? 'go' : 'off'">{{ s.upcoming.length ? s.upcoming.length + ' scheduled' : 'None scheduled' }}</span>
          </header>
          <div class="empty" *ngIf="s.upcoming.length === 0">
            <span class="icon" aria-hidden="true"><i [style.--ic]="icon('hourglass')"></i></span>
            <p class="muted-card">No scheduled deliveries are waiting.</p>
          </div>
          <div class="row" *ngFor="let u of s.upcoming">
            <span class="icon coming" aria-hidden="true"><i [style.--ic]="icon('clock')"></i></span>
            <strong>{{ u.label }}</strong>
            <span>{{ u.orderNumber }}</span>
            <span class="state" [attr.data-state]="statusTone(u.status)"><i [style.--ic]="icon(statusMark(u.status))" aria-hidden="true"></i>{{ u.status }}</span>
          </div>
        </section>
      </ng-container>
      </ng-template>
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
    .kpis{display:flex;flex-wrap:wrap;gap:12px}
    .kpis>.kpi{flex:1 1 150px}
    .kpi{--tint:#FFF3D6;--strong:#B45309;border-radius:24px;padding:14px 16px 16px;color:#141118;background:var(--tint);box-shadow:inset 0 5px 0 var(--strong);display:grid;align-content:start}
    .kpi[data-kind=packing]{--tint:#E3EEFF;--strong:#1D4ED8}
    .kpi[data-kind=ready]{--tint:#EFE8FF;--strong:#6D28D9}
    .kpi[data-kind=way]{--tint:#DBF5FA;--strong:#0E7490}
    .kpi[data-kind=delivered]{--tint:#DDF6E6;--strong:#15803D}
    .kpi[data-kind=sales]{--tint:#FFE7D3;--strong:#C2410C}
    .kpi-top{display:flex;justify-content:space-between;align-items:center;gap:6px;margin:4px 0 12px}
    .kpi .icon{background:var(--strong);color:#fff}
    .kpi .state{background:#fff;color:var(--strong)}
    .kpi.quiet .state{color:#5E5A66}
    .kpi p{margin:0 0 4px;font-size:11px;font-weight:800;color:var(--strong)}
    .kpi strong{display:block;font:800 30px 'Bricolage Grotesque',sans-serif;letter-spacing:-.8px}
    .kpi small{font-size:10px;color:rgba(20,17,24,.65)}
    i{display:inline-block;width:18px;height:18px;background:currentColor;-webkit-mask:var(--ic) center / contain no-repeat;mask:var(--ic) center / contain no-repeat;flex-shrink:0}
    .icon{width:38px;height:38px;border-radius:13px;display:grid;place-items:center;flex-shrink:0;background:#EEEBFA;color:#4C3A99}
    .icon.attention{background:#FFE7D3;color:#C2410C}
    .icon.coming{background:#DBF5FA;color:#0E7490}
    .icon.done{background:#DDF6E6;color:#15803D}
    .state{display:inline-flex;align-items:center;gap:5px;border-radius:999px;padding:4px 10px;font-size:10px;font-weight:800;white-space:nowrap;background:#ECEAF3;color:#5E5A66}
    .state i{width:12px;height:12px}
    .state[data-state=done]{background:#DDF6E6;color:#15803D}
    .state[data-state=go]{background:#E3EEFF;color:#1D4ED8}
    .state[data-state=wait]{background:#FFF3D6;color:#92400E}
    .state[data-state=bad]{background:#FFE0E6;color:#BE123C}
    .card{background:var(--card-bg,#F5F5F7);border-radius:24px;padding:20px;display:grid;gap:12px;color:var(--on-card,#141118)}
    header{display:flex;align-items:center;gap:12px;flex-wrap:wrap}
    h3{margin:0;font:800 16px 'Bricolage Grotesque',sans-serif}
    .muted-card{margin:0;color:var(--on-card-muted,#5E5A66);font-size:12px}
    .empty{display:grid;justify-items:center;gap:8px;padding:14px;text-align:center}
    .empty .icon{width:52px;height:52px;border-radius:50%}
    .empty .icon i{width:26px;height:26px}
    .limits{display:flex;align-items:center;gap:8px;font-size:11px;background:#F6F4FE;border-radius:14px;padding:10px 12px}
    .limits i{width:15px;height:15px}
    .alert{display:flex;gap:12px;align-items:center;padding:10px 12px;border-radius:16px;background:#FFF3D6;font-size:12px;flex-wrap:wrap}
    .alert.urgent{background:#FFE0E6}
    .alert .icon{background:#fff;color:#92400E}
    .alert.urgent .icon{color:#BE123C}
    .alert .state{background:#fff}
    .alert>div{flex:1;min-width:180px}
    .alert strong{display:block;font-size:13px}
    .alert small{display:block;color:rgba(20,17,24,.7);margin-top:2px}
    .go{font-weight:800;font-size:11px;color:#141118;text-decoration:underline;white-space:nowrap}
    .row{display:grid;grid-template-columns:auto 1.6fr 1fr auto;gap:12px;align-items:center;padding:8px 0;border-top:1px solid #D5CEF0;font-size:12px}
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
  readonly filters = filterState('today');
  readonly panel = cached<{ groups: FilterGroup[]; selected: Selection }>(
    () => [this.stores(), this.storeId],
    () => {
      return { groups: [{ key: 'store', label: 'Store', single: true, required: true, options: this.stores().map(s => ({ value: s.id, label: s.name })) }], selected: { store: this.storeId ? [this.storeId] : [] } };
    },
  );
  onFilters(next: Selection) {
    const store = next['store']?.[0] ?? '';
    this.filters.set(next);
    if (store && store !== this.storeId) this.choose(store);
  }
  clearFilters() {

  }
  canChooseStore(): boolean { return this.isAdmin() || this.stores().length > 1; }

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }

  readonly icon = iconUrl;
  /** The day's boxes: each with its icon and a word on whether anything is waiting there. */
  tiles(s: Summary) {
    const c = s.counts;
    const tile = (kind: string, icon: string, title: string, count: number, note: string, busy: string, quiet: string, value: string = String(count)) => ({ kind, icon, title, value, note, busy: count > 0, state: count > 0 ? busy : quiet });
    return [
      tile('new', 'clock', 'New', c.new, 'waiting to be accepted', 'Waiting', 'Clear'),
      tile('packing', 'box', 'Packing', c.packing, 'accepted or being packed', 'In progress', 'Clear'),
      tile('ready', 'bag', 'Ready', c.ready, 'waiting for pickup', 'Pickup due', 'Clear'),
      tile('way', 'truck', 'On the way', c.outForDelivery, 'out for delivery', 'Moving', 'None'),
      tile('delivered', 'circleCheck', 'Delivered today', c.delivered, `of ${c.placed} placed today`, 'Done', 'None yet'),
      tile('sales', 'rupee', 'Sales today', c.revenue, 'delivered orders', 'Earning', 'No sales', '₹' + Math.round(c.revenue).toLocaleString('en-IN')),
    ];
  }
  /** The boxes are rebuilt on every check, so each is kept by its kind. */
  readonly byKind = (_: number, t: { kind: string }) => t.kind;
  attention(s: Summary) {
    const urgent = s.alerts.filter(a => a.severity === 'urgent').length;
    if (urgent) return { text: urgent + ' urgent', tone: 'bad' };
    return s.alerts.length ? { text: s.alerts.length + ' to check', tone: 'wait' } : { text: 'All clear', tone: 'done' };
  }
  alertIcon(kind: string) { return /stock/i.test(kind) ? 'box' : /window|slot/i.test(kind) ? 'hourglass' : /ready/i.test(kind) ? 'bag' : 'clock'; }
  statusMark(status: string) { return statusIcon(status)?.[0] ?? 'clock'; }
  statusTone(status: string) { return statusIcon(status)?.[1] ?? 'off'; }

  ngOnInit() {
    this.api.get<Store[]>('/stores').subscribe({
      next: r => { this.stores.set(storeChoices(r.data)); const kept = this.filters.of('store')[0]; if (kept && this.stores().some(s => s.id === kept)) this.storeId = kept; if (!this.storeId) this.storeId = this.stores()[0]?.id ?? ''; if (this.storeId || !this.isAdmin()) this.load(); },
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
