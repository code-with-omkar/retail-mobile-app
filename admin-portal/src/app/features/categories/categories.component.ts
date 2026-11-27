import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection } from '../../shared/components/filter-layout/filter-state';
import { LoaderComponent } from '../../shared/components/loader/loader.component';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Observable, catchError, concatMap, from, map, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { ConfigService } from '../../core/services/config.service';
import { Store } from '../../core/models/domain.model';
import { SidePanelComponent } from '../../shared/components/side-panel/side-panel.component';
import { storeChoices } from '../../shared/utils/stores';

export interface AdminCategory { id: string; name: string; parentCategoryId: string | null; isActive: boolean; productCount: number; }

/** A top-level category with the sub-categories under it. */
export interface CategoryGroup { category: AdminCategory; subs: AdminCategory[]; }

/** A product a store sells, as the catalogue lists it. */
export interface StoreProduct { id: string; name: string; categoryId: string; imageUrl?: string | null; unitOfMeasure?: string; }

/** Top-level categories (A to Z), each with its sub-categories (A to Z). A sub-category whose parent is missing is shown as top level. */
export function groupCategories(all: AdminCategory[]): CategoryGroup[] {
  const byName = (a: AdminCategory, b: AdminCategory) => a.name.localeCompare(b.name);
  const ids = new Set(all.map(c => c.id));
  const tops = all.filter(c => !c.parentCategoryId || !ids.has(c.parentCategoryId)).sort(byName);
  return tops.map(category => ({ category, subs: all.filter(c => c.parentCategoryId === category.id).sort(byName) }));
}

export type Side = 'out' | 'in';

const PAGE = 50;
/** A shop's list is read page by page up to this many pages (1,000 products). */
const MAX_PAGES = 20;

/**
 * Categories and what is in them. Choose a category: the left box lists the chosen shop's products that are not in it, the right box the
 * ones that are. Drag between the boxes, or tick and use the buttons. Every product belongs to exactly one category, so taking one out
 * moves it to the category chosen in "Move to". A product's category is the same for every shop. The category tree is two levels deep.
 */
