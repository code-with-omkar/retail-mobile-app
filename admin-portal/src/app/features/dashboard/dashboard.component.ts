import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription, timer } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AdminDashboard } from '../../core/models/domain.model';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { ErrorStateComponent } from '../../shared/components/error-state/error-state.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, LoadingComponent, ErrorStateComponent],
  template: `
    <div class="dashboard">
      <div class="hero-row"><div><span class="eyebrow accent">LIVE SNAPSHOT</span><h2>Everything in motion.</h2><p class="muted">A clear view of today's retail pulse across your network.</p></div><span class="refresh-label">Updates every 30 seconds</span></div>
      <app-loading *ngIf="isLoading()" message="Loading dashboard data..."></app-loading>
      <app-error-state *ngIf="error() && !isLoading()" title="Unable to load dashboard" [message]="error() || ''" retryLabel="Retry" (onRetry)="loadData()"></app-error-state>
      <ng-container *ngIf="dashboard() as data">
        <section class="stats-grid">
          <article class="stat-card"><span class="stat-icon mint">↗</span><div><p>Total orders</p><strong>{{ data.totalOrders }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon gold">▦</span><div><p>Total products</p><strong>{{ data.totalProducts }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon blue">◎</span><div><p>Total customers</p><strong>{{ data.totalCustomers }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon rose">⌂</span><div><p>Active stores</p><strong>{{ data.activeStores }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon mint">◷</span><div><p>Today's orders</p><strong>{{ data.todaysOrders }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon gold">●</span><div><p>Active orders</p><strong>{{ data.activeOrders }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon blue">✓</span><div><p>Completed orders</p><strong>{{ data.completedOrders }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon blue">◷</span><div><p>Pending orders</p><strong>{{ data.pendingOrders }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon rose">×</span><div><p>Cancelled orders</p><strong>{{ data.cancelledOrders }}</strong></div></article>
          <article class="stat-card"><span class="stat-icon rose">₹</span><div><p>Total sales</p><strong>{{ data.totalSales | currency:'INR':'symbol':'1.0-0' }}</strong></div></article>
        </section>
        <section class="panel live-orders"><div class="panel-heading"><div><span class="eyebrow">LIVE ORDERS</span><h3>Currently active</h3></div><strong>{{ data.activeOrders }} active</strong></div>
          <div *ngIf="data.activeOrdersList.length === 0" class="empty-state">No active orders</div>
          <div *ngIf="data.activeOrdersList.length > 0" class="orders-table"><div class="order-row order-header"><span>Order</span><span>Customer</span><span>Store</span><span>Status</span><span>Time</span><span>Amount</span><span>Delivery</span></div><div *ngFor="let order of data.activeOrdersList" class="order-row"><strong>{{ order.orderNumber }}</strong><span>{{ order.customer }}</span><span>{{ order.store }}</span><span class="status">{{ order.status }}</span><span>{{ order.orderTime | date:'short' }}</span><span>{{ order.amount | currency:'INR':'symbol':'1.0-0' }}</span><span>{{ order.deliveryPartner || 'Unassigned' }}</span></div></div>
        </section>
      </ng-container>
    </div>
  `,
  styles: [`
    .dashboard{margin-bottom:40px}.hero-row{display:flex;justify-content:space-between;align-items:flex-end;margin:0 0 26px;gap:16px}.eyebrow{color:#899a94;font-size:10px;font-weight:700;letter-spacing:1.5px;margin:0 0 7px;display:block}.eyebrow.accent{color:#6c9d1e}h2{font-size:30px;letter-spacing:-1.3px;margin:0 0 4px;font-family:Manrope,sans-serif}h3{font-size:17px;letter-spacing:-.5px;margin:0;font-family:Manrope,sans-serif}.muted,.refresh-label{color:#82908b;font-size:12px;margin:0}.refresh-label{white-space:nowrap}.stats-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-bottom:26px}.stat-card{background:#fff;border:1px solid #e5ebe6;border-radius:10px;padding:18px;display:flex;gap:12px;min-height:78px}.stat-card p{color:#82908b;font-size:11px;margin:2px 0 8px}.stat-card strong{display:block;font-size:20px;font-family:Manrope,sans-serif}.stat-icon{display:grid;place-items:center;border-radius:7px;width:29px;height:29px;font-weight:700;font-size:15px;flex-shrink:0}.mint{background:#e4f4d8;color:#66a744}.gold{background:#fff1cf;color:#c09137}.blue{background:#deedf3;color:#4c9bb3}.rose{background:#fae4d9;color:#cf7c4e}.panel{background:#fff;border:1px solid #e5ebe6;border-radius:10px;padding:22px}.panel-heading{display:flex;justify-content:space-between;align-items:center;margin-bottom:16px}.panel-heading>strong{color:#6c9d1e;font-size:12px}.orders-table{overflow-x:auto}.order-row{display:grid;grid-template-columns:1.1fr 1.2fr 1.1fr 1fr 1.1fr .9fr 1fr;gap:12px;align-items:center;min-width:760px;padding:12px 0;border-top:1px solid #eff2ef;color:#586963;font-size:11px}.order-header{color:#899a94;font-size:10px;font-weight:700;text-transform:uppercase}.order-row strong{color:#172523}.status{color:#6c9d1e;font-weight:700}.empty-state{border-top:1px solid #eff2ef;padding:28px 0 8px;color:#82908b;font-size:12px;text-align:center}@media(max-width:1000px){.stats-grid{grid-template-columns:repeat(2,1fr)}}@media(max-width:600px){.hero-row{display:block}.refresh-label{display:block;margin-top:12px}.stats-grid{grid-template-columns:1fr}}
  `],
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
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
