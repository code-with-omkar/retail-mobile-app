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
          <article class="stat-card yellow"><span class="stat-icon">↗</span><div><p>Total orders</p><strong>{{ data.totalOrders }}</strong></div></article>
          <article class="stat-card green"><span class="stat-icon">▦</span><div><p>Total products</p><strong>{{ data.totalProducts }}</strong></div></article>
          <article class="stat-card lavender"><span class="stat-icon">◎</span><div><p>Total customers</p><strong>{{ data.totalCustomers }}</strong></div></article>
          <article class="stat-card pink"><span class="stat-icon">⌂</span><div><p>Active stores</p><strong>{{ data.activeStores }}</strong></div></article>
          <article class="stat-card yellow"><span class="stat-icon">◷</span><div><p>Today's orders</p><strong>{{ data.todaysOrders }}</strong></div></article>
          <article class="stat-card green"><span class="stat-icon">●</span><div><p>Active orders</p><strong>{{ data.activeOrders }}</strong></div></article>
          <article class="stat-card lavender"><span class="stat-icon">✓</span><div><p>Completed orders</p><strong>{{ data.completedOrders }}</strong></div></article>
          <article class="stat-card pink"><span class="stat-icon">◷</span><div><p>Pending orders</p><strong>{{ data.pendingOrders }}</strong></div></article>
          <article class="stat-card yellow"><span class="stat-icon">×</span><div><p>Cancelled orders</p><strong>{{ data.cancelledOrders }}</strong></div></article>
          <article class="stat-card green"><span class="stat-icon">₹</span><div><p>Total sales</p><strong>{{ data.totalSales | currency:'INR':'symbol':'1.0-0' }}</strong></div></article>
        </section>
        <section class="panel live-orders"><div class="panel-heading"><div><span class="eyebrow">LIVE ORDERS</span><h3>Currently active</h3></div><strong>{{ data.activeOrders }} active</strong></div>
          <div *ngIf="data.activeOrdersList.length === 0" class="empty-state">No active orders</div>
          <div *ngIf="data.activeOrdersList.length > 0" class="orders-table"><div class="order-row order-header"><span>Order</span><span>Customer</span><span>Store</span><span>Status</span><span>Time</span><span>Amount</span><span>Delivery</span></div><div *ngFor="let order of data.activeOrdersList" class="order-row"><strong>{{ order.orderNumber }}</strong><span>{{ order.customer }}</span><span>{{ order.store }}</span><span class="status">{{ order.status }}</span><span>{{ order.orderTime | date:'short' }}</span><span>{{ order.amount | currency:'INR':'symbol':'1.0-0' }}</span><span>{{ order.deliveryPartner || 'Unassigned' }}</span></div></div>
        </section>
      </ng-container>
    </div>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 20px}

    .dashboard{margin-bottom:40px}
    .hero-row{display:flex;justify-content:space-between;align-items:flex-end;margin:0 0 26px;gap:16px}
    .eyebrow{color:#E9E4FF;font-size:10px;font-weight:700;letter-spacing:1.5px;margin:0 0 7px;display:block}
    .eyebrow.accent{color:#FFD400}
    h2{font-size:30px;letter-spacing:-1.3px}
    .muted{margin:0}
    h3{font-size:17px;letter-spacing:-.5px;margin:0;font-family:'Space Grotesk',sans-serif;color:var(--on-card,#141118)}
    .refresh-label{color:#E9E4FF;font-size:12px;margin:0;white-space:nowrap}
    .stats-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-bottom:26px}
    .stat-card{border-radius:24px;padding:18px;display:flex;gap:12px;min-height:90px;color:var(--tile-ink,#141118)}
    .stat-card.yellow{background:var(--card-yellow,#FFF1A8)}
    .stat-card.green{background:var(--card-green,#B9FFCF)}
    .stat-card.lavender{background:var(--card-lavender,#EAE4FF)}
    .stat-card.pink{background:var(--card-pink,#FFC6E2)}
    .stat-card p{color:var(--tile-ink-muted,rgba(20,17,24,.7));font-size:11px;margin:2px 0 8px}
    .stat-card strong{display:block;font-size:22px;letter-spacing:-.8px;font-family:'Space Grotesk',sans-serif;color:var(--tile-ink,#141118)}
    .stat-icon{display:grid;place-items:center;border-radius:12px;width:32px;height:32px;font-weight:700;font-size:15px;flex-shrink:0;color:#141118}
    .yellow .stat-icon{background:#FFD400}
    .green .stat-icon{background:#02F34C}
    .lavender .stat-icon{background:#B9A8FF}
    .pink .stat-icon{background:#FB1A8E}
    .panel{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:22px;color:var(--on-card,#141118)}
    .panel-heading{display:flex;justify-content:space-between;align-items:center;margin-bottom:16px}
    .panel-heading>strong{color:var(--on-card,#141118);font-size:12px}
    .orders-table{overflow-x:auto}
    .order-row{display:grid;grid-template-columns:1.1fr 1.2fr 1.1fr 1fr 1.1fr .9fr 1fr;gap:12px;align-items:center;min-width:760px;padding:12px 0;border-top:1px solid var(--row-line,rgba(20,17,24,.12));color:var(--on-card-muted,#5E5A66);font-size:11px}
    .order-header{font-size:10px;font-weight:700;text-transform:uppercase}
    .order-row strong{color:var(--on-card,#141118)}
    .order-row .status{justify-self:start;background:#02F34C;color:#141118;font-weight:700;padding:3px 10px;border-radius:999px}
    .empty-state{border-top:1px solid var(--row-line,rgba(20,17,24,.12));padding:28px 0 8px;color:var(--on-card-muted,#5E5A66);font-size:12px;text-align:center}
    @media(max-width:1000px){.stats-grid{grid-template-columns:repeat(2,1fr)}}
    @media(max-width:600px){.hero-row{display:block}.refresh-label{display:block;margin-top:12px}.stats-grid{grid-template-columns:1fr}}
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