@Component({
  selector: 'app-categories',
  standalone: true,
  imports: [CommonModule, FormsModule, SidePanelComponent, LoaderComponent, FilterLayoutComponent],
  template: `
    <section class="page">
      <div class="top"><div><h2>Categories</h2>
      <p class="muted">Choose a category, then move products between the two boxes. A product's category is the same in every shop.</p></div>
        <div class="top-actions">
          <ng-container *ngIf="selected() as c">
            <span class="state" [class.off]="!c.isActive">{{ c.isActive ? 'Shown in the app' : 'Hidden' }}</span>
            <button type="button" class="ghost" (click)="edit(c)">Edit category</button>
            <button type="button" class="ghost" *ngIf="!c.parentCategoryId" (click)="startNew(c.id)">＋ Sub-category</button>
          </ng-container>
          <button type="button" class="primary" (click)="startNew('')">＋ Main category</button>
        </div>
      </div>

      <app-filter-layout [fill]="true" [groups]="filterGroups()" [selected]="selection()" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <p *ngIf="error() && !panelOpen()" class="error" role="alert">{{ error() }}</p>
        <p *ngIf="notice()" class="notice" role="status">{{ notice() }}</p>
        <p *ngIf="!loading() && !items().length" class="empty">No categories yet. Add a main category to begin.</p>

        <div class="boxes" *ngIf="selected() as c">
          <section class="box" aria-label="Products not in this category" [class.over]="overSide() === 'out'" (dragover)="over($event, 'out')" (dragleave)="leave('out')" (drop)="drop($event, 'out')">
            <div class="box-head">
              <h3>Not in {{ c.name }} <small>{{ outside().length }}</small></h3>
              <input type="search" name="outSearch" placeholder="Search these products" [ngModel]="outSearch()" (ngModelChange)="outSearch.set($event)" />
              <div class="bar">
                <label class="check"><input type="checkbox" name="allOut" [checked]="allTicked('out')" (change)="tickAll('out')" /> Select all</label>
                <button type="button" class="primary" [disabled]="busy() || !ticked(outside()).length" (click)="addTicked()">Add {{ ticked(outside()).length || '' }} to {{ c.name }} →</button>
              </div>
            </div>
            <div class="scroll">
              <app-loader *ngIf="loadingProducts()" message="Loading products…"></app-loader>
              <p class="empty" *ngIf="!loadingProducts() && !outside().length">No other products in this store.</p>
              <article *ngFor="let p of outside(); let i = index; trackBy: trackProduct" class="product" draggable="true" [class.changed]="changed().has(p.id)" (dragstart)="grab($event, p)" (dragend)="release()">
                <span class="no">{{ i + 1 }}</span>
                <input type="checkbox" [attr.aria-label]="'Select ' + p.name" [checked]="picked().has(p.id)" (change)="tick(p.id)" />
                <img *ngIf="p.imageUrl; else letterOut" [src]="img(p.imageUrl)" alt="" draggable="false" />
                <ng-template #letterOut><span class="letter">{{ p.name.charAt(0).toUpperCase() }}</span></ng-template>
                <span class="name">{{ p.name }}<small>{{ p.unitOfMeasure }} · {{ labelOf(p.categoryId) }}</small></span>
                <button type="button" class="pill" [disabled]="busy()" (click)="move([p], c.id)">Add →</button>
              </article>
            </div>
          </section>

          <section class="box mine" aria-label="Products in this category" [class.over]="overSide() === 'in'" (dragover)="over($event, 'in')" (dragleave)="leave('in')" (drop)="drop($event, 'in')">
            <div class="box-head">
              <h3>In {{ c.name }} <small>{{ inside().length }}</small></h3>
              <input type="search" name="inSearch" placeholder="Search these products" [ngModel]="inSearch()" (ngModelChange)="inSearch.set($event)" />
              <div class="bar">
                <label class="check"><input type="checkbox" name="allIn" [checked]="allTicked('in')" (change)="tickAll('in')" /> Select all</label>
                <label class="moveto">Move removed to<select name="removeTo" [ngModel]="removeTo()" (ngModelChange)="removeTo.set($event)"><option *ngFor="let o of otherChoices()" [value]="o.id">{{ o.label }}</option></select></label>
                <button type="button" class="pill" [disabled]="busy() || !removeTo() || !ticked(inside()).length" (click)="removeTicked()">← Remove {{ ticked(inside()).length || '' }}</button>
              </div>
            </div>
            <div class="scroll">
              <p class="empty" *ngIf="!loadingProducts() && !inside().length">No products of this store are in this category yet. Drag them here.</p>
              <article *ngFor="let p of inside(); let i = index; trackBy: trackProduct" class="product" draggable="true" [class.changed]="changed().has(p.id)" (dragstart)="grab($event, p)" (dragend)="release()">
                <span class="no">{{ i + 1 }}</span>
                <input type="checkbox" [attr.aria-label]="'Select ' + p.name" [checked]="picked().has(p.id)" (change)="tick(p.id)" />
                <img *ngIf="p.imageUrl; else letterIn" [src]="img(p.imageUrl)" alt="" draggable="false" />
                <ng-template #letterIn><span class="letter">{{ p.name.charAt(0).toUpperCase() }}</span></ng-template>
                <span class="name">{{ p.name }}<small>{{ p.unitOfMeasure }}</small></span>
                <button type="button" class="pill" [disabled]="busy() || !removeTo()" (click)="move([p], removeTo())">← Remove</button>
              </article>
            </div>
          </section>
        </div>
      </app-filter-layout>

      <app-side-panel [open]="panelOpen()" [title]="editing() ? 'Edit ' + editing()!.name : 'New category'" (closed)="reset()">
        <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
        <label>Name<input name="name" [(ngModel)]="name" maxlength="60" required (keyup.enter)="save()" /></label>
        <label>Under
          <select name="parent" [(ngModel)]="parentId">
            <option value="">Nothing (a main category)</option>
            <option *ngFor="let p of parentChoices()" [value]="p.id">{{ p.name }}</option>
          </select>
        </label>
        <label class="check"><input type="checkbox" name="active" [(ngModel)]="isActive" /> Shown in the app</label>
        <div panelActions class="bar">
          <button type="button" class="pill" (click)="reset()">Cancel</button>
          <button type="button" class="primary" [disabled]="busy() || !name.trim()" (click)="save()">{{ busy() ? 'Saving…' : (editing() ? 'Save changes' : 'Add category') }}</button>
        </div>
      </app-side-panel>
    </section>
  `,
  styles: [`
    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{max-width:1280px;width:100%;flex:1;min-height:0;display:flex;flex-direction:column;gap:12px}
    header{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;flex-wrap:wrap}
    h2{font-family:var(--display);font-size:28px;letter-spacing:-1px;margin:0 0 4px}
    h3{margin:0;font:800 14px 'Plus Jakarta Sans',sans-serif;display:flex;gap:8px;align-items:center}
    h3 small{background:var(--soft);border-radius:999px;padding:2px 9px;font-size:11px}
    .muted{color:var(--muted);font-size:12px;margin:0}
    .chooser{display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;background:#fff;border:1px solid var(--line);border-left:6px solid var(--yellow);border-radius:18px;padding:12px 16px}
    .chooser .grow{flex:1;min-width:220px}
    .chooser .big{font-size:14px;font-weight:800}
    .state{align-self:center;border-radius:999px;padding:4px 11px;font-size:11px;font-weight:800;background:#E3F7EA;color:var(--success)}
    .state.off{background:var(--line);color:var(--muted)}
    .boxes{flex:1;min-height:360px;display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);grid-template-rows:minmax(0,1fr);gap:14px}
    .box{min-height:0;display:flex;flex-direction:column;overflow:hidden;background:#fff;border:2px solid var(--line);border-radius:20px;transition:border-color .15s ease,box-shadow .15s ease}
    .box.mine{border-color:#D6CEFB}
    .box.over{border-color:var(--yellow);box-shadow:0 0 0 4px rgba(255,212,0,.35)}
    .box-head{display:grid;gap:10px;padding:12px 14px;background:var(--card);border-bottom:1px solid var(--line)}
    .bar{display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;justify-content:space-between}
    .scroll{flex:1;min-height:0;overflow-y:auto;padding:10px 12px;display:flex;flex-direction:column;gap:7px}
    .scroll>*{flex-shrink:0}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:var(--muted)}
    label.check{display:flex;gap:8px;align-items:center}
    input,select{border:1.5px solid #D9D7DF;border-radius:12px;padding:8px 10px;font:inherit;font-size:12px;background:#fff;color:var(--ink);min-width:0;max-width:100%}
    input[type=checkbox]{width:16px;height:16px;padding:0;accent-color:#8B7CF6}
    small{display:block;color:var(--muted);font-size:11px;font-weight:600}
    .product{display:grid;grid-template-columns:24px 36px 38px minmax(0,1fr) auto;gap:10px;align-items:center;border:1px solid var(--line);border-radius:14px;padding:7px 10px;cursor:grab;font-size:12px;font-weight:700;background:#fff}
    .product:active{cursor:grabbing}
    .product:hover{border-color:#C9C0F0}
    .product.changed{border-color:#14A44D;background:#E3F7EA}
    .product img,.letter{width:38px;height:38px;border-radius:11px;object-fit:cover}
    .letter{display:grid;place-items:center;background:var(--soft);font-weight:800}
    .no{color:var(--muted);font-size:11px;text-align:right}
    .name{min-width:0}
    .primary,.pill{border-radius:999px;font:800 11px 'Plus Jakarta Sans',sans-serif;cursor:pointer;color:var(--ink);white-space:nowrap}
    .primary{border:1px solid var(--yellow);background:var(--yellow);padding:9px 16px}
    .pill{border:1px solid #D9D7DF;background:#fff;padding:7px 12px}
    .primary:disabled,.pill:disabled{opacity:.45;cursor:default}
    .error{color:var(--danger);font-weight:700;font-size:12px;margin:0}
    .notice{color:var(--success);font-weight:700;font-size:12px;margin:0}
    .empty{color:var(--muted);font-size:12px;text-align:center}
    @media(max-width:960px){.boxes{grid-template-columns:1fr;grid-template-rows:auto;overflow-y:auto}.box{height:440px}}
    @media(max-width:560px){.product{grid-template-columns:18px 38px minmax(0,1fr)}.product .no{display:none}.product .pill{grid-column:1/-1}}
    @media(prefers-reduced-motion:reduce){.box{transition:none}}
  
    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{flex:1;min-height:0;width:100%;display:flex!important;flex-direction:column;gap:12px}
    .top{display:flex;justify-content:space-between;align-items:flex-end;gap:12px;flex-wrap:wrap}
    .top h2{margin:0 0 4px}
    .top .muted{margin:0;font-size:12px;color:var(--muted)}
    .top-actions{display:flex;gap:10px;flex-wrap:wrap}
    app-data-table small{display:block;font-size:11px;color:var(--muted);margin-top:2px}
`],
})
export class CategoriesComponent implements OnInit {
  readonly selection = computed<Selection>(() => ({ category: this.selectedId() ? [this.selectedId()] : [], store: this.storeId() ? [this.storeId()] : [] }));
  readonly filterGroups = computed<FilterGroup[]>(() => [
    { key: 'category', label: 'Category', single: true, required: true, options: this.choices().map(c => ({ value: c.id, label: c.label })) },
    { key: 'store', label: 'Store', single: true, required: true, options: this.stores().map(s => ({ value: s.id, label: s.name })) },
  ]);
  onFilters(next: Selection) {
    const category = next['category']?.[0], store = next['store']?.[0];
    if (category && category !== this.selectedId()) this.choose(category);
    if (store && store !== this.storeId()) this.pickStore(store);
  }
  clearFilters() { this.outSearch.set(''); this.inSearch.set(''); }
  private api = inject(ApiService);
  private config = inject(ConfigService, { optional: true });

