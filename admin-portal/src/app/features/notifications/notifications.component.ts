import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection, facets, filterState } from '../../shared/components/filter-layout/filter-state';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AlertsService, PortalAlert } from '../../core/services/alerts.service';
import { AuthService } from '../../core/services/auth.service';
import { CellDirective, DataColumn, DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { DEFAULT_PAGE_SIZE, PaginationComponent, pageCount } from '../../shared/components/pagination/pagination.component';

const SCREENS: Record<PortalAlert['kind'], string> = { Coupon: '/coupons', Offer: '/campaigns', Ad: '/ads' };

/** "ends in 2 days", "ends in 5 hours", "ended 1 day ago". */
export function whenText(endsAt: string, now = new Date()): string {
  const ms = new Date(endsAt).getTime() - now.getTime();
  const hours = Math.max(1, Math.round(Math.abs(ms) / 3_600_000));
  const amount = hours >= 24 ? Math.round(hours / 24) : hours;
  const unit = (hours >= 24 ? 'day' : 'hour') + (amount === 1 ? '' : 's');
  return ms > 0 ? `Ends in ${amount} ${unit}` : `Ended ${amount} ${unit} ago`;
}

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule, RouterLink, DataTableComponent, CellDirective, PaginationComponent, FilterLayoutComponent],
  template: `
    <section class="page">
      <h2>Notifications</h2>
      <p class="muted">Ending in the next 3 days, or ended in the last 3.</p>

      <div class="filters">
        <button type="button" class="act" [disabled]="!service.unread()" (click)="markAll()">Mark all as read</button>
        <button type="button" class="act" [disabled]="service.loading()" (click)="service.refresh()">Refresh</button>
      </div>

      <app-filter-layout [fill]="true" [groups]="groups()" [selected]="filters.selected()" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <p *ngIf="service.error()" class="error" role="alert">{{ service.error() }}</p>

        <app-data-table [fill]="true" [columns]="columns" [rows]="pageRows()" [loading]="service.loading()" [startIndex]="(shownPage() - 1) * pageSize()" minWidth="720px" emptyText="Nothing is ending soon.">
          <ng-template appCell="what" let-a><span class="kind">{{ a.kind }}</span> <strong>{{ a.name }}</strong><em class="new" *ngIf="!service.isRead(a.id)">NEW</em></ng-template>
          <ng-template appCell="when" let-a><span [class.over]="a.ended">{{ when(a) }}</span><small>{{ a.endsAt | date:'medium' }}</small></ng-template>
          <ng-template appCell="shops" let-a>{{ a.stores.length ? a.stores.join(', ') : 'All stores' }}</ng-template>
          <ng-template appCell="action" let-a>
            <a class="act" *ngIf="canOpen(a)" [routerLink]="screen(a)" (click)="service.markRead([a.id])">Open {{ a.kind.toLowerCase() }}s</a>
            <button type="button" class="act" *ngIf="!service.isRead(a.id)" (click)="service.markRead([a.id])">Mark read</button>
          </ng-template>
        </app-data-table>

        <app-pagination [page]="shownPage()" [pageSize]="pageSize()" [total]="shown().length" (pageChange)="page.set($event)" (pageSizeChange)="pageSize.set($event)"></app-pagination>
      </app-filter-layout>
    </section>
  `,
  styles: [`
    :host{display:flex;flex-direction:column;color:var(--ink)}
    h2{font-family:var(--display);font-size:28px;letter-spacing:-1px;margin:0 0 4px}
    .muted{color:var(--muted);font-size:12px;margin:0 0 16px}
    .page{width:100%;flex:1;min-height:0;display:flex;flex-direction:column}
    .filters{display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end;margin-bottom:12px}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:var(--muted)}
    select{border:1.5px solid #D9D7DF;border-radius:12px;padding:8px 10px;font:inherit;font-size:12px;background:#fff;color:var(--ink)}
    .act{display:inline-block;border:1px solid var(--line);border-radius:999px;background:#fff;color:var(--ink);padding:7px 14px;font:800 11px inherit;text-decoration:none;cursor:pointer;margin-right:6px}
    .act:disabled{opacity:.5;cursor:default}
    .kind{background:#EEEAFF;border-radius:999px;padding:2px 8px;font-size:10px;font-weight:800}
    .new{margin-left:8px;background:var(--yellow);border-radius:6px;padding:1px 6px;font-size:9px;font-weight:800;font-style:normal}
    .over{color:var(--danger);font-weight:800}
    small{display:block;font-size:11px;color:var(--muted)}
    .error{color:var(--danger);font-weight:700;font-size:12px}
  `],
})
export class NotificationsComponent implements OnInit {
  readonly service = inject(AlertsService);
  private auth = inject(AuthService);

  readonly columns: DataColumn[] = [{ key: 'what', label: 'What' }, { key: 'when', label: 'When' }, { key: 'shops', label: 'Stores' }, { key: 'action', label: 'Action' }];
  readonly filters = filterState('notifications');
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  private readonly found = computed(() => facets<PortalAlert>(this.service.alerts(), this.filters.selected(), { kind: a => a.kind }));
  readonly shown = computed(() => this.found().shown);
  readonly groups = computed<FilterGroup[]>(() => {
    const kinds = [['Coupon', 'Coupons'], ['Offer', 'Offers'], ['Ad', 'Ads']].filter(([value]) => value !== 'Coupon' || this.isAdmin());
    return [{ key: 'kind', label: 'Show', options: kinds.map(([value, label]) => ({ value, label, count: this.found().count('kind', value) })) }];
  });
  onFilters(next: Selection) { this.filters.set(next); this.page.set(1); }
  clearFilters() { this.filters.clear(); this.page.set(1); }
  readonly shownPage = computed(() => Math.min(this.page(), pageCount(this.shown().length, this.pageSize())));
  readonly pageRows = computed(() => this.shown().slice((this.shownPage() - 1) * this.pageSize(), this.shownPage() * this.pageSize()));

  ngOnInit() { this.service.refresh(); }

  isAdmin() { return this.auth.isAdmin(); }
  /** Store managers manage offers; coupons and ads are the administrator's screens. */
  canOpen(a: PortalAlert) { return this.isAdmin() || a.kind === 'Offer'; }
  screen(a: PortalAlert) { return SCREENS[a.kind]; }
  when(a: PortalAlert) { return whenText(a.endsAt); }
  markAll() { this.service.markRead(this.service.alerts().map(a => a.id)); }
}
