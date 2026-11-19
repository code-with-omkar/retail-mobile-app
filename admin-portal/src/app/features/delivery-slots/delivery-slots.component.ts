import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';

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
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <h2>Delivery times</h2>
      <p class="muted">Let customers choose a delivery window. Times are India time. Changes affect new orders only; orders already booked keep their window.</p>
      <p *ngIf="error()" class="error">{{ error() }}</p>
      <p *ngIf="notice()" class="notice">{{ notice() }}</p>

      <form class="card" *ngIf="editing() as target" (ngSubmit)="save()">
        <h3>{{ target === 'default' ? 'Default for every store' : 'Settings for ' + nameOf(target) }}</h3>
        <label class="check"><input type="checkbox" name="enabled" [(ngModel)]="form.enabled" /> Customers can choose a delivery time</label>
        <div class="grid">
          <label>Opens<input type="time" name="open" [(ngModel)]="form.open" step="300" /></label>
          <label>Closes<input type="time" name="close" [(ngModel)]="form.close" step="300" /></label>
          <label>Window length
            <select name="window" [(ngModel)]="form.windowMinutes">
              <option *ngFor="let m of windowChoices" [ngValue]="m">{{ m }} minutes</option>
            </select>
          </label>
          <label>Orders per window<input type="number" name="capacity" [(ngModel)]="form.capacity" min="1" max="500" step="1" /></label>
          <label>Cut-off (minutes before a window starts)<input type="number" name="cutoff" [(ngModel)]="form.cutoffMinutes" min="0" max="1440" step="5" /></label>
          <label>Days to choose from (today included)<input type="number" name="days" [(ngModel)]="form.daysAhead" min="1" max="7" step="1" /></label>
        </div>
        <label class="check"><input type="checkbox" name="asap" [(ngModel)]="form.asapAllowed" /> Also offer "as soon as possible"</label>
        <div class="buttons">
          <button class="primary" type="submit" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Save' }}</button>
          <button type="button" class="ghost" (click)="editing.set(null)">Cancel</button>
        </div>
      </form>

      <section class="card" *ngIf="rules() as r">
        <div class="row head"><span>Store</span><span>Delivery times</span><span>Action</span></div>
        <div class="row">
          <span><strong>Default</strong><small>every store without its own</small></span>
          <span>{{ describe(r.default) }}</span>
          <span class="actions"><button class="act" [disabled]="busy()" (click)="edit('default')">Edit</button></span>
        </div>
        <div class="row" *ngFor="let s of r.stores">
          <span><strong>{{ s.name }}</strong><small>{{ s.override ? 'own settings' : 'uses the default' }}</small></span>
          <span>{{ describe(s.effective) }}</span>
          <span class="actions">
            <button class="act" [disabled]="busy()" (click)="edit(s.storeId)">{{ s.override ? 'Edit' : 'Set own' }}</button>
            <button class="act ghost" *ngIf="s.override" [disabled]="busy()" (click)="useDefault(s.storeId)">Use default</button>
          </span>
        </div>
      </section>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 20px}
    .page{max-width:1000px;display:grid;gap:16px}
    .card{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:20px;display:grid;gap:12px;color:var(--on-card,#141118)}
    h3{margin:0;font:800 16px 'Space Grotesk',sans-serif}
    .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:12px}
    label{display:grid;gap:4px;font-size:11px;font-weight:700}
    label.check{display:flex;gap:8px;align-items:center}
    input,select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118}
    input[type=checkbox]{width:auto}
    .buttons{display:flex;gap:10px}
    .primary{border:0;border-radius:999px;background:#FFD400;color:#141118;padding:10px 20px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .primary:disabled{opacity:.5;cursor:default}
    .ghost{border:1px solid rgba(20,17,24,.35);border-radius:999px;background:transparent;padding:10px 20px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .row{display:grid;grid-template-columns:1.2fr 2fr 1.2fr;gap:12px;align-items:center;padding:10px 0;border-top:1px solid rgba(20,17,24,.1);font-size:12px}
    .row.head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .row small{display:block;color:var(--on-card-muted,#5E5A66);font-size:10px}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
    .act{border:0;border-radius:999px;padding:6px 12px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
    .act.ghost{background:transparent;color:#141118;border:1px solid rgba(20,17,24,.35);padding:5px 11px}
    .act:disabled{opacity:.5;cursor:default}
    .error{color:#FFB3D9;font-weight:700}
    .notice{color:#02F34C;font-weight:700}
  `],
})
export class DeliverySlotsComponent implements OnInit {
  private api = inject(ApiService);

  readonly rules = signal<Rules | null>(null);
  /** 'default' or a store id while the form is open. */
  readonly editing = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly windowChoices = [30, 45, 60, 90, 120, 180];
  form: Settings = { ...BLANK };

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
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    this.api.delete<Rules>('/admin/delivery-slots/stores/' + storeId).subscribe({
      next: r => { this.busy.set(false); this.rules.set(r.data ?? this.rules()); this.editing.set(null); this.notice.set(this.nameOf(storeId) + ' uses the default again.'); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not change.'); },
    });
  }
}