  readonly items = signal<AdminCategory[]>([]);
  readonly groups = computed(() => groupCategories(this.items()));
  /** Every category by its full name, for the lists: "Dairy", "Dairy › Milk". */
  readonly choices = computed(() => this.groups().flatMap(g => [{ id: g.category.id, label: g.category.name }, ...g.subs.map(s => ({ id: s.id, label: g.category.name + ' › ' + s.name }))]));
  readonly selectedId = signal('');
  readonly selected = computed(() => this.items().find(c => c.id === this.selectedId()) ?? null);
  readonly otherChoices = computed(() => this.choices().filter(c => c.id !== this.selectedId()));
  /** Where products taken out of the chosen category go. */
  readonly removeTo = signal('');
  readonly stores = signal<Store[]>([]);
  readonly storeId = signal('');
  readonly products = signal<StoreProduct[]>([]);
  readonly loadingProducts = signal(false);
  readonly outSearch = signal('');
  readonly inSearch = signal('');
  readonly outside = computed(() => this.matching(this.products().filter(p => p.categoryId !== this.selectedId()), this.outSearch()));
  readonly inside = computed(() => this.matching(this.products().filter(p => p.categoryId === this.selectedId()), this.inSearch()));
  readonly picked = signal<ReadonlySet<string>>(new Set());
  /** The products just moved, marked in the list. */
  readonly changed = signal<ReadonlySet<string>>(new Set());
  readonly dragging = signal<StoreProduct | null>(null);
  readonly overSide = signal<Side | null>(null);
  readonly editing = signal<AdminCategory | null>(null);
  readonly panelOpen = signal(false);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  private asked = 0;

