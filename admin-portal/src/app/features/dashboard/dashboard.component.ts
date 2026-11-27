import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection, filterState } from '../../shared/components/filter-layout/filter-state';
import { Component, OnDestroy, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription, timer } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AdminDashboard } from '../../core/models/domain.model';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { ErrorStateComponent } from '../../shared/components/error-state/error-state.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { CellDirective, DataColumn, DataTableComponent } from '../../shared/components/data-table/data-table.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, LoadingComponent, ErrorStateComponent, IconComponent, DataTableComponent, CellDirective, FilterLayoutComponent],
  template: `
    <div class="dashboard page">
      <div class="top">
        <div><h2>Overview</h2><p class="muted">Today's pulse across your network.</p></div>
        <span class="live"><i></i>Updates every 30 seconds</span>
      </div>
      <app-loading *ngIf="isLoading()" message="Loading dashboard data..."></app-loading>
      <app-error-state *ngIf="error() && !isLoading()" title="Unable to load dashboard" [message]="error() || ''" retryLabel="Retry" (onRetry)="loadData()"></app-error-state>
      <ng-container *ngIf="dashboard() as data">
        <h3>Today</h3>
        <section class="tiles today">
          <article class="tile hero"><span class="icon"><app-icon name="chart" [size]="22"></app-icon></span><div><p>Total sales</p><strong>{{ data.totalSales | currency:'INR':'symbol':'1.0-0' }}</strong></div></article>
            <article class="tile"><span class="icon yellow"><app-icon name="today"></app-icon></span><div><p>Today's orders</p><strong>{{ data.todaysOrders }}</strong></div></article>
            <article class="tile"><span class="icon green"><app-icon name="pulse"></app-icon></span><div><p>Active orders</p><strong>{{ data.activeOrders }}</strong></div></article>
            <article class="tile"><span class="icon pink"><app-icon name="clock"></app-icon></span><div><p>Pending orders</p><strong>{{ data.pendingOrders }}</strong></div></article>
        </section>

        <h3>Business</h3>
        <section class="tiles">
            <article class="tile"><span class="icon lavender"><app-icon name="orders"></app-icon></span><div><p>Total orders</p><strong>{{ data.totalOrders }}</strong></div></article>
            <article class="tile"><span class="icon green"><app-icon name="check"></app-icon></span><div><p>Completed orders</p><strong>{{ data.completedOrders }}</strong></div></article>
            <article class="tile"><span class="icon pink"><app-icon name="close"></app-icon></span><div><p>Cancelled orders</p><strong>{{ data.cancelledOrders }}</strong></div></article>
            <article class="tile"><span class="icon yellow"><app-icon name="users"></app-icon></span><div><p>Total customers</p><strong>{{ data.totalCustomers }}</strong></div></article>
            <article class="tile"><span class="icon lavender"><app-icon name="box"></app-icon></span><div><p>Total products</p><strong>{{ data.totalProducts }}</strong></div></article>
            <article class="tile"><span class="icon green"><app-icon name="store"></app-icon></span><div><p>Active stores</p><strong>{{ data.activeStores }}</strong></div></article>
        </section>

        <section class="live-orders">
          <div class="panel-heading"><h3>Currently active</h3><span class="count">{{ data.activeOrders }} active</span></div>
          <app-filter-layout [groups]="groups()" [selected]="filters.selected()" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
          <p *ngIf="data.activeOrdersList.length === 0" class="empty">No active orders</p>
          <app-data-table *ngIf="data.activeOrdersList.length > 0" [columns]="columns" [rows]="shownOrders()" maxHeight="44vh">
            <ng-template appCell="orderNumber" let-order><strong>{{ order.orderNumber }}</strong></ng-template>
            <ng-template appCell="status" let-order><span class="status">{{ order.status }}</span></ng-template>
            <ng-template appCell="orderTime" let-order>{{ order.orderTime | date:'short' }}</ng-template>
            <ng-template appCell="amount" let-order>{{ order.amount | currency:'INR':'symbol':'1.0-0' }}</ng-template>
            <ng-template appCell="deliveryPartner" let-order>{{ order.deliveryPartner || 'Unassigned' }}</ng-template>
          </app-data-table>
          </app-filter-layout>
        </section>
      </ng-container>
    </div>
  `,
  styles: [`
    :host{display:block;color:var(--ink)}
    .dashboard{max-width:1280px;display:grid;gap:12px;padding-bottom:20px}
    .head{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;flex-wrap:wrap}
    h2{font-family:var(--display);font-size:28px;letter-spacing:-1px;margin:0 0 4px}
    h3{font:800 14px 'Plus Jakarta Sans',sans-serif;margin:8px 0 0}
    .muted{color:var(--muted);font-size:12px;margin:0}
    .live{display:inline-flex;gap:8px;align-items:center;background:#fff;border:1px solid var(--line);border-radius:999px;padding:6px 13px;font-size:11px;font-weight:700;color:var(--muted)}
    .live i{width:8px;height:8px;border-radius:50%;background:#14A44D}
    .tiles{display:flex;flex-wrap:wrap;gap:12px}
    .tiles>.tile{flex:1 1 160px}
    .tiles.today>.tile.hero{flex:1.6 1 280px}
    .tile{background:#fff;border:1px solid var(--line);border-radius:18px;padding:16px;display:flex;gap:12px;align-items:center}
    .tile p{margin:0 0 4px;color:var(--muted);font-size:11px;font-weight:700}
    .tile strong{display:block;font-family:var(--display);font-size:24px;letter-spacing:-.8px}
    .icon{width:40px;height:40px;border-radius:13px;display:grid;place-items:center;flex-shrink:0;background:#fff}
    .yellow{background:#FFF1A8}.green{background:#D9F7E3}.lavender{background:#EEEAFF}.pink{background:#FFE3EF}
    .tile.hero{background:var(--yellow);border-color:var(--yellow)}
    .tile.hero p{color:var(--ink)}
    .tile.hero strong{font-size:30px}
    .panel-heading{display:flex;justify-content:space-between;align-items:center;margin-bottom:10px}
    .count{background:var(--soft);border-radius:999px;padding:4px 11px;font-size:11px;font-weight:800}
    .status{background:#E3F7EA;color:var(--success);font-weight:800;font-size:11px;padding:3px 10px;border-radius:999px}
    .empty{background:#fff;border:1px solid var(--line);border-radius:18px;padding:26px;text-align:center;color:var(--muted);font-size:12px;margin:0}
  
    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{flex:1;min-height:0;width:100%;display:flex!important;flex-direction:column;gap:12px}
    .top{display:flex;justify-content:space-between;align-items:flex-end;gap:12px;flex-wrap:wrap}
    .top h2{margin:0 0 4px}
    .top .muted{margin:0;font-size:12px;color:var(--muted)}
    .top-actions{display:flex;gap:10px;flex-wrap:wrap}
    app-data-table small{display:block;font-size:11px;color:var(--muted);margin-top:2px}
`],
})
export class DashboardComponent implements OnInit, OnDestroy {
  readonly filters = filterState('overview');
  readonly shownOrders = computed(() => (this.dashboard()?.activeOrdersList ?? []).filter(o => this.filters.allows('status', String(o.status))));
  readonly groups = computed<FilterGroup[]>(() => {
    const orders = this.dashboard()?.activeOrdersList ?? [];
    return [{ key: 'status', label: 'Status', options: [...new Set(orders.map(o => String(o.status)))].sort().map(v => ({ value: v, label: v, count: orders.filter(o => String(o.status) === v).length })) }];
  });
  onFilters(next: Selection) { this.filters.set(next); }
  clearFilters() { this.filters.clear(); }
  private readonly api = inject(ApiService);
  readonly columns: DataColumn[] = [
    { key: 'orderNumber', label: 'Order' }, { key: 'customer', label: 'Customer' }, { key: 'store', label: 'Store' }, { key: 'status', label: 'Status' },
    { key: 'orderTime', label: 'Time' }, { key: 'amount', label: 'Amount' }, { key: 'deliveryPartner', label: 'Delivery' },
  ];
  private readonly polling = new Subscription();
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);
  readonly dashboard = signal<AdminDashboard | null>(null);

  ngOnInit(): void { this.loadData(); this.polling.add(timer(30000, 30000).subscribe(() => this.loadData(true))); }
  ngOnDestroy(): void { this.polling.unsubscribe(); }
  loadData(background = false): void {
    if (!background) this.isLoading.set(true);
    this.error.set(null);
    this.api.get<AdminDashboard>('/admin/dashboard').subscribe({
      next: response => { if (response.success && response.data) this.dashboard.set(response.data); else this.error.set(response.message ?? 'Dashboard data was not returned.'); this.isLoading.set(false); },
      error: error => { this.error.set(error.message ?? 'Failed to load dashboard.'); this.isLoading.set(false); },
    });
  }
}
