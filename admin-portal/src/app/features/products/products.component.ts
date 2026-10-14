import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { Role } from '../../core/models/domain.model';

interface Variant { id: string; sku: string; label: string; price: number; mrp: number | null; sortOrder: number; isDefault: boolean; isActive: boolean; }
interface Translation { locale: string; name: string; description: string | null; }
interface AdminProduct {
  id: string; sku: string; name: string; description: string; categoryId: string; categoryName: string; unitOfMeasure: string;
  imageUrl: string | null; isActive: boolean; price: number; mrp: number | null; variants: Variant[]; translations: Translation[];
}
interface Page { items: AdminProduct[]; page: number; pageSize: number; totalCount: number; }
interface Category { id: string; name: string; isActive?: boolean; }

/**
 * The shared catalogue. Administrators create and edit products, Marathi text, sizes (price, MRP, default) and the picture;
 * store staff only look (their own stock is on the Stock page).
 */
@Component({
  selector: 'app-products',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <section class="page">
      <h2>Catalog</h2>
      <p class="muted" *ngIf="isAdmin()">Products are shared by every store. Prices and pictures you change here change for all stores. Each store sets its own stock on the <a routerLink="/stock">Stock</a> page.</p>
      <p class="muted" *ngIf="!isAdmin()">The catalogue is managed by an administrator. Set your own store's stock on the <a routerLink="/stock">Stock</a> page.</p>

      <div class="bar">
        <input class="search" name="search" [(ngModel)]="search" placeholder="Search name or SKU" (keyup.enter)="reload()" />
        <button class="act" type="button" (click)="reload()">Search</button>
        <button class="primary" type="button" *ngIf="isAdmin()" (click)="startNew()">New product</button>
      </div>
      <p class="error" *ngIf="error()">{{ error() }}</p>
      <p class="notice" *ngIf="notice()">{{ notice() }}</p>
      <p *ngIf="loading()">Loading products...</p>

      <section class="card" *ngIf="creating()">
        <h3>New product</h3>
        <div class="grid">
          <label>SKU<input name="nsku" [(ngModel)]="draft.sku" maxlength="64" /></label>
          <label>Name<input name="nname" [(ngModel)]="draft.name" maxlength="160" /></label>
          <label>Category<select name="ncat" [(ngModel)]="draft.categoryId"><option value="">Choose</option><option *ngFor="let c of categories()" [value]="c.id">{{ c.name }}</option></select></label>
          <label>Unit (first size)<input name="nunit" [(ngModel)]="draft.unitOfMeasure" maxlength="32" placeholder="e.g. 500 g" /></label>
          <label>Price (₹)<input type="number" min="0" step="0.01" name="nprice" [(ngModel)]="draft.price" /></label>
          <label>MRP (₹, optional)<input type="number" min="0" step="0.01" name="nmrp" [(ngModel)]="draft.mrp" /></label>
        </div>
        <label>Description<textarea name="ndesc" rows="2" [(ngModel)]="draft.description" maxlength="2000"></textarea></label>
        <div class="row-actions"><button class="primary" type="button" [disabled]="busy()" (click)="create()">Create</button><button class="act" type="button" (click)="creating.set(false)">Cancel</button></div>
      </section>

      <section class="card" *ngIf="editing() as p">
        <div class="head-row"><h3>{{ p.name }} <small>{{ p.sku }}</small></h3><button class="act" type="button" (click)="editing.set(null)">Close</button></div>

        <h4>Details</h4>
        <div class="grid">
          <label>Name<input name="ename" [(ngModel)]="form.name" maxlength="160" /></label>
          <label>Category<select name="ecat" [(ngModel)]="form.categoryId"><option *ngFor="let c of categories()" [value]="c.id">{{ c.name }}</option></select></label>
          <label>Unit<input name="eunit" [(ngModel)]="form.unitOfMeasure" maxlength="32" /></label>
          <label class="check"><input type="checkbox" name="eactive" [(ngModel)]="form.isActive" /> On sale (customers can see it)</label>
        </div>
        <label>Description<textarea name="edesc" rows="2" [(ngModel)]="form.description" maxlength="2000"></textarea></label>
        <div class="row-actions"><button class="primary" type="button" [disabled]="busy()" (click)="saveDetails()">Save details</button></div>

        <h4>Marathi</h4>
        <div class="grid">
          <label>Name (मराठी)<input name="mrname" [(ngModel)]="mr.name" maxlength="160" /></label>
          <label>Description (मराठी)<input name="mrdesc" [(ngModel)]="mr.description" maxlength="2000" /></label>
        </div>
        <div class="row-actions"><button class="primary" type="button" [disabled]="busy() || !mr.name.trim()" (click)="saveMarathi()">Save Marathi</button><button class="act" type="button" *ngIf="hasMarathi(p)" [disabled]="busy()" (click)="removeMarathi()">Remove Marathi</button></div>

        <h4>Sizes and prices</h4>
        <div class="table">
          <div class="vrow head"><span>Size</span><span>SKU</span><span>Price ₹</span><span>MRP ₹</span><span>Order</span><span>On</span><span></span></div>
          <div class="vrow" *ngFor="let v of sizes">
            <input [name]="'l' + v.id" [(ngModel)]="v.label" maxlength="40" />
            <input [name]="'s' + v.id" [(ngModel)]="v.sku" maxlength="80" />
            <input type="number" min="0" step="0.01" [name]="'p' + v.id" [(ngModel)]="v.price" />
            <input type="number" min="0" step="0.01" [name]="'m' + v.id" [(ngModel)]="v.mrp" />
            <input type="number" min="0" max="1000" [name]="'o' + v.id" [(ngModel)]="v.sortOrder" />
            <input type="checkbox" [name]="'a' + v.id" [(ngModel)]="v.isActive" [disabled]="v.isDefault" [title]="v.isDefault ? 'Make another size the default first' : ''" />
            <span class="actions">
              <button class="act" type="button" [disabled]="busy()" (click)="saveSize(v)">Save</button>
              <button class="act give" type="button" *ngIf="!v.isDefault" [disabled]="busy() || !v.isActive" (click)="makeDefault(v)">Make default</button>
              <em class="mine" *ngIf="v.isDefault">default</em>
            </span>
          </div>
        </div>
        <div class="vrow add">
          <input name="nl" [(ngModel)]="newSize.label" placeholder="New size, e.g. 1 kg" maxlength="40" />
          <input name="ns" [(ngModel)]="newSize.sku" placeholder="SKU" maxlength="80" />
          <input type="number" min="0" step="0.01" name="np" [(ngModel)]="newSize.price" placeholder="Price" />
          <input type="number" min="0" step="0.01" name="nm" [(ngModel)]="newSize.mrp" placeholder="MRP" />
          <input type="number" min="0" max="1000" name="no" [(ngModel)]="newSize.sortOrder" />
          <span></span>
          <button class="primary" type="button" [disabled]="busy()" (click)="addSize()">Add size</button>
        </div>

        <h4>Picture</h4>
        <div class="photo">
          <img *ngIf="p.imageUrl" [src]="img(p.imageUrl)" alt="" />
          <span class="noimg" *ngIf="!p.imageUrl">No picture</span>
          <div class="photo-actions">
            <input type="file" accept="image/jpeg,image/png,image/webp" (change)="picked($event)" />
            <small>JPEG, PNG or WebP, up to 2 MB.</small>
            <div class="row-actions"><button class="primary" type="button" [disabled]="busy() || !file" (click)="upload()">Upload</button><button class="act" type="button" *ngIf="p.imageUrl" [disabled]="busy()" (click)="removePicture()">Remove picture</button></div>
          </div>
        </div>
      </section>

      <div class="table" *ngIf="items().length">
        <div class="trow head"><span></span><span>Product</span><span>Category</span><span>Price</span><span>Status</span><span></span></div>
        <div class="trow" *ngFor="let p of items()">
          <span><img class="thumb" *ngIf="p.imageUrl" [src]="img(p.imageUrl)" alt="" /><span class="thumb ph" *ngIf="!p.imageUrl">{{ p.name.charAt(0).toUpperCase() }}</span></span>
          <span><strong>{{ p.name }}</strong><br /><small>{{ p.sku }} · {{ p.unitOfMeasure }}</small></span>
          <span>{{ p.categoryName }}</span>
          <span>{{ p.price | currency:'INR':'symbol':'1.2-2' }}<small *ngIf="p.variants.length > 1"> · {{ p.variants.length }} sizes</small></span>
          <span><span class="badge" [class.off]="!p.isActive">{{ p.isActive ? 'On sale' : 'Off' }}</span></span>
          <span><button class="act" type="button" *ngIf="isAdmin()" (click)="edit(p)">Edit</button></span>
        </div>
      </div>
      <p *ngIf="!loading() && !error() && items().length === 0">No products found.</p>
      <button class="act more" type="button" *ngIf="items().length < total()" [disabled]="loading()" (click)="more()">Show more ({{ items().length }} of {{ total() }})</button>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    h3{margin:0 0 8px;color:var(--on-card,#141118);font-size:16px}
    h3 small{font-weight:600;font-size:11px;color:var(--on-card-muted,#5E5A66)}
    h4{margin:14px 0 6px;color:var(--on-card,#141118);font-size:13px}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 16px}
    .muted a{color:#FFD400}
    .page{max-width:1200px}
    .bar{display:flex;gap:8px;margin-bottom:16px;flex-wrap:wrap}
    .search{border:1px solid rgba(20,17,24,.2);border-radius:999px;padding:8px 14px;font:inherit;background:#fff;color:#141118;min-width:240px}
    .error{color:#FFC6E2;font-weight:700}
    .notice{color:#02F34C;font-weight:700}
    .card{background:var(--card-bg,#EAE4FF);color:var(--on-card,#141118);border-radius:24px;padding:20px;display:grid;gap:10px;margin-bottom:20px}
    .head-row{display:flex;justify-content:space-between;align-items:center}
    .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:10px}
    label{display:grid;gap:4px;font-size:12px;font-weight:700;color:var(--on-card-muted,#5E5A66)}
    input,select,textarea{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118;min-width:0}
    .check{display:flex;gap:8px;align-items:center}
    .check input{width:auto}
    .row-actions{display:flex;gap:8px;flex-wrap:wrap}
    .primary{border:0;border-radius:999px;background:#FFD400;color:#141118;padding:9px 18px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .act{border:0;border-radius:999px;padding:7px 14px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
    .act.give{background:#02F34C;color:#141118}
    .primary:disabled,.act:disabled{opacity:.5;cursor:default}
    .more{margin-top:12px}
    .table{background:var(--card-bg,#EAE4FF);border-radius:24px;overflow:auto;margin-bottom:12px}
    .trow{display:grid;grid-template-columns:56px 2.4fr 1.2fr 1.4fr .8fr .7fr;gap:12px;padding:12px 18px;border-top:1px solid rgba(20,17,24,.12);min-width:760px;font-size:12px;color:var(--on-card-muted,#5E5A66);align-items:center}
    .trow strong{color:var(--on-card,#141118)}
    .vrow{display:grid;grid-template-columns:1.2fr 1.4fr .8fr .8fr .6fr .4fr 2fr;gap:8px;padding:8px 12px;border-top:1px solid rgba(20,17,24,.12);min-width:820px;align-items:center;font-size:12px}
    .vrow.add{border:0;background:transparent;padding:0}
    .head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .actions{display:flex;gap:6px;align-items:center}
    .thumb{width:44px;height:44px;border-radius:14px;object-fit:cover;display:grid;place-items:center;background:#C9C2F0;color:#141118;font-weight:800}
    .badge{padding:3px 10px;border-radius:999px;font-size:10px;font-weight:800;background:#02F34C;color:#141118}
    .badge.off{background:#C9C2F0}
    .mine{font-style:normal;background:#02F34C;color:#141118;border-radius:999px;padding:1px 8px;font-size:10px;font-weight:800}
    .photo{display:flex;gap:16px;align-items:flex-start;flex-wrap:wrap}
    .photo img{width:140px;height:140px;object-fit:cover;border-radius:18px;background:#fff}
    .noimg{width:140px;height:140px;border-radius:18px;background:#fff;display:grid;place-items:center;font-size:12px}
    .photo-actions{display:grid;gap:8px}
  `],
})
export class ProductsComponent implements OnInit {
  private api = inject(ApiService);
  private auth = inject(AuthService);
  private config = inject(ConfigService);

  readonly items = signal<AdminProduct[]>([]);
  readonly total = signal(0);
  readonly categories = signal<Category[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly creating = signal(false);
  readonly editing = signal<AdminProduct | null>(null);

  search = '';
  private page = 1;
  draft = this.blankDraft();
  form = { name: '', description: '', categoryId: '', unitOfMeasure: '', isActive: true };
  mr = { name: '', description: '' };
  sizes: Variant[] = [];
  newSize = { sku: '', label: '', price: null as number | null, mrp: null as number | null, sortOrder: 0 };
  file: File | null = null;

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }

  /** Pictures are saved as paths on the API (/media/...); anything else (a link) is used as it is. */
  img(url: string | null): string { return url && url.startsWith('/') ? this.config.getBaseUrl().replace(/\/api\/?$/, '') + url : (url ?? ''); }

  ngOnInit() {
    this.api.get<Category[]>('/categories').subscribe({ next: r => this.categories.set((r.data ?? []).filter(c => c.isActive !== false)), error: () => {} });
    this.reload();
  }

  private blankDraft() { return { sku: '', name: '', description: '', categoryId: '', unitOfMeasure: '', price: null as number | null, mrp: null as number | null }; }

  reload() { this.page = 1; this.items.set([]); this.load(); }
  more() { this.page++; this.load(); }

  private load() {
    this.loading.set(true); this.error.set(null);
    if (!this.isAdmin()) {
      this.api.get<any[]>('/products').subscribe({
        next: r => { const all = (r.data ?? []).filter(p => !this.search || (p.name + p.sku).toLowerCase().includes(this.search.toLowerCase())).map(p => ({ ...p, categoryName: '', variants: [], translations: [], mrp: null, imageUrl: p.imageUrl ?? null }) as AdminProduct); this.items.set(all); this.total.set(all.length); this.loading.set(false); },
        error: e => { this.error.set(e?.message ?? 'Unable to load products'); this.loading.set(false); },
      });
      return;
    }

    this.api.get<Page>('/admin/catalog/products', { params: { search: this.search.trim() || null, page: this.page, pageSize: 20 } }).subscribe({
      next: r => { this.items.set([...this.items(), ...(r.data?.items ?? [])]); this.total.set(r.data?.totalCount ?? 0); this.loading.set(false); },
      error: e => { this.error.set(e?.message ?? 'Unable to load products'); this.loading.set(false); },
    });
  }

  startNew() { this.editing.set(null); this.draft = this.blankDraft(); this.creating.set(true); this.notice.set(null); this.error.set(null); }

  create() {
    const d = this.draft;
    this.run(this.api.post<AdminProduct>('/admin/catalog/products', { sku: d.sku, name: d.name, description: d.description, categoryId: d.categoryId, unitOfMeasure: d.unitOfMeasure, price: d.price ?? 0, mrp: d.mrp }), 'Product created.', p => {
      this.creating.set(false); this.reload(); this.edit(p);
    });
  }

  edit(p: AdminProduct) {
    this.creating.set(false); this.notice.set(null); this.error.set(null); this.file = null;
    this.editing.set(p);
    this.form = { name: p.name, description: p.description, categoryId: p.categoryId, unitOfMeasure: p.unitOfMeasure, isActive: p.isActive };
    const mr = p.translations.find(t => t.locale === 'mr');
    this.mr = { name: mr?.name ?? '', description: mr?.description ?? '' };
    this.sizes = p.variants.map(v => ({ ...v }));
    this.newSize = { sku: '', label: '', price: null, mrp: null, sortOrder: p.variants.length };
  }

  hasMarathi(p: AdminProduct) { return p.translations.some(t => t.locale === 'mr'); }

  private current(): AdminProduct { return this.editing()!; }

  saveDetails() { this.run(this.api.put<AdminProduct>('/admin/catalog/products/' + this.current().id, this.form), 'Details saved.', p => this.refreshed(p)); }
  saveMarathi() { this.run(this.api.put<AdminProduct>('/admin/catalog/products/' + this.current().id + '/translations/mr', { name: this.mr.name, description: this.mr.description || null }), 'Marathi saved.', p => this.refreshed(p)); }
  removeMarathi() { this.run(this.api.delete<AdminProduct>('/admin/catalog/products/' + this.current().id + '/translations/mr'), 'Marathi removed.', p => this.refreshed(p)); }

  saveSize(v: Variant) {
    this.run(this.api.put<AdminProduct>('/admin/catalog/products/' + this.current().id + '/variants/' + v.id, { sku: v.sku, label: v.label, price: v.price ?? 0, mrp: v.mrp === null || v.mrp === undefined || (v.mrp as unknown) === '' ? null : v.mrp, sortOrder: v.sortOrder ?? 0, isActive: v.isActive }), 'Size saved.', p => this.refreshed(p));
  }

  makeDefault(v: Variant) { this.run(this.api.post<AdminProduct>('/admin/catalog/products/' + this.current().id + '/variants/' + v.id + '/default'), 'Default size changed.', p => this.refreshed(p)); }

  addSize() {
    const s = this.newSize;
    this.run(this.api.post<AdminProduct>('/admin/catalog/products/' + this.current().id + '/variants', { sku: s.sku, label: s.label, price: s.price ?? 0, mrp: s.mrp, sortOrder: s.sortOrder ?? 0, isActive: true }), 'Size added.', p => this.refreshed(p));
  }

  picked(event: Event) { this.file = (event.target as HTMLInputElement).files?.[0] ?? null; }

  upload() {
    if (!this.file) return;
    const data = new FormData();
    data.append('file', this.file);
    this.run(this.api.post<AdminProduct>('/admin/catalog/products/' + this.current().id + '/image', data), 'Picture uploaded.', p => { this.file = null; this.refreshed(p); });
  }

  removePicture() { this.run(this.api.delete<AdminProduct>('/admin/catalog/products/' + this.current().id + '/image'), 'Picture removed.', p => this.refreshed(p)); }

  private refreshed(p: AdminProduct) {
    this.items.set(this.items().map(item => item.id === p.id ? p : item));
    this.edit(p);
  }

  private run(call: ReturnType<ApiService['get']> | any, done: string, then: (p: AdminProduct) => void) {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    call.subscribe({
      next: (r: { data?: AdminProduct }) => { this.busy.set(false); this.notice.set(done); if (r.data) then(r.data); },
      error: (e: { message?: string }) => { this.busy.set(false); this.error.set(e?.message ?? 'Could not save.'); },
    });
  }
}