  name = '';
  parentId = '';
  isActive = true;

  trackProduct = (_: number, p: StoreProduct) => p.id;
  img(url: string): string { return url.startsWith('/') ? (this.config?.getBaseUrl().replace(/\/api\/?$/, '') ?? '') + url : url; }
  labelOf(categoryId: string) { return this.choices().find(c => c.id === categoryId)?.label ?? 'No category'; }
  private matching(list: StoreProduct[], search: string) {
    const text = search.trim().toLowerCase();
    return text ? list.filter(p => p.name.toLowerCase().includes(text)) : list;
  }

  ngOnInit() {
    this.load();
    this.api.get<Store[]>('/stores').subscribe({
      next: r => { this.stores.set(storeChoices(r.data)); if (!this.storeId()) this.storeId.set(this.stores()[0]?.id ?? ''); this.loadProducts(); },
      error: e => this.error.set(e?.message ?? 'Could not load the stores.'),
    });
  }

  load() {
    this.loading.set(true);
    this.api.get<AdminCategory[]>('/admin/catalog/categories').subscribe({
      next: r => {
        this.items.set(r.data ?? []); this.loading.set(false);
        // The first category by name is chosen until another one is.
        if (this.selected()) { this.keepRemoveTarget(); return; }
        const said = this.notice(); // choosing clears the message, which must stay after a save
        this.choose(this.choices()[0]?.id ?? '');
        this.notice.set(said);
      },
      error: e => { this.error.set(e?.message ?? 'Could not load categories.'); this.loading.set(false); },
    });
  }

