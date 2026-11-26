import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FileUploadComponent } from '../../shared/components/file-upload/file-upload.component';
import { PhonePreviewComponent } from '../../shared/components/phone-preview/phone-preview.component';
import { CellDirective, DataColumn, DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { DEFAULT_PAGE_SIZE, PaginationComponent, pageCount } from '../../shared/components/pagination/pagination.component';
import { ApiService } from '../../core/services/api.service';
import { ConfigService } from '../../core/services/config.service';

export type AdTarget = 'None' | 'Product' | 'Category' | 'Offer' | 'Url';

export interface Ad {
  id: string; placement: string; title: string | null; imageUrl: string; targetType: AdTarget; target: string | null; storeId: string | null;
  startsAt: string; endsAt: string | null; isActive: boolean; sortOrder: number; views: number; taps: number; status: string;
}

interface Choice { id: string; name: string; }

/** Where an ad can be shown, with the key the API and the app use. */
export const AD_PLACEMENTS: { key: string; label: string; hint: string }[] = [
  { key: 'home', label: 'Home: top banner', hint: 'Under the search bar. Wide picture, about 1080 x 360 (3:1).' },
  { key: 'home_middle', label: 'Home: between rows', hint: 'Between Home sections. Wide picture, about 1080 x 360 (3:1).' },
  { key: 'categories', label: 'Categories', hint: 'Above the products of a category. About 1080 x 360 (3:1).' },
  { key: 'search', label: 'Search results', hint: 'Above search results. About 1080 x 360 (3:1).' },
  { key: 'cart', label: 'Cart', hint: 'In the cart, above the bill. About 1080 x 360 (3:1).' },
  { key: 'order', label: 'Order screens', hint: 'On the order placed and order screens. About 1080 x 360 (3:1).' },
  { key: 'product', label: 'Product page', hint: 'Under You might also like. About 1080 x 360 (3:1).' },
];

export const placementLabel = (key: string) => AD_PLACEMENTS.find(p => p.key === key)?.label ?? key;

/** `datetime-local` wants local time without a zone: "2026-11-01T10:30". */
const toLocal = (iso: string | null): string => {
  if (!iso) return '';
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
};

/**
 * Ads in the customer app. An administrator uploads a picture, chooses where it shows (Home, Categories, Search, Cart, Order, Product),
 * what a tap opens (a product, a category, an offer or a web page), the store (or all stores) and the dates. Views and taps are counted.
 */
@Component({
  selector: 'app-ads',
  standalone: true,
  imports: [CommonModule, FormsModule, FileUploadComponent, PhonePreviewComponent, DataTableComponent, CellDirective, PaginationComponent],
  template: `
    <section class="page">
      <h2>Ads</h2>
      <p class="muted">Pictures shown in the customer app at the places you choose. Customers see an ad only between its dates and while it is on. Views and taps are counted.</p>
      <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
      <p *ngIf="notice()" class="notice" role="status">{{ notice() }}</p>

      <div class="with-preview">
      <form class="card" (ngSubmit)="save()" #f="ngForm">
        <h3>{{ editing() ? 'Edit ad' : 'New ad' }}</h3>
        <div class="grid">
          <label>Where it shows
            <select name="placement" [(ngModel)]="placement">
              <option *ngFor="let p of placements" [value]="p.key">{{ p.label }}</option>
            </select>
          </label>
          <label>Title (optional)<input name="title" [(ngModel)]="title" maxlength="80" placeholder="Shown over the picture" /></label>
          <label>Store
            <select name="storeId" [(ngModel)]="storeId">
              <option value="">All stores</option>
              <option *ngFor="let s of stores()" [value]="s.id">{{ s.name }}</option>
            </select>
          </label>
          <label>Order (lower first)<input type="number" name="sortOrder" [(ngModel)]="sortOrder" min="0" max="1000" step="1" /></label>
          <label>Starts<input type="datetime-local" name="startsAt" [(ngModel)]="startsAt" /></label>
          <label>Ends (optional)<input type="datetime-local" name="endsAt" [(ngModel)]="endsAt" /></label>
        </div>
        <small class="hint">{{ hint() }}</small>

        <div class="picture">
          <app-file-upload label="Ad picture" hint="Wide picture, about 1080 x 360 (3:1)." [previewUrl]="imageUrl() ? img(imageUrl()) : null" [busy]="busy()" [removable]="false" (fileSelected)="takeImage($event)"></app-file-upload>
        </div>

        <div class="grid">
          <label>When tapped, open
            <select name="targetType" [(ngModel)]="targetType" (ngModelChange)="target = ''">
              <option value="None">Nothing</option>
              <option value="Product">A product</option>
              <option value="Category">A category</option>
              <option value="Offer">An offer</option>
              <option value="Url">A web page</option>
            </select>
          </label>
          <label *ngIf="targetType === 'Url'">Web link<input name="target" [(ngModel)]="target" maxlength="300" placeholder="https://" /></label>
          <label *ngIf="targetType === 'Category'">Category
            <select name="category" [(ngModel)]="target"><option value="">Choose…</option><option *ngFor="let c of categories()" [value]="c.id">{{ c.name }}</option></select>
          </label>
          <label *ngIf="targetType === 'Offer'">Offer
            <select name="offer" [(ngModel)]="target"><option value="">Choose…</option><option *ngFor="let o of offers()" [value]="o.id">{{ o.name }}</option></select>
          </label>
          <label *ngIf="targetType === 'Product'">Search a product
            <span class="fee-row"><input name="productSearch" [(ngModel)]="productSearch" placeholder="Product name" (keyup.enter)="searchProducts()" /><button type="button" class="act" [disabled]="busy()" (click)="searchProducts()">Search</button></span>
          </label>
        </div>
        <div class="chips" *ngIf="targetType === 'Product'">
          <button type="button" class="chip" *ngFor="let p of productResults()" (click)="pickProduct(p)" [class.on]="target === p.id">{{ p.name }}</button>
          <span *ngIf="target && pickedProductName()" class="picked">Opens: {{ pickedProductName() }}</span>
        </div>

        <label class="check"><input type="checkbox" name="active" [(ngModel)]="isActive" /> On (customers see it while it is between its dates)</label>
        <div class="buttons">
          <button class="primary" type="submit" [disabled]="busy() || !imageUrl() || f.invalid">{{ busy() ? 'Saving…' : (editing() ? 'Save changes' : 'Create ad') }}</button>
          <button type="button" class="ghost" *ngIf="editing()" (click)="reset()">Cancel</button>
        </div>
      </form>
        <app-phone-preview [ad]="{ placement: placement, title: title, imageUrl: imageUrl() ? img(imageUrl()) : null }"></app-phone-preview>
      </div>

      <p *ngIf="loading()">Loading ads…</p>
      <p *ngIf="!loading() && ads().length === 0">No ads yet.</p>
      <ng-container *ngIf="ads().length">
        <app-data-table [columns]="columns" [rows]="pageRows()" [startIndex]="(shownPage() - 1) * pageSize()" [activeId]="editing()?.id ?? null" minWidth="1040px">
          <ng-template appCell="picture" let-a><img class="thumb" [src]="img(a.imageUrl)" alt="" /><small *ngIf="a.title">{{ a.title }}</small></ng-template>
          <ng-template appCell="where" let-a>{{ label(a.placement) }}<small>Order {{ a.sortOrder }}</small></ng-template>
          <ng-template appCell="opens" let-a>{{ opens(a) }}</ng-template>
          <ng-template appCell="shop" let-a>{{ storeName(a.storeId) }}</ng-template>
          <ng-template appCell="dates" let-a>{{ a.startsAt | date:'mediumDate' }}<small *ngIf="a.endsAt">to {{ a.endsAt | date:'mediumDate' }}</small></ng-template>
          <ng-template appCell="views" let-a>{{ a.views }}</ng-template>
          <ng-template appCell="taps" let-a>{{ a.taps }}<small *ngIf="a.views > 0">{{ rate(a) }} of views</small></ng-template>
          <ng-template appCell="status" let-a><span class="badge" [ngClass]="'badge-' + a.status.toLowerCase()">{{ a.status }}</span></ng-template>
          <ng-template appCell="action" let-a><span class="actions"><button class="act" [disabled]="busy()" (click)="edit(a)">Edit</button>
            <button class="act" [disabled]="busy()" (click)="toggle(a)">{{ a.isActive ? 'Switch off' : 'Switch on' }}</button>
            <button class="act danger" [disabled]="busy()" (click)="remove(a)">Delete</button></span></ng-template>
        </app-data-table>
        <app-pagination [page]="shownPage()" [pageSize]="pageSize()" [total]="ads().length" (pageChange)="page.set($event)" (pageSizeChange)="pageSize.set($event)"></app-pagination>
      </ng-container>
    </section>
  `,
  styles: [`
    app-data-table small{display:block;font-size:11px;color:var(--muted);margin-top:2px}
    .edit-card{display:grid!important;gap:12px}

    .with-preview{display:grid;grid-template-columns:minmax(0,1fr) 320px;gap:16px;align-items:start}
    .with-preview>app-phone-preview{position:sticky;top:0}
    @media(max-width:1100px){.with-preview{grid-template-columns:1fr}.with-preview>app-phone-preview{position:static}}

    :host{display:block;font-family:'Plus Jakarta Sans',sans-serif;color:var(--ink)}
    h2{font-family:'Bricolage Grotesque',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:var(--ink)}
    .muted{color:var(--muted);font-size:12px;margin:0 0 20px}
    .page{max-width:1280px;display:grid;gap:16px}
    .card{background:var(--card-bg,#F5F5F7);border-radius:24px;padding:20px;display:grid;gap:12px;color:var(--on-card,#141118)}
    h3{margin:0;font:800 16px 'Bricolage Grotesque',sans-serif}
    .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px}
    label{display:grid;gap:4px;font-size:11px;font-weight:700}
    label.check{display:flex;gap:8px;align-items:center}
    input,select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118}
    input[type=checkbox]{width:auto}
    .hint{color:var(--on-card-muted,#5E5A66);font-size:11px}
    .picture{display:grid;gap:8px}
    .fee-row{display:flex;gap:8px;align-items:center;flex-wrap:wrap}
    .preview{max-width:360px;width:100%;border-radius:14px;border:1px solid rgba(20,17,24,.15)}
    .chips{display:flex;gap:6px;flex-wrap:wrap;align-items:center}
    .chip{border:1px solid rgba(20,17,24,.25);border-radius:999px;background:#fff;padding:5px 12px;font:700 11px 'Plus Jakarta Sans',sans-serif;cursor:pointer}
    .chip.on{background:#FFD400;border-color:#FFD400}
    .picked{font-size:11px;font-weight:700}
    .buttons{display:flex;gap:10px}
    .primary{border:0;border-radius:999px;background:#FFD400;color:#141118;padding:10px 20px;font:800 12px 'Plus Jakarta Sans',sans-serif;cursor:pointer}
    .primary:disabled{opacity:.5;cursor:default}
    .ghost{border:1px solid rgba(20,17,24,.35);border-radius:999px;background:transparent;padding:10px 20px;font:800 12px 'Plus Jakarta Sans',sans-serif;cursor:pointer}
    .table{background:var(--card-bg,#F5F5F7);border-radius:24px;padding:8px 20px;color:var(--on-card,#141118);overflow-x:auto}
    .row{display:grid;grid-template-columns:1.3fr 1.2fr 1.2fr 1fr 1fr .6fr .8fr .7fr 1.6fr;gap:12px;align-items:center;min-width:1000px;padding:12px 0;border-top:1px solid rgba(20,17,24,.1);font-size:12px}
    .row.head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .row small{display:block;color:var(--on-card-muted,#5E5A66);font-size:10px}
    .thumb{width:120px;height:40px;object-fit:cover;border-radius:8px;background:#ddd;display:block}
    .badge{justify-self:start;border-radius:999px;padding:3px 10px;font-size:10px;font-weight:800;background:#ECECF0}
    .badge-live{background:#14A44D;color:#fff}.badge-scheduled{background:#FFD400}.badge-off,.badge-ended{background:#ECECF0}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
    .act{border:0;border-radius:999px;padding:6px 12px;font:800 11px 'Plus Jakarta Sans',sans-serif;cursor:pointer;background:#EEEAFF;color:#141118}
    .act.danger{background:#C42F1D}
    .act:disabled{opacity:.5;cursor:default}
    .error{color:#C42F1D;font-weight:700}
    .notice{color:#0B6B33;font-weight:700}
  `],
})
export class AdsComponent implements OnInit {
  private api = inject(ApiService);
  private config = inject(ConfigService);

  readonly placements = AD_PLACEMENTS;
  readonly ads = signal<Ad[]>([]);
  readonly columns: DataColumn[] = [{ key: 'picture', label: 'Picture' }, { key: 'where', label: 'Where' }, { key: 'opens', label: 'Opens' }, { key: 'shop', label: 'Store' }, { key: 'dates', label: 'Dates' }, { key: 'views', label: 'Views' }, { key: 'taps', label: 'Taps' }, { key: 'status', label: 'Status' }, { key: 'action', label: 'Action' }];
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  /** The page asked for, or the last one there is after the list shrinks. */
  readonly shownPage = computed(() => Math.min(this.page(), pageCount(this.ads().length, this.pageSize())));
  readonly pageRows = computed(() => this.ads().slice((this.shownPage() - 1) * this.pageSize(), this.shownPage() * this.pageSize()));
  readonly stores = signal<Choice[]>([]);
  readonly categories = signal<Choice[]>([]);
  readonly offers = signal<Choice[]>([]);
  readonly productResults = signal<Choice[]>([]);
  readonly editing = signal<Ad | null>(null);
  readonly imageUrl = signal<string | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  private readonly pickedName = signal<string | null>(null);

  placement = 'home';
  title = '';
  storeId = '';
  sortOrder = 0;
  startsAt = '';
  endsAt = '';
  targetType: AdTarget = 'None';
  target = '';
  isActive = true;
  productSearch = '';
  uploadFile: File | null = null;

  ngOnInit() {
    this.loading.set(true);
    this.load();
    this.api.get<{ id: string; name: string; isActive?: boolean }[]>('/stores').subscribe({ next: r => this.stores.set((r.data ?? []).filter(s => s.isActive !== false)), error: () => {} });
    this.api.get<{ id: string; name: string; isActive?: boolean }[]>('/categories').subscribe({ next: r => this.categories.set((r.data ?? []).filter(c => c.isActive !== false)), error: () => {} });
    this.api.get<{ id: string; titleEn: string; status: string }[]>('/admin/campaigns').subscribe({
      next: r => this.offers.set((r.data ?? []).filter(o => o.status !== 'Cancelled').map(o => ({ id: o.id, name: o.titleEn }))),
      error: () => {},
    });
  }

  img(url: string | null | undefined): string { return url && url.startsWith('/') ? this.config.getBaseUrl().replace(/\/api\/?$/, '') + url : (url ?? ''); }
  label(key: string) { return placementLabel(key); }
  hint() { return AD_PLACEMENTS.find(p => p.key === this.placement)?.hint ?? ''; }
  rate(a: Ad) { return a.views > 0 ? Math.round((a.taps / a.views) * 1000) / 10 + '%' : ''; }
  storeName(id: string | null) { return id ? (this.stores().find(s => s.id === id)?.name ?? 'One store') : 'All stores'; }
  pickedProductName() { return this.pickedName(); }

  opens(a: Ad): string {
    switch (a.targetType) {
      case 'Url': return a.target ?? 'Web page';
      case 'Category': return 'Category: ' + (this.categories().find(c => c.id === a.target)?.name ?? '');
      case 'Offer': return 'Offer: ' + (this.offers().find(o => o.id === a.target)?.name ?? '');
      case 'Product': return 'A product';
      default: return 'Nothing';
    }
  }

  load() {
    this.api.get<Ad[]>('/admin/ads').subscribe({
      next: r => { this.ads.set(r.data ?? []); this.loading.set(false); },
      error: e => { this.error.set(e?.message ?? 'Could not load ads.'); this.loading.set(false); },
    });
  }

  /** The upload card hands over a checked picture, which is sent at once. */
  takeImage(file: File) { this.uploadFile = file; this.uploadImage(); }

  pickImage(event: Event) { this.uploadFile = (event.target as HTMLInputElement).files?.[0] ?? null; }

  uploadImage() {
    if (!this.uploadFile || this.busy()) return;
    const data = new FormData();
    data.append('file', this.uploadFile);
    this.busy.set(true); this.error.set(null);
    this.api.post<{ imageUrl: string }>('/admin/campaigns/image', data).subscribe({
      next: r => { this.busy.set(false); this.imageUrl.set(r.data?.imageUrl ?? null); this.uploadFile = null; this.notice.set('Picture uploaded.'); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not upload the picture.'); },
    });
  }

  searchProducts() {
    this.api.get<{ id: string; name: string }[]>('/admin/campaigns/products?search=' + encodeURIComponent(this.productSearch.trim())).subscribe({
      next: r => this.productResults.set((r.data ?? []).map(p => ({ id: p.id, name: p.name }))),
      error: e => this.error.set(e?.message ?? 'Could not search products.'),
    });
  }

  pickProduct(p: Choice) { this.target = p.id; this.pickedName.set(p.name); }

  private body(over: Partial<{ isActive: boolean }> = {}) {
    return {
      placement: this.placement,
      imageUrl: this.imageUrl(),
      title: this.title.trim() || null,
      targetType: this.targetType,
      target: this.targetType === 'None' ? null : (this.target.trim() || null),
      storeId: this.storeId || null,
      startsAt: this.startsAt ? new Date(this.startsAt).toISOString() : null,
      endsAt: this.endsAt ? new Date(this.endsAt).toISOString() : null,
      isActive: this.isActive,
      sortOrder: Number(this.sortOrder ?? 0),
      ...over,
    };
  }

  reset() {
    this.editing.set(null);
    this.placement = 'home'; this.title = ''; this.storeId = ''; this.sortOrder = 0; this.startsAt = ''; this.endsAt = '';
    this.targetType = 'None'; this.target = ''; this.isActive = true; this.productSearch = ''; this.uploadFile = null;
    this.imageUrl.set(null); this.pickedName.set(null); this.productResults.set([]);
  }

  edit(a: Ad) {
    this.editing.set(a);
    this.placement = a.placement; this.title = a.title ?? ''; this.storeId = a.storeId ?? ''; this.sortOrder = a.sortOrder;
    this.startsAt = toLocal(a.startsAt); this.endsAt = toLocal(a.endsAt); this.targetType = a.targetType; this.target = a.target ?? '';
    this.isActive = a.isActive; this.imageUrl.set(a.imageUrl); this.pickedName.set(null);
    this.error.set(null); this.notice.set(null);
  }

  save() {
    if (this.busy() || !this.imageUrl()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const editing = this.editing();
    const call = editing ? this.api.put<Ad>('/admin/ads/' + editing.id, this.body()) : this.api.post<Ad>('/admin/ads', this.body());
    call.subscribe({
      next: () => { this.busy.set(false); this.notice.set(editing ? 'Ad saved.' : 'Ad created.'); this.reset(); this.load(); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not save the ad.'); },
    });
  }

  /** Switching on or off sends the ad as it is with only that flag changed. */
  toggle(a: Ad) {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const body = { placement: a.placement, imageUrl: a.imageUrl, title: a.title, targetType: a.targetType, target: a.target, storeId: a.storeId, startsAt: a.startsAt, endsAt: a.endsAt, isActive: !a.isActive, sortOrder: a.sortOrder };
    this.api.put<Ad>('/admin/ads/' + a.id, body).subscribe({
      next: () => { this.busy.set(false); this.notice.set(a.isActive ? 'Ad switched off.' : 'Ad switched on.'); this.load(); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not change the ad.'); },
    });
  }

  remove(a: Ad) {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    this.api.delete<boolean>('/admin/ads/' + a.id).subscribe({
      next: () => { this.busy.set(false); this.notice.set('Ad deleted.'); if (this.editing()?.id === a.id) this.reset(); this.load(); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not delete the ad.'); },
    });
  }
}
