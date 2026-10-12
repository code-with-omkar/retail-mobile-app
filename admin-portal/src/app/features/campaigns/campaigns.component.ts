import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role, Store } from '../../core/models/domain.model';

interface Campaign {
  id: string;
  titleEn: string;
  bodyEn: string;
  titleMr?: string | null;
  bodyMr?: string | null;
  startsAt: string;
  status: 'Scheduled' | 'Sent' | 'Cancelled';
  recipientCount: number;
  createdAt: string;
  stores: { id: string; name: string }[];
  allStores: boolean;
}

/**
 * Offers and announcements for customers. An administrator picks the stores an offer is from (none picked means every store); store staff
 * announce for their own store. An offer is written once (English, and Marathi if you want), given a start time, and sent to the customers
 * of those stores who have offers switched on when that time comes. A scheduled offer can be cancelled; one already sent cannot be taken back.
 */
@Component({
  selector: 'app-campaigns',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <h2>Offers</h2>
      <p class="muted">Offers and announcements shown in the customer app. Customers can switch offers off; order and payment messages are always sent.</p>

      <form class="card" (ngSubmit)="create()" #f="ngForm">
        <h3>New offer</h3>
        <label>Title (English)<input name="titleEn" [(ngModel)]="titleEn" maxlength="160" required /></label>
        <label>Message (English)<textarea name="bodyEn" [(ngModel)]="bodyEn" maxlength="500" rows="3" required></textarea></label>
        <label>Title (Marathi, optional)<input name="titleMr" [(ngModel)]="titleMr" maxlength="160" /></label>
        <label>Message (Marathi, optional)<textarea name="bodyMr" [(ngModel)]="bodyMr" maxlength="500" rows="3"></textarea></label>
        <fieldset class="stores" *ngIf="isAdmin()">
          <legend>Stores (none ticked means every store)</legend>
          <label class="check" *ngFor="let s of stores()"><input type="checkbox" [checked]="picked().has(s.id)" (change)="toggle(s.id)" /> {{ s.name }}</label>
        </fieldset>
        <p class="muted-card" *ngIf="!isAdmin()">This offer is for your store and goes to customers who have ordered from it or shopped there.</p>
        <label>Send at (leave empty to send now)<input type="datetime-local" name="startsAt" [(ngModel)]="startsAt" /></label>
        <p *ngIf="error()" class="error">{{ error() }}</p>
        <p *ngIf="notice()" class="notice">{{ notice() }}</p>
        <button class="primary" type="submit" [disabled]="busy() || f.invalid">{{ busy() ? 'Saving…' : 'Create offer' }}</button>
      </form>

      <p *ngIf="loading()">Loading offers…</p>
      <p *ngIf="!loading() && campaigns().length === 0">No offers yet.</p>
      <div class="table" *ngIf="campaigns().length">
        <div class="row head"><span>Offer</span><span>Stores</span><span>Send at</span><span>Status</span><span>Sent to</span><span>Action</span></div>
        <div class="row" *ngFor="let c of campaigns()">
          <span><strong>{{ c.titleEn }}</strong><small>{{ c.bodyEn }}</small></span>
          <span>{{ c.allStores ? 'All stores' : storeNames(c) }}</span>
          <span>{{ c.startsAt | date:'short' }}</span>
          <span class="badge" [ngClass]="'badge-' + c.status.toLowerCase()">{{ c.status }}</span>
          <span>{{ c.status === 'Sent' ? c.recipientCount + ' customers' : '—' }}</span>
          <span><button class="act" *ngIf="c.status === 'Scheduled' && canCancel(c)" [disabled]="busy()" (click)="cancel(c)">Cancel</button></span>
        </div>
      </div>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    h3{margin:0 0 8px;color:var(--on-card,#141118);font-size:16px}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 20px}
    .page{max-width:1000px}
    .card{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:20px;display:grid;gap:12px;margin-bottom:24px;color:var(--on-card,#141118)}
    label{display:grid;gap:4px;font-size:12px;font-weight:700;color:var(--on-card-muted,#5E5A66)}
    input,textarea{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:10px 12px;font:inherit;background:#fff;color:#141118}
    .primary{justify-self:start;border:0;border-radius:999px;background:#FFD400;color:#141118;padding:10px 20px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .primary:disabled{opacity:.5;cursor:default}
    .error{color:#B00060;margin:0;font-weight:700}
    .notice{color:#04782F;margin:0;font-weight:700}
    .table{background:var(--card-bg,#EAE4FF);border-radius:24px;overflow:auto}
    .row{display:grid;grid-template-columns:2.2fr 1.4fr 1.1fr .9fr .9fr .8fr;gap:12px;padding:14px 18px;border-top:1px solid rgba(20,17,24,.12);min-width:900px;font-size:12px;color:var(--on-card-muted,#5E5A66);align-items:center}
    .row strong{display:block;color:var(--on-card,#141118)}
    .row small{display:block;margin-top:2px}
    .head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .badge{justify-self:start;padding:3px 10px;border-radius:999px;font-size:10px;font-weight:800;background:#C9C2F0;color:#141118}
    .badge-sent{background:#02F34C}
    .badge-scheduled{background:#FFD400}
    .badge-cancelled{background:#FB1A8E}
    .act{border:0;border-radius:999px;padding:6px 12px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
    .act:disabled{opacity:.5;cursor:default}
    .stores{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:10px 14px;display:grid;gap:6px;max-height:180px;overflow:auto}
    .stores legend{font-size:12px;font-weight:700;padding:0 6px}
    .check{display:flex;gap:8px;align-items:center;font-size:13px;font-weight:600;color:var(--on-card,#141118)}
    .muted-card{margin:0;font-size:12px;color:var(--on-card-muted,#5E5A66)}
  `],
})
export class CampaignsComponent implements OnInit {
  private api = inject(ApiService);
  private auth = inject(AuthService);

  readonly campaigns = signal<Campaign[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly stores = signal<Store[]>([]);
  readonly picked = signal<Set<string>>(new Set());

  /** An administrator picks stores; store staff always announce for their own store. */
  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }

  toggle(id: string) {
    const next = new Set(this.picked());
    if (!next.delete(id)) next.add(id);
    this.picked.set(next);
  }

  storeNames(c: Campaign) { return c.stores.map(s => s.name).join(', '); }

  /** Store staff can cancel only offers that are for their store alone; an administrator can cancel any. */
  canCancel(c: Campaign) { return this.isAdmin() || c.stores.length === 1; }

  titleEn = '';
  bodyEn = '';
  titleMr = '';
  bodyMr = '';
  startsAt = '';

  ngOnInit() {
    this.loading.set(true);
    this.load();
    if (this.isAdmin()) {
      this.api.get<Store[]>('/stores').subscribe({ next: r => this.stores.set((r.data ?? []).filter(s => s.isActive)), error: () => {} });
    }
  }

  load() {
    this.api.get<Campaign[]>('/admin/campaigns').subscribe({
      next: r => {
        this.campaigns.set(r.data ?? []);
        this.loading.set(false);
      },
      error: e => {
        this.error.set(e?.message ?? 'Could not load offers.');
        this.loading.set(false);
      },
    });
  }

  create() {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    const body = {
      titleEn: this.titleEn.trim(),
      bodyEn: this.bodyEn.trim(),
      titleMr: this.titleMr.trim() || null,
      bodyMr: this.bodyMr.trim() || null,
      startsAt: this.startsAt ? new Date(this.startsAt).toISOString() : null,
      storeIds: this.isAdmin() ? [...this.picked()] : [],
    };
    this.api.post<Campaign>('/admin/campaigns', body).subscribe({
      next: () => {
        this.busy.set(false);
        this.notice.set(this.startsAt ? 'Offer scheduled.' : 'Offer created. It is sent within a minute.');
        this.titleEn = this.bodyEn = this.titleMr = this.bodyMr = this.startsAt = '';
        this.picked.set(new Set());
        this.load();
      },
      error: e => {
        this.busy.set(false);
        this.error.set(e?.message ?? 'Could not create the offer.');
      },
    });
  }

  cancel(campaign: Campaign) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.api.post<Campaign>(`/admin/campaigns/${campaign.id}/cancel`).subscribe({
      next: () => {
        this.busy.set(false);
        this.notice.set('Offer cancelled.');
        this.load();
      },
      error: e => {
        this.busy.set(false);
        this.error.set(e?.message ?? 'Could not cancel the offer.');
        this.load();
      },
    });
  }
}