  choose(id: string) {
    this.selectedId.set(id); this.picked.set(new Set()); this.changed.set(new Set()); this.outSearch.set(''); this.inSearch.set(''); this.notice.set(null);
    this.removeTo.set(''); this.keepRemoveTarget();
  }

  /** Removed products go to the parent category when there is one, otherwise to the first other category. */
  private keepRemoveTarget() {
    if (this.otherChoices().some(c => c.id === this.removeTo())) return;
    const parent = this.selected()?.parentCategoryId;
    this.removeTo.set(parent && this.otherChoices().some(c => c.id === parent) ? parent : this.otherChoices()[0]?.id ?? '');
  }

  // ---- the shop's products ----
  pickStore(id: string) { this.storeId.set(id); this.picked.set(new Set()); this.changed.set(new Set()); this.loadProducts(); }

  /** The whole list of the shop, so both boxes are complete. */
  loadProducts(page = 1, held: StoreProduct[] = [], request = ++this.asked) {
    if (!this.storeId()) return;
    this.loadingProducts.set(true);
    this.api.get<{ items: StoreProduct[]; totalCount: number }>('/catalog/products', { params: { storeId: this.storeId(), carriedOnly: true, page, pageSize: PAGE } }).subscribe({
      next: r => {
        if (request !== this.asked) return;
        const batch = r.data?.items ?? [], all = [...held, ...batch];
        if (batch.length && all.length < (r.data?.totalCount ?? 0) && page < MAX_PAGES) { this.loadProducts(page + 1, all, request); return; }
        this.products.set(all);
        this.loadingProducts.set(false);
      },
      error: e => { if (request !== this.asked) return; this.loadingProducts.set(false); this.error.set(e?.message ?? 'Could not load the products.'); },
    });
  }

  // ---- choosing and moving ----
  tick(id: string) { const next = new Set(this.picked()); if (!next.delete(id)) next.add(id); this.picked.set(next); }
  ticked(list: StoreProduct[]) { return list.filter(p => this.picked().has(p.id)); }
  allTicked(side: Side) { const list = side === 'out' ? this.outside() : this.inside(); return list.length > 0 && list.every(p => this.picked().has(p.id)); }
  tickAll(side: Side) {
    const list = side === 'out' ? this.outside() : this.inside(), all = this.allTicked(side), next = new Set(this.picked());
    for (const p of list) { if (all) next.delete(p.id); else next.add(p.id); }
    this.picked.set(next);
  }
  addTicked() { this.move(this.ticked(this.outside()), this.selectedId()); }
  removeTicked() { this.move(this.ticked(this.inside()), this.removeTo()); }

