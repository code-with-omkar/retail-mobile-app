import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';

interface Coupon {
  id: string; code: string; description: string | null; kind: 'Percent' | 'Flat' | 'FreeDelivery'; value: number; maxDiscount: number | null; minSubtotal: number;
  startsAt: string; endsAt: string | null; totalLimit: number | null; perCustomerLimit: number | null; firstOrderOnly: boolean; isActive: boolean;
  usedCount: number; discountGiven: number; status: string;
}

/** `datetime-local` wants local time without a zone: "2026-11-01T10:30". */
const toLocal = (iso: string | null): string => {
  if (!iso) return '';
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
};

/**
 * Coupon codes customers type in the cart. The business pays for them: a store's commission and earnings are worked out before the coupon.
 * Orders that already used a coupon keep what it was then, so editing one only affects new orders.
 */
@Component({
  selector: 'app-coupons',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <h2>Coupons</h2>
      <p class="muted">Codes customers type in the cart. We pay for them, so a store's commission and earnings do not change. Editing a coupon affects new orders only.</p>
      <p *ngIf="error()" class="error">{{ error() }}</p>
      <p *ngIf="notice()" class="notice">{{ notice() }}</p>

      <form class="card" (ngSubmit)="save()" #f="ngForm">
        <h3>{{ editing() ? 'Edit coupon ' + editing()!.code : 'New coupon' }}</h3>
        <div class="grid">
          <label>Code<input name="code" [(ngModel)]="code" maxlength="20" required placeholder="e.g. WELCOME50" /></label>
          <label>Kind
            <select name="kind" [(ngModel)]="kind">
              <option value="Percent">Percent off</option>
              <option value="Flat">Flat amount off</option>
              <option value="FreeDelivery">Free delivery</option>
            </select>
          </label>
          <label *ngIf="kind !== 'FreeDelivery'">{{ kind === 'Percent' ? 'Percent (1 to 100)' : 'Amount (₹)' }}<input type="number" name="value" [(ngModel)]="value" min="0" step="0.01" /></label>
          <label *ngIf="kind === 'Percent'">Most it can take off (₹, optional)<input type="number" name="cap" [(ngModel)]="cap" min="0" step="0.01" /></label>
          <label>Minimum order (₹)<input type="number" name="min" [(ngModel)]="minSubtotal" min="0" step="0.01" /></label>
          <label>Starts<input type="datetime-local" name="startsAt" [(ngModel)]="startsAt" /></label>
          <label>Ends (optional)<input type="datetime-local" name="endsAt" [(ngModel)]="endsAt" /></label>
          <label>Uses in all (optional)<input type="number" name="totalLimit" [(ngModel)]="totalLimit" min="1" step="1" /></label>
          <label>Uses per customer (optional)<input type="number" name="perCustomer" [(ngModel)]="perCustomerLimit" min="1" step="1" /></label>
          <label class="wide">Note for the team (optional)<input name="description" [(ngModel)]="description" maxlength="200" /></label>
        </div>
        <label class="check"><input type="checkbox" name="first" [(ngModel)]="firstOrderOnly" /> Only for a customer's first order</label>
        <div class="buttons">
          <button class="primary" type="submit" [disabled]="busy() || f.invalid">{{ busy() ? 'Saving…' : (editing() ? 'Save changes' : 'Create coupon') }}</button>
          <button type="button" class="ghost" *ngIf="editing()" (click)="reset()">Cancel</button>
        </div>
      </form>

      <p *ngIf="loading()">Loading coupons…</p>
      <p *ngIf="!loading() && coupons().length === 0">No coupons yet.</p>
      <div class="table" *ngIf="coupons().length">
        <div class="row head"><span>Code</span><span>What it does</span><span>Valid</span><span>Used</span><span>Cost</span><span>Status</span><span>Action</span></div>
        <div class="row" *ngFor="let c of coupons()">
          <span><strong>{{ c.code }}</strong><small *ngIf="c.description">{{ c.description }}</small></span>
          <span>{{ describe(c) }}<small *ngIf="c.minSubtotal > 0">Minimum order {{ rupees(c.minSubtotal) }}</small><small *ngIf="c.firstOrderOnly">First order only</small><small *ngIf="c.perCustomerLimit">{{ c.perCustomerLimit }} per customer</small></span>
          <span>{{ c.startsAt | date:'mediumDate' }}<small *ngIf="c.endsAt">to {{ c.endsAt | date:'mediumDate' }}</small></span>
          <span>{{ c.usedCount }}<small *ngIf="c.totalLimit">of {{ c.totalLimit }}</small></span>
          <span>{{ rupees(c.discountGiven) }}</span>
          <span class="badge" [ngClass]="'badge-' + c.status.toLowerCase().replace(' ', '')">{{ c.status }}</span>
          <span class="actions">
            <button class="act" [disabled]="busy()" (click)="edit(c)">Edit</button>
            <button class="act" [disabled]="busy()" (click)="toggle(c)">{{ c.isActive ? 'Switch off' : 'Switch on' }}</button>
          </span>
        </div>
      </div>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 20px}
    .page{max-width:1100px;display:grid;gap:16px}
    .card{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:20px;display:grid;gap:12px;color:var(--on-card,#141118)}
    h3{margin:0;font:800 16px 'Space Grotesk',sans-serif}
    .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:12px}
    label{display:grid;gap:4px;font-size:11px;font-weight:700}
    label.wide{grid-column:1/-1}
    label.check{display:flex;gap:8px;align-items:center}
    input,select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118}
    input[type=checkbox]{width:auto}
    .buttons{display:flex;gap:10px}
    .primary{border:0;border-radius:999px;background:#FFD400;color:#141118;padding:10px 20px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .primary:disabled{opacity:.5;cursor:default}
    .ghost{border:1px solid rgba(20,17,24,.35);border-radius:999px;background:transparent;padding:10px 20px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .table{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:8px 20px;color:var(--on-card,#141118);overflow-x:auto}
    .row{display:grid;grid-template-columns:1.2fr 1.6fr 1fr .7fr .8fr .8fr 1.2fr;gap:12px;align-items:center;min-width:820px;padding:12px 0;border-top:1px solid rgba(20,17,24,.1);font-size:12px}
    .row.head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .row small{display:block;color:var(--on-card-muted,#5E5A66);font-size:10px}
    .badge{justify-self:start;border-radius:999px;padding:3px 10px;font-size:10px;font-weight:800;background:#C9C2F0}
    .badge-active{background:#02F34C}.badge-scheduled{background:#FFD400}.badge-off,.badge-expired,.badge-usedup{background:#FFC6E2}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
    .act{border:0;border-radius:999px;padding:6px 12px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
    .act:disabled{opacity:.5;cursor:default}
    .error{color:#FFB3D9;font-weight:700}
    .notice{color:#02F34C;font-weight:700}
  `],
})
export class CouponsComponent implements OnInit {
  private api = inject(ApiService);

  readonly coupons = signal<Coupon[]>([]);
  readonly editing = signal<Coupon | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  code = '';
  kind: Coupon['kind'] = 'Percent';
  value: number | null = null;
  cap: number | null = null;
  minSubtotal: number | null = 0;
  startsAt = '';
  endsAt = '';
  totalLimit: number | null = null;
  perCustomerLimit: number | null = null;
  firstOrderOnly = false;
  description = '';

  ngOnInit() { this.loading.set(true); this.load(); }

  rupees(amount: number) { return '₹' + amount.toFixed(2).replace(/\.00$/, ''); }

  describe(c: Coupon): string {
    if (c.kind === 'FreeDelivery') return 'Free delivery';
    if (c.kind === 'Flat') return this.rupees(c.value) + ' off';
    return c.value + '% off' + (c.maxDiscount ? ', up to ' + this.rupees(c.maxDiscount) : '');
  }

  load() {
    this.api.get<Coupon[]>('/admin/coupons').subscribe({
      next: r => { this.coupons.set(r.data ?? []); this.loading.set(false); },
      error: e => { this.error.set(e?.message ?? 'Could not load coupons.'); this.loading.set(false); },
    });
  }

  private body(over: Partial<{ isActive: boolean }> = {}) {
    return {
      code: this.code.trim(),
      kind: this.kind,
      value: this.kind === 'FreeDelivery' ? 0 : Number(this.value ?? 0),
      maxDiscount: this.kind === 'Percent' && this.cap ? Number(this.cap) : null,
      minSubtotal: Number(this.minSubtotal ?? 0),
      startsAt: this.startsAt ? new Date(this.startsAt).toISOString() : null,
      endsAt: this.endsAt ? new Date(this.endsAt).toISOString() : null,
      totalLimit: this.totalLimit ? Number(this.totalLimit) : null,
      perCustomerLimit: this.perCustomerLimit ? Number(this.perCustomerLimit) : null,
      firstOrderOnly: this.firstOrderOnly,
      isActive: this.editing()?.isActive ?? true,
      description: this.description.trim() || null,
      ...over,
    };
  }

  reset() {
    this.editing.set(null);
    this.code = ''; this.kind = 'Percent'; this.value = null; this.cap = null; this.minSubtotal = 0; this.startsAt = ''; this.endsAt = '';
    this.totalLimit = null; this.perCustomerLimit = null; this.firstOrderOnly = false; this.description = '';
  }

  edit(c: Coupon) {
    this.editing.set(c);
    this.code = c.code; this.kind = c.kind; this.value = c.kind === 'FreeDelivery' ? null : c.value; this.cap = c.maxDiscount; this.minSubtotal = c.minSubtotal;
    this.startsAt = toLocal(c.startsAt); this.endsAt = toLocal(c.endsAt); this.totalLimit = c.totalLimit; this.perCustomerLimit = c.perCustomerLimit;
    this.firstOrderOnly = c.firstOrderOnly; this.description = c.description ?? '';
    this.error.set(null); this.notice.set(null);
  }

  save() {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const editing = this.editing();
    const call = editing ? this.api.put<Coupon>('/admin/coupons/' + editing.id, this.body()) : this.api.post<Coupon>('/admin/coupons', this.body());
    call.subscribe({
      next: () => { this.busy.set(false); this.notice.set(editing ? 'Coupon saved. New orders use it.' : 'Coupon created.'); this.reset(); this.load(); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not save the coupon.'); },
    });
  }

  /** Switching off or on sends the coupon as it is with only that flag changed. */
  toggle(c: Coupon) {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const body = {
      code: c.code, kind: c.kind, value: c.value, maxDiscount: c.maxDiscount, minSubtotal: c.minSubtotal, startsAt: c.startsAt, endsAt: c.endsAt,
      totalLimit: c.totalLimit, perCustomerLimit: c.perCustomerLimit, firstOrderOnly: c.firstOrderOnly, isActive: !c.isActive, description: c.description,
    };
    this.api.put<Coupon>('/admin/coupons/' + c.id, body).subscribe({
      next: () => { this.busy.set(false); this.notice.set(c.code + (c.isActive ? ' switched off.' : ' switched on.')); this.load(); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not change the coupon.'); },
    });
  }
}
