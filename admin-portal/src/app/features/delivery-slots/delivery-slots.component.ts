import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { CellDirective, DataColumn, DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { DEFAULT_PAGE_SIZE, PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { SidePanelComponent } from '../../shared/components/side-panel/side-panel.component';

interface Settings { enabled: boolean; open: string; close: string; windowMinutes: number; capacity: number; cutoffMinutes: number; daysAhead: number; asapAllowed: boolean; }
interface StoreRules { storeId: string; name: string; override: Settings | null; effective: Settings; }
interface Rules { default: Settings; stores: StoreRules[]; }

const BLANK: Settings = { enabled: false, open: '09:00', close: '21:00', windowMinutes: 60, capacity: 10, cutoffMinutes: 60, daysAhead: 2, asapAllowed: true };

/**
 * When customers can choose a delivery time. A default for every store and, if a store needs different hours or a different limit, its own
 * settings. Switching it off means every order is "as soon as possible". Changes affect new orders only; orders already booked keep their window.
 */
@Component({
  selector: 'app-delivery-slots',
  standalone: true,
  imports: [CommonModule, FormsModule, DataTableComponent, CellDirective, PaginationComponent, SidePanelComponent],
  template: `
    <section class="page">
      <header>
        <div>
          <h2>Delivery times</h2>
          <p class="muted">The delivery windows customers can choose, in India time. Changes affect new orders only; orders already booked keep their window.</p>
        </div>
      </header>

      <article class="default" *ngIf="rules() as r">
        <div><strong>Default for every store</strong><span>{{ describe(r.default) }}</span></div>
        <span class="chip" [class.on]="r.default.enabled">{{ r.default.enabled ? 'Customers choose a time' : 'As soon as possible' }}</span>
        <button type="button" class="primary" [disabled]="busy()" (click)="edit('default')">Edit default</button>
      </article>

      <div class="filters">
        <label>Search<input type="search" name="search" placeholder="Store name" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" /></label>
        <label>Settings<select name="kind" [ngModel]="kind()" (ngModelChange)="kind.set($event); page.set(1)"><option value="all">All stores</option><option value="own">With own settings</option><option value="default">Using the default</option></select></label>
        <label>Delivery windows<select name="windows" [ngModel]="windows()" (ngModelChange)="windows.set($event); page.set(1)"><option value="all">On or off</option><option value="on">On</option><option value="off">Off</option></select></label>
      </div>

      <p *ngIf="error() && !editing()" class="error" role="alert">{{ error() }}</p>
      <p *ngIf="notice()" class="notice" role="status">{{ notice() }}</p>

      <app-data-table [fill]="true" [columns]="columns" [rows]="pageRows()" [loading]="!rules() && !error()" [activeId]="changedId()" [startIndex]="(page() - 1) * pageSize()" emptyText="No store matches.">
        <ng-template appCell="store" let-s><strong>{{ s.name }}</strong><small>{{ s.override ? 'own settings' : 'uses the default' }}</small></ng-template>
        <ng-template appCell="hours" let-s><ng-container *ngIf="s.effective.enabled; else offText">{{ s.effective.open }} – {{ s.effective.close }}<small>{{ s.effective.windowMinutes }}-minute windows</small></ng-container><ng-template #offText><span class="chip">{{ s.effective.asapAllowed ? 'As soon as possible' : 'Off' }}</span></ng-template></ng-template>
        <ng-template appCell="limit" let-s><ng-container *ngIf="s.effective.enabled; else dash">{{ s.effective.capacity }} orders each<small>{{ s.effective.daysAhead }} day(s) · cut-off {{ s.effective.cutoffMinutes }} min</small></ng-container><ng-template #dash>—</ng-template></ng-template>
        <ng-template appCell="summary" let-s><span class="summary" [title]="describe(s.effective)">{{ describe(s.effective) }}</span></ng-template>
        <ng-template appCell="action" let-s>
          <span class="actions">
            <button type="button" class="act" [disabled]="busy()" (click)="edit(s.storeId)">{{ s.override ? 'Edit' : 'Set own' }}</button>
            <button type="button" class="act ghost" *ngIf="s.override" [disabled]="busy()" (click)="useDefault(s.storeId)">Use default</button>
          </span>
        </ng-template>
      </app-data-table>

      <app-pagination [page]="page()" [pageSize]="pageSize()" [total]="shown().length" (pageChange)="page.set($event)" (pageSizeChange)="pageSize.set($event)"></app-pagination>

      <app-side-panel [open]="!!editing()" [title]="editing() === 'default' ? 'Default for every store' : 'Settings for ' + nameOf(editing() ?? '')" (closed)="editing.set(null)">
        <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
        <label class="check"><input type="checkbox" name="enabled" [(ngModel)]="form.enabled" /> Customers can choose a delivery time</label>
        <div class="two">
          <label>Opens<input type="time" name="open" [(ngModel)]="form.open" step="300" /></label>
          <label>Closes<input type="time" name="close" [(ngModel)]="form.close" step="300" /></label>
        </div>
        <label>Window length<select name="window" [(ngModel)]="form.windowMinutes"><option *ngFor="let m of windowChoices" [ngValue]="m">{{ m }} minutes</option></select></label>
        <label>Orders per window<input type="number" name="capacity" [(ngModel)]="form.capacity" min="1" max="500" step="1" /></label>
        <label>Cut-off (minutes before a window starts)<input type="number" name="cutoff" [(ngModel)]="form.cutoffMinutes" min="0" max="1440" step="5" /></label>
        <label>Days to choose from (today included)<input type="number" name="days" [(ngModel)]="form.daysAhead" min="1" max="7" step="1" /></label>
        <label class="check"><input type="checkbox" name="asap" [(ngModel)]="form.asapAllowed" /> Also offer "as soon as possible"</label>
        <div panelActions class="actions">
          <button type="button" class="act ghost" (click)="editing.set(null)">Cancel</button>
          <button type="button" class="primary" [disabled]="busy()" (click)="save()">{{ busy() ? 'Saving…' : 'Save' }}</button>
        </div>
      </app-side-panel>
    </section>
  `,
  styles: [`
    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{max-width:1280px;width:100%;flex:1;min-height:0;display:flex;flex-direction:column;gap:12px}
    h2{font-family:var(--display);font-size:28px;letter-spacing:-1px;margin:0 0 4px}
    .muted{color:var(--muted);font-size:12px;margin:0}
    .default{display:flex;gap:14px;align-items:center;flex-wrap:wrap;background:#fff;border:1px solid var(--line);border-left:6px solid var(--yellow);border-radius:18px;padding:14px 18px}
    .default>div{flex:1;min-width:240px;display:grid;gap:3px;font-size:12px}
    .default span:not(.chip){color:var(--muted)}
    .filters{display:flex;gap:10px;flex-wrap:wrap}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:var(--muted)}
    label.check{display:flex;gap:8px;align-items:center}
    input,select{border:1.5px solid #D9D7DF;border-radius:12px;padding:9px 12px;font:inherit;font-size:12px;background:#fff;color:var(--ink);min-width:0}
    .filters input{min-width:240px}
    input[type=checkbox]{width:auto}
    .two{display:grid;grid-template-columns:1fr 1fr;gap:10px}
    small{display:block;color:var(--muted);font-size:11px;margin-top:2px}
    .summary{display:block;max-width:320px;color:var(--muted);font-size:11px}
    .chip{display:inline-block;border-radius:999px;padding:4px 11px;font-size:11px;font-weight:800;background:var(--line)}
    .chip.on{background:#E3F7EA;color:var(--success)}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
    .primary,.act{border-radius:999px;font:800 11px 'Plus Jakarta Sans',sans-serif;cursor:pointer;color:var(--ink)}
    .primary{border:1px solid var(--yellow);background:var(--yellow);padding:10px 18px}
    .act{border:1px solid #D5CEF5;background:var(--soft);padding:6px 12px}
    .act.ghost{background:#fff;border-color:#D9D7DF}
    .primary:disabled,.act:disabled{opacity:.5;cursor:default}
    .error{color:var(--danger);font-weight:700;font-size:12px;margin:0}
    .notice{color:var(--success);font-weight:700;font-size:12px;margin:0}
  `],
})
export class DeliverySlotsComponent implements OnInit {
  private api = inject(ApiService);

  readonly columns: DataColumn[] = [{ key: 'store', label: 'Store' }, { key: 'hours', label: 'Hours' }, { key: 'limit', label: 'Limits' }, { key: 'summary', label: 'In words' }, { key: 'action', label: 'Action' }];
  readonly rules = signal<Rules | null>(null);
  /** 'default' or a store id while the form is open. */
  readonly editing = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly search = signal('');
  readonly kind = signal('all');
  readonly windows = signal('all');
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  readonly changedId = signal<string | null>(null);
  readonly windowChoices = [30, 45, 60, 90, 120, 180];
  form: Settings = { ...BLANK };

  /** The stores matching the search and filters, by name. */
  readonly shown = computed(() => {
    const text = this.search().trim().toLowerCase(), kind = this.kind(), windows = this.windows();
    return (this.rules()?.stores ?? [])
      .filter(s => (!text || s.name.toLowerCase().includes(text)) && (kind === 'all' || !!s.override === (kind === 'own')) && (windows === 'all' || s.effective.enabled === (windows === 'on')))
      .sort((a, b) => a.name.localeCompare(b.name));
  });
  readonly pageRows = computed(() => this.shown().slice((this.page() - 1) * this.pageSize(), this.page() * this.pageSize()).map(s => ({ ...s, id: s.storeId })));

  ngOnInit() {
    this.api.get<Rules>('/admin/delivery-slots').subscribe({ next: r => this.rules.set(r.data ?? null), error: e => this.error.set(e?.message ?? 'Could not load delivery times.') });
  }

  nameOf(storeId: string) { return this.rules()?.stores.find(s => s.storeId === storeId)?.name ?? 'this store'; }

  describe(s: Settings): string {
    if (!s.enabled) return s.asapAllowed ? 'As soon as possible only' : 'Off';
    return `${s.open} to ${s.close}, ${s.windowMinutes}-minute windows, ${s.capacity} orders each, ${s.daysAhead} ${s.daysAhead === 1 ? 'day' : 'days'}, cut-off ${s.cutoffMinutes} min` + (s.asapAllowed ? ', or as soon as possible' : '');
  }

  edit(target: string) {
    const r = this.rules();
    if (!r) return;
    const current = target === 'default' ? r.default : (r.stores.find(s => s.storeId === target)?.effective ?? r.default);
    this.form = { ...current };
    this.editing.set(target);
    this.changedId.set(target === 'default' ? null : target);
    this.error.set(null); this.notice.set(null);
  }

  save() {
    const target = this.editing();
    if (!target || this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const body: Settings = {
      ...this.form,
      windowMinutes: Number(this.form.windowMinutes), capacity: Number(this.form.capacity), cutoffMinutes: Number(this.form.cutoffMinutes), daysAhead: Number(this.form.daysAhead),
    };
    const path = target === 'default' ? '/admin/delivery-slots/default' : '/admin/delivery-slots/stores/' + target;
    this.api.put<Rules>(path, body).subscribe({
      next: r => { this.busy.set(false); this.rules.set(r.data ?? this.rules()); this.editing.set(null); this.notice.set('Saved. New orders use it.'); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not save.'); },
    });
  }

  useDefault(storeId: string) {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null); this.changedId.set(storeId);
    this.api.delete<Rules>('/admin/delivery-slots/stores/' + storeId).subscribe({
      next: r => { this.busy.set(false); this.rules.set(r.data ?? this.rules()); this.editing.set(null); this.notice.set(this.nameOf(storeId) + ' uses the default again.'); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not change.'); },
    });
  }
}
