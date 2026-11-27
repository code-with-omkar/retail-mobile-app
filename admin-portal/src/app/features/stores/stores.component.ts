import { AuthService } from '../../core/services/auth.service';
import { SidePanelComponent } from '../../shared/components/side-panel/side-panel.component';
import { LoaderComponent } from '../../shared/components/loader/loader.component';
import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection, facets, filterState } from '../../shared/components/filter-layout/filter-state';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { Store } from '../../core/models/domain.model';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { areaOf } from '../../shared/utils/stores';

/** The shops in the caller's scope as cards: where each is, how far it delivers, and whether it is open for business. */
const blankStore = () => ({ name: '', address: '', latitude: null as number | null, longitude: null as number | null, serviceRadiusKm: 6, code: '', phoneNumber: '', isActive: true });

@Component({
  selector: 'app-stores',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent, FilterLayoutComponent, LoaderComponent, SidePanelComponent],
  template: `
    <section class="page">
      <header>
        <div>
          <h2>Stores</h2>
          <p class="muted">{{ stores().length }} in your scope · {{ activeCount() }} active</p>
        </div>
        <button type="button" class="primary" *ngIf="isAdmin()" (click)="edit(null)">＋ New store</button>
      </header>

      <app-filter-layout [fill]="true" [groups]="groups()" [selected]="filters.selected()" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <input filterSearch type="search" name="search" aria-label="Search" placeholder="Name or address" [value]="search()" (input)="search.set($any($event.target).value)" />
        <app-loader *ngIf="loading()" message="Loading stores…"></app-loader>
        <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
        <p *ngIf="notice()" class="notice" role="status">{{ notice() }}</p>
        <p *ngIf="!loading() && !error() && stores().length === 0" class="empty">No stores found.</p>
        <p *ngIf="stores().length && !shown().length" class="empty">No store matches your search.</p>

        <div class="list">
          <article *ngFor="let store of shown(); let i = index">
            <div class="top">
              <div class="avatar" [ngClass]="'t' + (i % 4)">{{ store.name.charAt(0).toUpperCase() }}</div>
              <div class="info"><strong>{{ store.name }}</strong><span class="pill" [class.active]="store.isActive">{{ store.isActive ? 'Active' : 'Inactive' }}</span></div>
              <span class="number">#{{ i + 1 }}</span>
            </div>
            <p class="address"><app-icon name="store" [size]="15"></app-icon>{{ store.address }}</p>
            <div class="facts">
              <span *ngIf="store.serviceRadiusKm"><app-icon name="clock" [size]="14"></app-icon>Delivers within {{ store.serviceRadiusKm }} km</span>
              <span class="area">{{ areaName(store) }}</span>
              <span *ngIf="store.code">Code {{ store.code }}</span>
              <span *ngIf="store.phoneNumber">{{ store.phoneNumber }}</span>
            </div>
            <div class="foot">
              <a class="act" *ngIf="store.latitude && store.longitude" [href]="mapLink(store)" target="_blank" rel="noopener noreferrer">Open map</a>
              <button type="button" class="act" *ngIf="isAdmin()" (click)="edit(store)">Edit</button>
            </div>
          </article>
        </div>
      </app-filter-layout>

      <app-side-panel [open]="panelOpen()" [title]="editing() ? 'Edit ' + editing()!.name : 'New store'" (closed)="panelOpen.set(false)">
        <p class="error" role="alert" *ngIf="saveError()">{{ saveError() }}</p>
        <label class="field">Name<input name="storeName" maxlength="160" [(ngModel)]="form.name" /></label>
        <label class="field">Address<textarea name="storeAddress" rows="3" maxlength="500" [(ngModel)]="form.address"></textarea></label>
        <label class="field">Latitude<input type="number" step="0.000001" name="storeLat" [(ngModel)]="form.latitude" placeholder="19.0420" /></label>
        <label class="field">Longitude<input type="number" step="0.000001" name="storeLon" [(ngModel)]="form.longitude" placeholder="73.0700" /></label>
        <p class="tip">In a maps app, press and hold on the store to copy its latitude and longitude.
          <a *ngIf="form.latitude !== null && form.longitude !== null" [href]="mapLink(form)" target="_blank" rel="noopener noreferrer">Check on the map</a></p>
        <label class="field">Delivery radius (km)<input type="number" min="0.5" max="50" step="0.5" name="storeRadius" [(ngModel)]="form.serviceRadiusKm" /></label>
        <label class="field">Code (starts order numbers)<input name="storeCode" maxlength="8" [(ngModel)]="form.code" placeholder="KHG" /></label>
        <label class="field">Phone (optional)<input name="storePhone" maxlength="20" [(ngModel)]="form.phoneNumber" /></label>
        <label class="check"><input type="checkbox" name="storeActive" [(ngModel)]="form.isActive" /> Open for orders</label>
        <div panelActions class="actions">
          <button type="button" class="act ghost" (click)="panelOpen.set(false)">Cancel</button>
          <button type="button" class="primary" [disabled]="busy()" (click)="save()">{{ busy() ? 'Saving…' : 'Save store' }}</button>
        </div>
      </app-side-panel>
    </section>
  `,
  styles: [`
    .field{display:grid;gap:4px;font-size:11px;font-weight:700}
    .field input,.field textarea{border:1.5px solid #D9D7DF;border-radius:12px;padding:8px 10px;font:inherit;font-size:13px}
    .check{display:flex;gap:10px;align-items:center;font-size:13px;font-weight:700}
    .tip{margin:0;font-size:12px;color:var(--muted)}

    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{max-width:1280px;width:100%;flex:1;min-height:0;display:flex;flex-direction:column;gap:14px}
    header{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;flex-wrap:wrap}
    h2{font-family:var(--display);font-size:28px;letter-spacing:-1px;margin:0 0 4px}
    .muted{color:var(--muted);font-size:12px;margin:0}
    .filters{display:flex;gap:10px;flex-wrap:wrap}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:var(--muted)}
    input,select{border:1.5px solid #D9D7DF;border-radius:12px;padding:9px 12px;font:inherit;font-size:12px;background:#fff;color:var(--ink)}
    input{min-width:220px}
    .error{color:var(--danger);font-weight:700;font-size:12px}
    .empty{color:var(--muted);font-size:12px}
    .list{flex:1;min-height:0;overflow-y:auto;align-content:start;padding:4px 6px 12px 2px;display:grid;grid-template-columns:repeat(auto-fill,minmax(290px,1fr));grid-auto-rows:max-content;gap:14px}
    article{background:#fff;border:1px solid var(--line);border-radius:20px;padding:18px;display:grid;gap:12px;align-content:start;transition:box-shadow .15s ease,transform .15s ease}
    article:hover{box-shadow:0 14px 34px rgba(40,24,90,.10);transform:translateY(-2px)}
    .top{display:flex;align-items:center;gap:12px}
    .avatar{width:46px;height:46px;border-radius:15px;display:grid;place-items:center;font-family:var(--display);font-weight:800;font-size:18px;flex-shrink:0}
    .t0{background:#FFF1A8}.t1{background:#D9F7E3}.t2{background:#EEEAFF}.t3{background:#FFE3EF}
    .info{flex:1;min-width:0;display:grid;gap:4px;justify-items:start}
    .info strong{font-size:14px;font-weight:800}
    .number{color:var(--muted);font-size:11px;font-weight:700;align-self:flex-start}
    .pill{padding:2px 10px;border-radius:999px;font-size:10px;font-weight:800;background:var(--line);color:var(--muted)}
    .pill.active{background:#E3F7EA;color:var(--success)}
    .address{display:flex;gap:8px;margin:0;color:var(--muted);font-size:12px;line-height:1.5}
    .facts{display:flex;gap:8px;flex-wrap:wrap;border-top:1px dashed var(--line);padding-top:12px}
    .facts span,.facts a{display:inline-flex;gap:6px;align-items:center;background:var(--card);border-radius:999px;padding:5px 11px;font-size:11px;font-weight:700;color:var(--ink);text-decoration:none}
    .facts a{background:var(--soft)}
    .list>article{display:flex;flex-direction:column;gap:12px}
    .list>article .address{min-height:36px}
    .list>article .facts{min-height:44px;align-content:flex-start}
    .foot{display:flex;gap:10px;justify-content:flex-end;margin-top:auto}
  `],
})
export class StoresComponent implements OnInit {
  private api = inject(ApiService);
  readonly stores = signal<Store[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly search = signal('');
  readonly filters = filterState('stores');
  areaName(store: Store) { return areaOf(store.address); }
  readonly activeCount = computed(() => this.stores().filter(store => store.isActive).length);
  private readonly found = computed(() => {
    const text = this.search().trim().toLowerCase();
    const searched = this.stores().filter(store => !text || store.name.toLowerCase().includes(text) || (store.address ?? '').toLowerCase().includes(text));
    return facets<Store>(searched, this.filters.selected(), { area: store => areaOf(store.address), status: store => (store.isActive ? 'active' : 'inactive') });
  });
  readonly shown = computed(() => this.found().shown);
  readonly groups = computed<FilterGroup[]>(() => {
    const found = this.found();
    const areas = [...new Set(this.stores().map(store => areaOf(store.address)))].sort((a, b) => a.localeCompare(b));
    return [
      { key: 'area', label: 'Area', options: areas.map(a => ({ value: a, label: a, count: found.count('area', a) })) },
      { key: 'status', label: 'Status', options: [['active', 'Active'], ['inactive', 'Inactive']].map(([value, label]) => ({ value, label, count: found.count('status', value) })) },
    ];
  });
  onFilters(next: Selection) { this.filters.set(next); }
  clearFilters() { this.filters.clear(); this.search.set(''); }

  private auth = inject(AuthService, { optional: true });
  isAdmin() { return !!this.auth?.isAdmin?.(); }

  readonly panelOpen = signal(false);
  readonly editing = signal<Store | null>(null);
  readonly busy = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  form = blankStore();

  edit(store: Store | null) {
    this.editing.set(store);
    this.form = store ? { name: store.name, address: store.address, latitude: store.latitude, longitude: store.longitude, serviceRadiusKm: store.serviceRadiusKm, code: store.code ?? '', phoneNumber: store.phoneNumber ?? '', isActive: store.isActive } : blankStore();
    this.saveError.set(null); this.notice.set(null); this.panelOpen.set(true);
  }

  save() {
    if (this.busy()) return;
    const f = this.form;
    if (!f.name.trim() || !f.address.trim() || f.latitude === null || f.longitude === null || !f.code.trim()) { this.saveError.set('Fill in the name, address, latitude, longitude and code.'); return; }
    const body = { name: f.name.trim(), address: f.address.trim(), latitude: Number(f.latitude), longitude: Number(f.longitude), serviceRadiusKm: Number(f.serviceRadiusKm), code: f.code.trim().toUpperCase(), phoneNumber: f.phoneNumber.trim() || null, isActive: f.isActive };
    const editing = this.editing();
    this.busy.set(true); this.saveError.set(null);
    const call = editing ? this.api.put<Store>('/admin/stores/' + editing.id, body) : this.api.post<Store>('/admin/stores', body);
    call.subscribe({
      next: () => { this.busy.set(false); this.panelOpen.set(false); this.notice.set(editing ? 'Store saved.' : 'Store added. Set its stock on the Stock screen.'); this.load(); },
      error: e => { this.busy.set(false); this.saveError.set(e?.error?.message ?? e?.message ?? 'Could not save the store.'); },
    });
  }

  mapLink(store: { latitude: number | null; longitude: number | null } | Store) { return `https://www.google.com/maps/search/?api=1&query=${store.latitude},${store.longitude}`; }

  ngOnInit() { this.loading.set(true); this.load(); }

  load() {
    // an administrator also sees the closed stores
    this.api.get<Store[]>(this.isAdmin() ? '/admin/stores' : '/stores').subscribe({
      next: r => { this.stores.set([...(r.data ?? [])].sort((a, b) => a.name.localeCompare(b.name))); this.loading.set(false); },
      error: e => { this.error.set(e.message ?? 'Unable to load stores'); this.loading.set(false); },
    });
  }
}
