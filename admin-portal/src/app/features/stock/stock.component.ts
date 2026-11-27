import { LoaderComponent } from '../../shared/components/loader/loader.component';
import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection, cached, filterState } from '../../shared/components/filter-layout/filter-state';
import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { storeChoices } from '../../shared/utils/stores';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role, Store } from '../../core/models/domain.model';

interface Row { productId: string; productName: string; variantId: string; variantLabel: string; carried: boolean; available: number; reorderThreshold: number; priceOverride?: number | null; basePrice?: number; mrp?: number | null; }

/** A store's own stock. Store staff always see their own store; an administrator picks one. */
@Component({
  selector: 'app-stock',
  standalone: true,
  imports: [CommonModule, FormsModule, FilterLayoutComponent, LoaderComponent],
  template: `
    <section class="page">
      <h2>Stock</h2>
      <p class="muted">Tick "Sells here" for what your store carries and set how many you have. Customers only see what your store carries.</p>
      <app-filter-layout [groups]="panel().groups" [selected]="panel().selected" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <input filterSearch type="search" name="q" aria-label="Search product" placeholder="Search product" [value]="query" (input)="query = $any($event.target).value" />
        <p class="error" *ngIf="error()">{{ error() }}</p>
        <p class="notice" *ngIf="notice()">{{ notice() }}</p>
        <app-loader *ngIf="loading()" message="Loading stock…"></app-loader>
        <div class="table" *ngIf="shown().length">
          <div class="row head"><span>Product</span><span>Size</span><span>Sells here</span><span>In stock</span><span>Reorder at</span><span>Store price (₹)</span><span></span></div>
          <div class="row" *ngFor="let r of shown()">
            <strong>{{ r.productName }}</strong>
            <span>{{ r.variantLabel }}</span>
            <input type="checkbox" [name]="'c' + r.variantId" [(ngModel)]="r.carried" />
            <input type="number" min="0" max="100000" [name]="'a' + r.variantId" [(ngModel)]="r.available" [disabled]="!r.carried" />
            <input type="number" min="0" max="100000" [name]="'t' + r.variantId" [(ngModel)]="r.reorderThreshold" [disabled]="!r.carried" />
            <span class="price"><input type="number" min="0.01" max="100000" step="0.01" [name]="'p' + r.variantId" [(ngModel)]="r.priceOverride" [disabled]="!r.carried" [placeholder]="'Shared ' + (r.basePrice ?? '')" /><small *ngIf="r.priceOverride && r.basePrice && r.priceOverride < r.basePrice">{{ discountPercent(r) }}% below the shared price</small></span>
            <button class="act" type="button" [disabled]="busyId() === r.variantId" (click)="save(r)">Save</button>
          </div>
        </div>
        <p *ngIf="!loading() && !error() && rows().length > 0 && shown().length === 0">Nothing matches.</p>
      </app-filter-layout>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Plus Jakarta Sans',sans-serif;color:var(--ink)}
    h2{font-family:'Bricolage Grotesque',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:var(--ink)}
    .muted{color:var(--muted);font-size:12px;margin:0 0 16px}
    .page{max-width:1100px}
    .bar{display:flex;gap:12px;margin-bottom:16px;flex-wrap:wrap;align-items:end}
    label{display:grid;gap:4px;font-size:12px;font-weight:700;color:var(--muted)}
    .check{display:flex;gap:8px;align-items:center}
    .check input{width:auto}
    input,select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118;min-width:0}
    .search{border-radius:999px;padding:8px 14px;min-width:220px}
    .error{color:#FFC6E2;font-weight:700}
    .notice{color:#02F34C;font-weight:700}
    .table{background:var(--card-bg,#F5F5F7);border-radius:24px;overflow:auto}
    .price{display:grid;gap:2px}
    .price small{font-size:10px;color:#04782F;font-weight:700}
    .row{display:grid;grid-template-columns:2fr 1fr .8fr 1fr 1fr 1.4fr .8fr;gap:12px;padding:10px 18px;border-top:1px solid rgba(20,17,24,.12);min-width:900px;font-size:12px;color:var(--on-card-muted,#5E5A66);align-items:center}
    .row strong{color:var(--on-card,#141118)}
    .row input[type=checkbox]{width:18px;height:18px}
    .head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .act{border:0;border-radius:999px;padding:7px 14px;font:800 11px 'Plus Jakarta Sans',sans-serif;cursor:pointer;background:#EEEAFF;color:#141118}
    .act:disabled{opacity:.5;cursor:default}
  `],
})
export class StockComponent implements OnInit {
  private api = inject(ApiService);
  private auth = inject(AuthService);

