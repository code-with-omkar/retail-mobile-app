import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';

interface StoreCommission { storeId: string; name: string; overridePercent: number | null; effectivePercent: number; }
interface CommissionData { defaultPercent: number; stores: StoreCommission[]; }

/**
 * The commission the platform takes from each store's product sales (after discounts; delivery and handling fees stay with the store).
 * A default for every store, and an optional percent per store. The percent in force when an order is placed is written onto that order.
 */
@Component({
  selector: 'app-commission',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <h2>Commission</h2>
      <p class="muted">What we earn from each store's sales. Changing a percent affects new orders only; orders already placed keep theirs.</p>
      <p *ngIf="error()" class="error">{{ error() }}</p>
      <p *ngIf="notice()" class="notice">{{ notice() }}</p>
      <section class="card" *ngIf="data()">
        <div class="row"><strong>Default for every store</strong><input type="number" min="0" max="100" step="0.01" name="def" [(ngModel)]="defaultPercent" /><span>%</span><button type="button" class="act" [disabled]="busy()" (click)="saveDefault()">Save</button></div>
        <div class="row" *ngFor="let s of data()!.stores">
          <span>{{ s.name }}<small *ngIf="s.overridePercent === null"> (default {{ data()!.defaultPercent }}%)</small></span>
          <input type="number" min="0" max="100" step="0.01" [name]="'c-' + s.storeId" [ngModel]="storePercent()[s.storeId]" (ngModelChange)="setStore(s.storeId, $event)" [placeholder]="'default ' + data()!.defaultPercent" />
          <span>%</span>
          <button type="button" class="act" [disabled]="busy()" (click)="saveStore(s.storeId)">Save</button>
          <button type="button" class="act ghost" *ngIf="s.overridePercent !== null" [disabled]="busy()" (click)="clearStore(s.storeId)">Use default</button>
        </div>
      </section>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 20px}
    .page{max-width:900px}
    .card{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:20px;display:grid;gap:12px;color:var(--on-card,#141118)}
    .row{display:flex;gap:10px;align-items:center;font-size:13px}
    .row>strong,.row>span:first-child{flex:1}
    .row small{color:var(--on-card-muted,#5E5A66)}
    input{width:110px;border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118}
    .act{border:0;border-radius:999px;padding:6px 12px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
    .act:disabled{opacity:.5;cursor:default}
    .act.ghost{background:transparent;color:#141118;border:1px solid rgba(20,17,24,.35)}
    .error{color:#FFB3D9;font-weight:700}
    .notice{color:#02F34C;font-weight:700}
  `],
})
export class CommissionComponent implements OnInit {
  private api = inject(ApiService);
  readonly data = signal<CommissionData | null>(null);
  readonly storePercent = signal<Record<string, string>>({});
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  defaultPercent = '0';

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
  saveStore(id: string) { this.save('/admin/commission/stores/' + id, this.storePercent()[id] ?? ''); }
  clearStore(id: string) { this.save('/admin/commission/stores/' + id, null); }
}
