import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role, Store } from '../../core/models/domain.model';

interface Row { productId: string; productName: string; variantId: string; variantLabel: string; carried: boolean; available: number; reorderThreshold: number; }

/** A store's own stock. Store staff always see their own store; an administrator picks one. */
@Component({
  selector: 'app-stock',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <h2>Stock</h2>
      <p class="muted">Tick "Sells here" for what your store carries and set how many you have. Customers only see what your store carries.</p>
      <div class="bar">
        <label *ngIf="isAdmin()">Store
          <select name="store" [(ngModel)]="storeId" (ngModelChange)="load()"><option value="">Choose a store</option><option *ngFor="let s of stores()" [value]="s.id">{{ s.name }}</option></select>
        </label>
        <input class="search" name="q" [(ngModel)]="query" placeholder="Search product" />
        <label class="check"><input type="checkbox" name="only" [(ngModel)]="onlyCarried" /> Only what we sell</label>
      </div>
      <p class="error" *ngIf="error()">{{ error() }}</p>
      <p class="notice" *ngIf="notice()">{{ notice() }}</p>
      <p *ngIf="loading()">Loading stock...</p>
      <div class="table" *ngIf="shown().length">
        <div class="row head"><span>Product</span><span>Size</span><span>Sells here</span><span>In stock</span><span>Reorder at</span><span></span></div>
        <div class="row" *ngFor="let r of shown()">
          <strong>{{ r.productName }}</strong>
          <span>{{ r.variantLabel }}</span>
          <input type="checkbox" [name]="'c' + r.variantId" [(ngModel)]="r.carried" />
          <input type="number" min="0" max="100000" [name]="'a' + r.variantId" [(ngModel)]="r.available" [disabled]="!r.carried" />
          <input type="number" min="0" max="100000" [name]="'t' + r.variantId" [(ngModel)]="r.reorderThreshold" [disabled]="!r.carried" />
          <button class="act" type="button" [disabled]="busyId() === r.variantId" (click)="save(r)">Save</button>
        </div>
      </div>
      <p *ngIf="!loading() && !error() && rows().length > 0 && shown().length === 0">Nothing matches.</p>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 16px}
    .page{max-width:1100px}
    .bar{display:flex;gap:12px;margin-bottom:16px;flex-wrap:wrap;align-items:end}
    label{display:grid;gap:4px;font-size:12px;font-weight:700;color:#E9E4FF}
    .check{display:flex;gap:8px;align-items:center}
    .check input{width:auto}
    input,select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118;min-width:0}
    .search{border-radius:999px;padding:8px 14px;min-width:220px}
    .error{color:#FFC6E2;font-weight:700}
    .notice{color:#02F34C;font-weight:700}
    .table{background:var(--card-bg,#EAE4FF);border-radius:24px;overflow:auto}
    .row{display:grid;grid-template-columns:2fr 1fr .8fr 1fr 1fr .8fr;gap:12px;padding:10px 18px;border-top:1px solid rgba(20,17,24,.12);min-width:720px;font-size:12px;color:var(--on-card-muted,#5E5A66);align-items:center}
    .row strong{color:var(--on-card,#141118)}
    .row input[type=checkbox]{width:18px;height:18px}
    .head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .act{border:0;border-radius:999px;padding:7px 14px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
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

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }

  shown(): Row[] {
    const q = this.query.trim().toLowerCase();
    return this.rows().filter(r => (!q || r.productName.toLowerCase().includes(q)) && (!this.onlyCarried || r.carried));
  }

  ngOnInit() {
    if (this.isAdmin()) this.api.get<Store[]>('/stores').subscribe({ next: r => this.stores.set((r.data ?? []).filter(s => s.isActive)), error: () => {} });
    else this.load();
  }

  load() {
    if (this.isAdmin() && !this.storeId) { this.rows.set([]); return; }
    this.loading.set(true); this.error.set(null);
    this.api.get<Row[]>('/stock', { params: { storeId: this.isAdmin() ? this.storeId : null } }).subscribe({
      next: r => { this.rows.set(r.data ?? []); this.loading.set(false); },
      error: e => { this.error.set(e?.message ?? 'Could not load stock.'); this.loading.set(false); },
    });
  }

  save(r: Row) {
    if (this.busyId()) return;
    this.busyId.set(r.variantId); this.error.set(null); this.notice.set(null);
    const url = '/stock/' + r.variantId + (this.isAdmin() ? '?storeId=' + this.storeId : '');
    this.api.put<Row>(url, { carried: r.carried, available: Number(r.available) || 0, reorderThreshold: Number(r.reorderThreshold) || 0 }).subscribe({
      next: res => { this.busyId.set(null); this.notice.set(r.productName + ' ' + r.variantLabel + ': saved.'); if (res.data) this.rows.set(this.rows().map(x => x.variantId === r.variantId ? res.data! : x)); },
      error: e => { this.busyId.set(null); this.error.set(e?.message ?? 'Could not save.'); },
    });
  }
}
