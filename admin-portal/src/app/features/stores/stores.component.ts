import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { Store } from '../../core/models/domain.model';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { areaOf } from '../../shared/utils/stores';

/** The shops in the caller's scope as cards: where each is, how far it delivers, and whether it is open for business. */
@Component({
  selector: 'app-stores',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <section class="page">
      <header>
        <div>
          <h2>Stores</h2>
          <p class="muted">{{ stores().length }} in your scope · {{ activeCount() }} active</p>
        </div>
        <div class="filters">
          <label>Search<input name="search" type="search" placeholder="Name or address" [ngModel]="search()" (ngModelChange)="search.set($event)" /></label>
          <label>Area<select name="area" [ngModel]="area()" (ngModelChange)="area.set($event)"><option value="all">All areas</option><option *ngFor="let a of areas()" [value]="a">{{ a }}</option></select></label>
          <label>Status<select name="status" [ngModel]="status()" (ngModelChange)="status.set($event)"><option value="all">All stores</option><option value="active">Active</option><option value="inactive">Inactive</option></select></label>
        </div>
      </header>

      <p *ngIf="loading()">Loading stores...</p>
      <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
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
            <a *ngIf="store.latitude && store.longitude" [href]="mapLink(store)" target="_blank" rel="noopener noreferrer"><app-icon name="up" [size]="14"></app-icon>Open map</a>
          </div>
        </article>
      </div>
    </section>
  `,
  styles: [`
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
    .list{flex:1;min-height:0;overflow-y:auto;align-content:start;padding:4px 6px 12px 2px;display:grid;grid-template-columns:repeat(auto-fill,minmax(290px,1fr));gap:14px}
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
  `],
})
export class StoresComponent implements OnInit {
  private api = inject(ApiService);
  readonly stores = signal<Store[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly search = signal('');
  readonly status = signal('all');
  readonly area = signal('all');
  readonly areas = computed(() => [...new Set(this.stores().map(store => areaOf(store.address)))].sort((a, b) => a.localeCompare(b)));
  areaName(store: Store) { return areaOf(store.address); }
  readonly activeCount = computed(() => this.stores().filter(store => store.isActive).length);
  readonly shown = computed(() => {
    const text = this.search().trim().toLowerCase();
    const wanted = this.status();
    return this.stores().filter(store =>
      (wanted === 'all' || store.isActive === (wanted === 'active')) &&
      (this.area() === 'all' || areaOf(store.address) === this.area()) &&
      (!text || store.name.toLowerCase().includes(text) || (store.address ?? '').toLowerCase().includes(text)));
  });

  mapLink(store: Store) { return `https://www.google.com/maps/search/?api=1&query=${store.latitude},${store.longitude}`; }

  ngOnInit() {
    this.loading.set(true);
    this.api.get<Store[]>('/stores').subscribe({
      next: r => { this.stores.set([...(r.data ?? [])].sort((a, b) => a.name.localeCompare(b.name))); this.loading.set(false); },
      error: e => { this.error.set(e.message ?? 'Unable to load stores'); this.loading.set(false); },
    });
  }
}
