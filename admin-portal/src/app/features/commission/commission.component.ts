import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection, facets, filterState } from '../../shared/components/filter-layout/filter-state';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { CellDirective, DataColumn, DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { DEFAULT_PAGE_SIZE, PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface StoreCommission { storeId: string; name: string; overridePercent: number | null; effectivePercent: number; }
interface CommissionData { defaultPercent: number; stores: StoreCommission[]; }

/**
 * The commission the platform takes from each store's product sales (after discounts; delivery and handling fees stay with the store).
 * A default for every store, and an optional percent per store. The percent in force when an order is placed is written onto that order.
 */
@Component({
  selector: 'app-commission',
  standalone: true,
  imports: [CommonModule, FormsModule, DataTableComponent, CellDirective, PaginationComponent, FilterLayoutComponent],
  template: `
    <section class="page">
      <header>
        <h2>Commission</h2>
        <p class="muted">What we earn from each store's sales. Changing a percent affects new orders only; orders already placed keep theirs.</p>
      </header>

      <div class="tiles" *ngIf="data() as d">
        <article class="tile hero">
          <p>Default for every store</p>
          <div class="edit"><input type="number" min="0" max="100" step="0.01" name="def" aria-label="Default percent" [(ngModel)]="defaultPercent" (keyup.enter)="saveDefault()" /><span>%</span><button type="button" class="act dark" [disabled]="busy()" (click)="saveDefault()">Save</button></div>
        </article>
        <article class="tile"><p>Stores with their own percent</p><strong>{{ ownCount() }}</strong><small>of {{ d.stores.length }} stores</small></article>
        <article class="tile"><p>Highest in force</p><strong>{{ highest() }}%</strong><small>lowest {{ lowest() }}%</small></article>
      </div>

      <app-filter-layout [fill]="true" [groups]="groups()" [selected]="filters.selected()" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <input filterSearch type="search" name="search" aria-label="Search" placeholder="Store name" [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" />
        <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
        <p *ngIf="notice()" class="notice" role="status">{{ notice() }}</p>

        <app-data-table [fill]="true" [columns]="columns" [rows]="pageRows()" [loading]="!data() && !error()" [activeId]="changedId()" [startIndex]="(page() - 1) * pageSize()" emptyText="No store matches.">
          <ng-template appCell="store" let-s><strong>{{ s.name }}</strong><small>{{ s.overridePercent === null ? '(default ' + (data()?.defaultPercent ?? 0) + '%)' : 'own percent' }}</small></ng-template>
          <ng-template appCell="force" let-s><div class="meter"><span class="bar"><i [style.width.%]="s.effectivePercent ?? 0"></i></span><b>{{ s.effectivePercent ?? 0 }}%</b></div></ng-template>
          <ng-template appCell="own" let-s><span class="edit"><input type="number" min="0" max="100" step="0.01" [name]="'c-' + s.storeId" [attr.aria-label]="'Percent for ' + s.name" [ngModel]="storePercent()[s.storeId]" (ngModelChange)="setStore(s.storeId, $event)" (keyup.enter)="saveStore(s.storeId)" [placeholder]="'default ' + (data()?.defaultPercent ?? 0)" /><span>%</span></span></ng-template>
          <ng-template appCell="action" let-s>
            <span class="actions">
              <button type="button" class="act" [disabled]="busy()" (click)="saveStore(s.storeId)">Save</button>
              <button type="button" class="act ghost" *ngIf="s.overridePercent !== null" [disabled]="busy()" (click)="clearStore(s.storeId)">Use default</button>
            </span>
          </ng-template>
        </app-data-table>

        <app-pagination [page]="page()" [pageSize]="pageSize()" [total]="shown().length" (pageChange)="page.set($event)" (pageSizeChange)="pageSize.set($event)"></app-pagination>
      </app-filter-layout>
    </section>
  `,
  styles: [`
    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{max-width:1100px;width:100%;flex:1;min-height:0;display:flex;flex-direction:column;gap:12px}
    h2{font-family:var(--display);font-size:28px;letter-spacing:-1px;margin:0 0 4px}
    .muted{color:var(--muted);font-size:12px;margin:0}
    .tiles{display:grid;grid-template-columns:1.5fr 1fr 1fr;gap:12px}
    .tile{background:#fff;border:1px solid var(--line);border-radius:18px;padding:14px 16px;display:grid;gap:4px}
    .tile.hero{background:var(--yellow);border-color:var(--yellow)}
    .tile p{margin:0;font-size:11px;font-weight:700}
    .tile strong{font-family:var(--display);font-size:28px;letter-spacing:-1px}
    small{display:block;color:var(--muted);font-size:11px}
    .edit{display:flex;gap:8px;align-items:center}
    .filters{display:flex;gap:10px;flex-wrap:wrap}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:var(--muted)}
    input,select{border:1.5px solid #D9D7DF;border-radius:12px;padding:8px 11px;font:inherit;font-size:12px;background:#fff;color:var(--ink);min-width:0}
    .hero input{font-size:20px;font-weight:800;width:130px}
    .filters input{min-width:240px}
    .edit input[type=number]{width:120px}
    .meter{display:flex;gap:10px;align-items:center}
    .bar{width:150px;height:10px;border-radius:999px;background:var(--card);overflow:hidden;border:1px solid var(--line)}
    .bar i{display:block;height:100%;background:linear-gradient(90deg,#8B7CF6,#F59ACB);border-radius:999px}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
    .act{border:1px solid #D5CEF5;border-radius:999px;background:var(--soft);padding:6px 13px;font:800 11px 'Plus Jakarta Sans',sans-serif;color:var(--ink);cursor:pointer}
    .act.dark{background:#fff;border-color:#fff}
    .act.ghost{background:#fff;border-color:#D9D7DF}
    .act:disabled{opacity:.5;cursor:default}
    .error{color:var(--danger);font-weight:700;font-size:12px;margin:0}
    .notice{color:var(--success);font-weight:700;font-size:12px;margin:0}
    @media(max-width:800px){.tiles{grid-template-columns:1fr}.bar{width:80px}}
  `],
})
export class CommissionComponent implements OnInit {
  private api = inject(ApiService);
  readonly columns: DataColumn[] = [{ key: 'store', label: 'Store' }, { key: 'force', label: 'In force' }, { key: 'own', label: 'Own percent' }, { key: 'action', label: 'Action' }];
  readonly data = signal<CommissionData | null>(null);
  readonly storePercent = signal<Record<string, string>>({});
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly search = signal('');
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  readonly changedId = signal<string | null>(null);
  defaultPercent = '0';

  readonly filters = filterState('commission');
  private readonly found = computed(() => {
    const text = this.search().trim().toLowerCase();
    const searched = (this.data()?.stores ?? []).filter(s => !text || s.name.toLowerCase().includes(text));
    return facets(searched, this.filters.selected(), { kind: s => (s.overridePercent !== null ? 'own' : 'default') });
  });
  /** The stores matching the search and filters, by name. */
  readonly shown = computed(() => [...this.found().shown].sort((a, b) => a.name.localeCompare(b.name)));
  readonly groups = computed<FilterGroup[]>(() => {
    const found = this.found();
    return [
      { key: 'kind', label: 'Show', options: [['own','With own percent'],['default','Using the default']].map(([value, label]) => ({ value, label, count: found.count('kind', value) })) },
    ];
  });
  onFilters(next: Selection) { this.filters.set(next); this.page.set(1); }
  clearFilters() { this.filters.clear(); this.search.set(''); this.page.set(1); }
  readonly pageRows = computed(() => this.shown().slice((this.page() - 1) * this.pageSize(), this.page() * this.pageSize()).map(s => ({ ...s, id: s.storeId })));
  readonly ownCount = computed(() => this.data()?.stores.filter(s => s.overridePercent !== null).length ?? 0);
  readonly highest = computed(() => Math.max(0, ...(this.data()?.stores.map(s => s.effectivePercent ?? 0) ?? [])));
  readonly lowest = computed(() => (this.data()?.stores.length ? Math.min(...this.data()!.stores.map(s => s.effectivePercent ?? 0)) : 0));

  ngOnInit() {
    this.api.get<CommissionData>('/admin/commission').subscribe({ next: r => this.show(r.data), error: e => this.error.set(e?.message ?? 'Could not load commission.') });
  }

  setStore(id: string, value: string) { this.storePercent.set({ ...this.storePercent(), [id]: value }); }

  private show(d: CommissionData | undefined) {
    if (!d) return;
    this.data.set(d);
    this.defaultPercent = String(d.defaultPercent);
    this.storePercent.set(Object.fromEntries(d.stores.map(s => [s.storeId, s.overridePercent === null ? '' : String(s.overridePercent)])));
  }

  private save(path: string, value: string | null) {
    if (this.busy()) return;
    const percent = value === null || value === '' ? null : Number(value);
    if (percent !== null && (Number.isNaN(percent) || percent < 0 || percent > 100)) { this.error.set('Give a percent from 0 to 100.'); return; }
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    this.api.put<CommissionData>(path, { percent }).subscribe({
      next: r => { this.busy.set(false); this.notice.set('Saved. New orders use it.'); this.show(r.data); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not save.'); },
    });
  }

  saveDefault() { this.save('/admin/commission/default', this.defaultPercent); }
  saveStore(id: string) { this.changedId.set(id); this.save('/admin/commission/stores/' + id, this.storePercent()[id] ?? ''); }
  clearStore(id: string) { this.changedId.set(id); this.save('/admin/commission/stores/' + id, null); }
}