  readonly rows = signal<Row[]>([]);
  readonly stores = signal<Store[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly busyId = signal<string | null>(null);

  storeId = '';
  query = '';
  onlyCarried = false;

  /** A store list to pick from: always for administrators, and for a manager who has more than one store. */
  readonly filters = filterState('stock');
  readonly panel = cached<{ groups: FilterGroup[]; selected: Selection }>(
    () => [this.stores(), this.storeId, this.onlyCarried, this.canChooseStore()],
    () => {
      const sells: FilterGroup = { key: 'sells', label: 'Show', options: [{ value: 'carried', label: 'Only what we sell' }] };
      return {
        groups: this.canChooseStore() ? [{ key: 'store', label: 'Store', single: true, required: true, options: this.stores().map(s => ({ value: s.id, label: s.name })) }, sells] : [sells],
        selected: { store: this.storeId ? [this.storeId] : [], sells: this.onlyCarried ? ['carried'] : [] },
      };
    },
  );
  onFilters(next: Selection) {
    const store = next['store']?.[0] ?? '';
    this.onlyCarried = !!next['sells']?.length;
    this.filters.set(next);
    if (store !== this.storeId) { this.storeId = store; this.load(); }
  }
  clearFilters() {
    this.query = '';
    this.onlyCarried = false;
    this.filters.set({ store: this.storeId ? [this.storeId] : [] });
  }
  canChooseStore(): boolean { return this.isAdmin() || this.stores().length > 1; }

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }

  shown(): Row[] {
    const q = this.query.trim().toLowerCase();
    return this.rows().filter(r => (!q || r.productName.toLowerCase().includes(q)) && (!this.onlyCarried || r.carried));
  }

  ngOnInit() {
    this.onlyCarried = !!this.filters.of('sells').length;
    this.api.get<Store[]>('/stores').subscribe({
      next: r => { this.stores.set(storeChoices(r.data)); const kept = this.filters.of('store')[0]; if (kept && this.stores().some(s => s.id === kept)) this.storeId = kept; if (!this.storeId) this.storeId = this.stores()[0]?.id ?? ''; this.load(); },
      error: () => this.load(),
    });
  }

  load() {
    if (this.isAdmin() && !this.storeId) { this.rows.set([]); return; } // a manager without a list still gets their own store
    this.loading.set(true); this.error.set(null);
    this.api.get<Row[]>('/stock', { params: { storeId: this.storeId || null } }).subscribe({
      next: r => { this.rows.set(r.data ?? []); this.loading.set(false); },
      error: e => { this.error.set(e?.message ?? 'Could not load stock.'); this.loading.set(false); },
    });
  }

  /** How far the store's price is below the shared price (or the MRP when that is higher), as the customer will see it. */
  discountPercent(r: Row): number {
    const original = Math.max(r.basePrice ?? 0, r.mrp ?? 0);
    return original > 0 && r.priceOverride ? Math.round((original - r.priceOverride) / original * 100) : 0;
  }

  save(r: Row) {
    if (this.busyId()) return;
    this.busyId.set(r.variantId); this.error.set(null); this.notice.set(null);
    const url = '/stock/' + r.variantId + (this.storeId ? '?storeId=' + this.storeId : '');
    this.api.put<Row>(url, { carried: r.carried, available: Number(r.available) || 0, reorderThreshold: Number(r.reorderThreshold) || 0, priceOverride: r.carried && r.priceOverride ? Number(r.priceOverride) : null }).subscribe({
      next: res => { this.busyId.set(null); this.notice.set(r.productName + ' ' + r.variantLabel + ': saved.'); if (res.data) this.rows.set(this.rows().map(x => x.variantId === r.variantId ? res.data! : x)); },
      error: e => { this.busyId.set(null); this.error.set(e?.message ?? 'Could not save.'); },
    });
  }
}