  /** Moves products into a category one after another; each keeps its other details, and each answer is reported. */
  move(products: StoreProduct[], categoryId: string) {
    const moving = products.filter(p => p.categoryId !== categoryId);
    if (this.busy() || !categoryId || !moving.length) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const done: string[] = [], failed: string[] = [];
    from(moving).pipe(concatMap(p => this.assign(p, categoryId))).subscribe({
      next: result => {
        if (result.problem) { failed.push(result.product.name + ': ' + result.problem); return; }
        done.push(result.product.name);
        this.products.set(this.products().map(x => (x.id === result.product.id ? { ...x, categoryId } : x)));
        this.changed.set(new Set([...this.changed(), result.product.id]));
      },
      complete: () => {
        this.busy.set(false); this.picked.set(new Set());
        if (done.length) this.notice.set((done.length === 1 ? done[0] + ' is' : done.length + ' products are') + ' now in ' + this.labelOf(categoryId) + '.');
        if (failed.length) this.error.set('Not moved. ' + failed.join('; '));
        this.load();
      },
    });
  }

  private assign(product: StoreProduct, categoryId: string): Observable<{ product: StoreProduct; problem?: string }> {
    const url = '/admin/catalog/products/' + product.id;
    return this.api.get<{ name: string; description: string; unitOfMeasure: string; isActive: boolean }>(url).pipe(
      switchMap(r => {
        const p = r.data;
        if (!p) return of({ product, problem: 'not found' });
        return this.api.put(url, { name: p.name, description: p.description, categoryId, unitOfMeasure: p.unitOfMeasure, isActive: p.isActive }).pipe(map(() => ({ product })));
      }),
      catchError(e => of({ product, problem: (e?.message as string) ?? 'could not be saved' })),
    );
  }

  // ---- dragging between the boxes ----
  grab(event: DragEvent, product: StoreProduct) { event.dataTransfer?.setData('text/plain', product.id); this.dragging.set(product); }
  release() { this.dragging.set(null); this.overSide.set(null); }
  /** A product can be dropped on the box it is not in. */
  accepts(side: Side) { const p = this.dragging(); return !!p && (side === 'in') === (p.categoryId !== this.selectedId()) && (side === 'in' || !!this.removeTo()); }
  over(event: DragEvent, side: Side) { if (this.accepts(side)) { event.preventDefault(); this.overSide.set(side); } }
  leave(side: Side) { if (this.overSide() === side) this.overSide.set(null); }
  drop(event: DragEvent, side: Side) {
    event.preventDefault();
    const product = this.dragging(), ok = this.accepts(side);
    this.release();
    if (!product || !ok) return;
    // Dragging one of the ticked products takes the whole ticked set of that box with it.
    const source = side === 'in' ? this.outside() : this.inside();
    this.move(this.picked().has(product.id) ? this.ticked(source) : [product], side === 'in' ? this.selectedId() : this.removeTo());
  }

  // ---- adding and editing categories ----
  /** Only a main category can be a parent, never the category being edited, and a category that has sub-categories cannot go under another. */
  parentChoices(): AdminCategory[] {
    const all = this.items();
    const own = this.editing();
    if (own && all.some(c => c.parentCategoryId === own.id)) return [];
    return all.filter(c => !c.parentCategoryId && c.id !== own?.id).sort((a, b) => a.name.localeCompare(b.name));
  }

  startNew(parentId: string) { this.reset(); this.parentId = parentId; this.error.set(null); this.notice.set(null); this.panelOpen.set(true); }

  reset() { this.panelOpen.set(false); this.editing.set(null); this.name = ''; this.parentId = ''; this.isActive = true; }

  edit(c: AdminCategory) {
    this.editing.set(c);
    this.name = c.name; this.parentId = c.parentCategoryId ?? ''; this.isActive = c.isActive;
    this.error.set(null); this.notice.set(null); this.panelOpen.set(true);
  }

  save() {
    if (this.busy() || !this.name.trim()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const editing = this.editing();
    const body = { name: this.name.trim(), parentCategoryId: this.parentId || null, isActive: this.isActive };
    const call = editing ? this.api.put<AdminCategory>('/admin/catalog/categories/' + editing.id, body) : this.api.post<AdminCategory>('/admin/catalog/categories', body);
    call.subscribe({
      next: r => {
        this.busy.set(false);
        const id = editing?.id ?? r.data?.id;
        this.reset();
        if (id && !editing) this.choose(id); // a new category is shown at once, ready to be filled
        this.notice.set(editing ? 'Category saved.' : 'Category added.');
        this.load();
      },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not save the category.'); },
    });
  }
}
