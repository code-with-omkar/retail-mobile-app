import { DataColumn } from '../../shared/components/data-table/data-table.component';
import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { CellDirective } from '../../shared/components/data-table/data-table.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { SidePanelComponent } from '../../shared/components/side-panel/side-panel.component';
import { FilterGroup, Selection, facets, filterState } from '../../shared/components/filter-layout/filter-state';
import { DEFAULT_PAGE_SIZE, pageCount } from '../../shared/components/pagination/pagination.component';
import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { storeChoices } from '../../shared/utils/stores';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role, Store } from '../../core/models/domain.model';

interface Rules { enabled: boolean; minBidPaise: number; minStepPaise: number; maxWeeksAhead: number; }
interface Week { weekStart: string; closesAt: string; leadingPaise: number | null; leadingIsMine: boolean; minNextBidPaise: number; }
interface Market { pincode: string; weeks: Week[]; }
interface Bid { id: string; storeId: string; storeName: string; pincode: string; weekStart: string; closesAt: string; amountPaise: number; status: 'AwaitingPayment' | 'Leading' | 'Outbid' | 'Cancelled'; paymentStatus: number; createdAt: string; }
interface StartPayment { keyId: string; providerOrderId: string; amountPaise: number; currency: string; orderNumber: string; }
declare const Razorpay: any;

/**
 * Paid placement. A store bids for first place in a pincode for a week and pays up front; the highest paid bid leads and that store is shown
 * first (marked Sponsored) to customers in the pincode. A higher paid bid replaces a lower one and the beaten bid is refunded in full.
 * Bidding for a week closes when the week starts (Monday 00:00 India time).
 */
@Component({
  selector: 'app-placement',
  standalone: true,
  imports: [CommonModule, FormsModule, FilterLayoutComponent, DataTableComponent, CellDirective, PaginationComponent, SidePanelComponent],
  template: `
    <section class="page">
      <div class="top"><div><h2>Paid placement</h2>
      <p class="muted">Bid for first place in a pincode for a week. Pay up front; the highest paid bid is shown first, marked Sponsored. If someone bids higher, your payment is refunded in full.</p></div><div class="top-actions"><button type="button" class="ghost" *ngIf="isAdmin() && rules()" (click)="panel.set('rules')">Rules</button><button type="button" class="primary" (click)="panel.set('market')">Find a pincode</button></div></div>
      <p class="error" role="alert" *ngIf="error() && !panel()">{{ error() }}</p>
      <p class="notice" role="status" *ngIf="notice() && !panel()">{{ notice() }}</p>
      <app-filter-layout [fill]="true" [groups]="groups()" [selected]="filters.selected()" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <app-data-table [fill]="true" [columns]="columns" [rows]="pageRows()" [loading]="loading()" [startIndex]="(shownPage() - 1) * pageSize()" emptyText="No bids yet.">
          <ng-template appCell="shop" let-b><strong>{{ b.storeName }}</strong></ng-template>
          <ng-template appCell="pincode" let-b>{{ b.pincode }}</ng-template>
          <ng-template appCell="week" let-b>{{ b.weekStart | date:'mediumDate' }}</ng-template>
          <ng-template appCell="amount" let-b>{{ rupees(b.amountPaise) }}</ng-template>
          <ng-template appCell="status" let-b><span class="badge" [ngClass]="'badge-' + b.status.toLowerCase()">{{ label(b) }}</span></ng-template>
          <ng-template appCell="action" let-b><span class="actions">
            <button class="act pay" *ngIf="b.status === 'AwaitingPayment'" [disabled]="busy()" (click)="pay(b)">Pay {{ rupees(b.amountPaise) }}</button>
            <button class="act" *ngIf="b.status === 'AwaitingPayment' || (isAdmin() && b.status === 'Leading')" [disabled]="busy()" (click)="cancel(b)">Cancel</button>
          </span></ng-template>
        </app-data-table>
        <app-pagination [page]="shownPage()" [pageSize]="pageSize()" [total]="shown().length" (pageChange)="page.set($event)" (pageSizeChange)="pageSize.set($event)"></app-pagination>
      </app-filter-layout>
      <app-side-panel [open]="panel() === 'rules'" title="Placement rules" (closed)="panel.set(null)">
        <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>
        <p class="notice" role="status" *ngIf="notice()">{{ notice() }}</p>
        <section class="card" *ngIf="isAdmin() && rules()">
          <h3>Rules</h3>
          <label class="check"><input type="checkbox" name="enabled" [(ngModel)]="rulesEnabled" /> Paid placement is on</label>
          <div class="fee-row"><span>Minimum bid (₹)</span><input type="number" min="1" name="minBid" [(ngModel)]="minBidRupees" /></div>
          <div class="fee-row"><span>Minimum step above the highest bid (₹)</span><input type="number" min="0" name="minStep" [(ngModel)]="minStepRupees" /></div>
          <div class="fee-row"><span>Weeks ahead a bid can be placed</span><input type="number" min="1" max="26" name="weeks" [(ngModel)]="weeksAhead" /></div>
          <button class="primary" type="button" [disabled]="busy()" (click)="saveRules()">Save rules</button>
        </section>
      </app-side-panel>
      <app-side-panel [wide]="true" [open]="panel() === 'market'" title="Find a pincode" (closed)="panel.set(null)">
        <section class="card">
          <div class="fee-row"><input name="pincode" [(ngModel)]="pincode" maxlength="6" placeholder="e.g. 410209" /><button class="primary" type="button" [disabled]="busy() || pincode.length !== 6" (click)="loadMarket()">Show weeks</button></div>
          <label *ngIf="canChooseStore()">Bid for store
            <select name="store" [(ngModel)]="storeId"><option value="">Choose a store</option><option *ngFor="let s of stores()" [value]="s.id">{{ s.name }}</option></select>
          </label>
          <p class="error" *ngIf="error()">{{ error() }}</p>
          <p class="notice" *ngIf="notice()">{{ notice() }}</p>
          <div class="table" *ngIf="market()">
            <div class="row head"><span>Week starting</span><span>Closes</span><span>Highest bid</span><span>Least to bid</span><span>Your bid (₹)</span><span></span></div>
            <div class="row" *ngFor="let w of market()!.weeks">
              <strong>{{ w.weekStart | date:'mediumDate' }}</strong>
              <span>{{ w.closesAt | date:'short' }}</span>
              <span>{{ w.leadingPaise === null ? 'No bid yet' : rupees(w.leadingPaise) }} <em *ngIf="w.leadingIsMine" class="mine">yours</em></span>
              <span>{{ rupees(w.minNextBidPaise) }}</span>
              <span><input type="number" min="1" [name]="'bid-' + w.weekStart" [ngModel]="amounts()[w.weekStart]" (ngModelChange)="setAmount(w.weekStart, $event)" [placeholder]="'' + (w.minNextBidPaise / 100)" /></span>
              <span><button class="act" type="button" [disabled]="busy() || (isAdmin() && !storeId)" (click)="bid(w)">Place bid</button></span>
            </div>
          </div>
        </section>
      </app-side-panel>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Plus Jakarta Sans',sans-serif;color:var(--ink)}
    h2{font-family:'Bricolage Grotesque',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:var(--ink)}
    h3{margin:0 0 8px;color:var(--on-card,#141118);font-size:16px}
    .list-title{color:var(--ink);margin-top:8px}
    .muted{color:var(--muted);font-size:12px;margin:0 0 20px}
    .page{max-width:1100px}
    .card{background:var(--card-bg,#F5F5F7);border-radius:24px;padding:20px;display:grid;gap:12px;margin-bottom:24px;color:var(--on-card,#141118)}
    label{display:grid;gap:4px;font-size:12px;font-weight:700;color:var(--on-card-muted,#5E5A66)}
    input,select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 12px;font:inherit;background:#fff;color:#141118;max-width:220px}
    .check{display:flex;gap:8px;align-items:center;font-size:13px;color:var(--on-card,#141118)}
    .check input{max-width:none}
    .fee-row{display:flex;gap:10px;align-items:center;font-size:13px;color:var(--on-card,#141118)}
    .fee-row>span:first-child{flex:1}
    .primary{justify-self:start;border:0;border-radius:999px;background:#FFD400;color:#141118;padding:10px 20px;font:800 12px 'Plus Jakarta Sans',sans-serif;cursor:pointer}
    .primary:disabled{opacity:.5;cursor:default}
    .error{color:#B00060;margin:0;font-weight:700}
    .notice{color:#04782F;margin:0;font-weight:700}
    .table{background:var(--card-bg,#F5F5F7);border-radius:24px;overflow:auto}
    .card .table{background:rgba(20,17,24,0.28)}
    .row{display:grid;grid-template-columns:1.5fr 1.2fr 1.3fr 1.1fr 1.3fr 1.4fr;gap:12px;padding:12px 18px;border-top:1px solid rgba(20,17,24,.12);min-width:820px;font-size:12px;color:var(--on-card-muted,#5E5A66);align-items:center}
    .row strong{color:var(--on-card,#141118)}
    .head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .mine{font-style:normal;background:#02F34C;color:#141118;border-radius:999px;padding:1px 8px;font-size:10px;font-weight:800;margin-left:4px}
    .badge{justify-self:start;padding:3px 10px;border-radius:999px;font-size:10px;font-weight:800;background:#ECECF0;color:#141118}
    .badge-leading{background:#02F34C}
    .badge-awaitingpayment{background:#FFB020}
    .badge-outbid,.badge-cancelled{background:#C42F1D}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
    .act{border:0;border-radius:999px;padding:6px 12px;font:800 11px 'Plus Jakarta Sans',sans-serif;cursor:pointer;background:#EEEAFF;color:#141118}
    .act.pay{background:#02F34C;color:#141118}
    .act:disabled{opacity:.5;cursor:default}
  
    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{flex:1;min-height:0;width:100%;display:flex!important;flex-direction:column;gap:12px}
    .top{display:flex;justify-content:space-between;align-items:flex-end;gap:12px;flex-wrap:wrap}
    .top h2{margin:0 0 4px}
    .top .muted{margin:0;font-size:12px;color:var(--muted)}
    .top-actions{display:flex;gap:10px;flex-wrap:wrap}
    app-data-table small{display:block;font-size:11px;color:var(--muted);margin-top:2px}
    app-side-panel .card{background:transparent;padding:0}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
`],
})
export class PlacementComponent implements OnInit {
  readonly columns: DataColumn[] = [{ key: 'shop', label: 'Store' }, { key: 'pincode', label: 'Pincode' }, { key: 'week', label: 'Week' }, { key: 'amount', label: 'Amount' }, { key: 'status', label: 'Status' }, { key: 'action', label: 'Action' }];
  readonly panel = signal<'rules' | 'market' | null>(null);
  readonly filters = filterState('placement');
  private readonly found = computed(() => facets<Bid>(this.bids(), this.filters.selected(), { status: b => b.status, shop: b => b.storeName }));
  readonly shown = computed(() => this.found().shown);
  readonly groups = computed<FilterGroup[]>(() => [
    { key: 'status', label: 'Status', options: [['AwaitingPayment', 'Awaiting payment'], ['Leading', 'Leading'], ['Outbid', 'Outbid'], ['Cancelled', 'Cancelled']].map(([value, label]) => ({ value, label, count: this.found().count('status', value) })) },
    { key: 'shop', label: 'Store', options: [...new Set(this.bids().map(b => b.storeName))].sort().map(v => ({ value: v, label: v, count: this.found().count('shop', v) })) },
  ]);
  onFilters(next: Selection) { this.filters.set(next); this.page.set(1); }
  clearFilters() { this.filters.clear(); this.page.set(1); }
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  readonly shownPage = computed(() => Math.min(this.page(), pageCount(this.shown().length, this.pageSize())));
  readonly pageRows = computed(() => this.shown().slice((this.shownPage() - 1) * this.pageSize(), this.shownPage() * this.pageSize()));
  private api = inject(ApiService);
  private auth = inject(AuthService);

  readonly rules = signal<Rules | null>(null);
  readonly market = signal<Market | null>(null);
  readonly bids = signal<Bid[]>([]);
  readonly stores = signal<Store[]>([]);
  readonly amounts = signal<Record<string, string>>({});
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  pincode = '';
  storeId = '';
  rulesEnabled = true;
  minBidRupees = '500';
  minStepRupees = '100';
  weeksAhead = 4;

  /** A store list to pick from: always for administrators, and for a manager who has more than one store. */
  canChooseStore(): boolean { return this.isAdmin() || this.stores().length > 1; }

  isAdmin(): boolean { return this.auth.hasRole(Role.ApplicationAdmin) || this.auth.hasRole(Role.Admin); }
  rupees(paise: number) { return '₹' + (paise / 100).toFixed(2).replace(/\.00$/, ''); }
  setAmount(week: string, value: string) { this.amounts.set({ ...this.amounts(), [week]: value }); }

  label(b: Bid): string {
    if (b.status === 'AwaitingPayment') return 'Awaiting payment';
    if (b.status === 'Leading') return 'Leading';
    const refund = b.paymentStatus === 5 ? ' (refunded)' : b.paymentStatus === 4 ? ' (refund on its way)' : b.paymentStatus === 6 ? ' (refund failed)' : '';
    return (b.status === 'Outbid' ? 'Outbid' : 'Cancelled') + refund;
  }

  ngOnInit() {
    this.loading.set(true);
    this.loadBids();
    this.api.get<Rules>('/placement/rules').subscribe({ next: r => this.showRules(r.data), error: () => {} });
    if (this.isAdmin()) this.api.get<Store[]>('/stores').subscribe({ next: r => { this.stores.set(storeChoices(r.data)); if (!this.storeId) this.storeId = this.stores()[0]?.id ?? ''; }, error: () => {} });
  }

  private showRules(r?: Rules) {
    if (!r) return;
    this.rules.set(r);
    this.rulesEnabled = r.enabled;
    this.minBidRupees = String(r.minBidPaise / 100);
    this.minStepRupees = String(r.minStepPaise / 100);
    this.weeksAhead = r.maxWeeksAhead;
  }

  loadBids() {
    this.api.get<Bid[]>('/placement/bids').subscribe({
      next: r => { this.bids.set(r.data ?? []); this.loading.set(false); },
      error: e => { this.error.set(e?.message ?? 'Could not load bids.'); this.loading.set(false); },
    });
  }

  saveRules() {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    this.api.put<Rules>('/placement/rules', {
      enabled: this.rulesEnabled,
      minBidPaise: Math.round(Number(this.minBidRupees) * 100),
      minStepPaise: Math.round(Number(this.minStepRupees) * 100),
      maxWeeksAhead: Number(this.weeksAhead),
    }).subscribe({
      next: r => { this.busy.set(false); this.notice.set('Rules saved.'); this.showRules(r.data); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not save the rules.'); },
    });
  }

  /** `keepMessages` is for the reads that follow an action, so its result message is not wiped. */
  loadMarket(keepMessages = false) {
    if (!keepMessages) { this.error.set(null); this.notice.set(null); }
    this.api.get<Market>('/placement/market', { params: { pincode: this.pincode } }).subscribe({
      next: r => { this.market.set(r.data ?? null); this.amounts.set({}); },
      error: e => { this.market.set(null); this.error.set(e?.message ?? 'Could not load the weeks.'); },
    });
  }

  bid(w: Week) {
    if (this.busy()) return;
    const rupees = Number(this.amounts()[w.weekStart] ?? w.minNextBidPaise / 100);
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    this.api.post<Bid>('/placement/bids', { pincode: this.pincode, weekStart: w.weekStart, amountPaise: Math.round(rupees * 100), storeId: this.isAdmin() ? this.storeId : null }).subscribe({
      next: r => {
        this.busy.set(false);
        this.notice.set('Bid placed. Pay to take the lead; it leads only once it is paid and the highest.');
        this.loadBids(); this.loadMarket(true);
        if (r.data) setTimeout(() => this.pay(r.data!), 0);
      },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not place the bid.'); this.loadMarket(true); },
    });
  }

  cancel(b: Bid) {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    this.api.post<Bid>('/placement/bids/' + b.id + '/cancel').subscribe({
      next: () => { this.busy.set(false); this.notice.set('Bid cancelled.'); this.loadBids(); if (this.market()) this.loadMarket(true); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not cancel the bid.'); this.loadBids(); },
    });
  }

  /** Starts paying a bid: asks the server what to open, then opens the provider's checkout. Card and UPI details never reach us. */
  pay(b: Bid) {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(null);
    this.api.post<StartPayment>('/placement/bids/' + b.id + '/payment').subscribe({
      next: r => { this.busy.set(false); if (r.data) this.openCheckout(b, r.data); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not start the payment.'); this.loadBids(); },
    });
  }

  private openCheckout(b: Bid, start: StartPayment) {
    const open = () => new Razorpay({
      key: start.keyId, amount: start.amountPaise, currency: start.currency, name: 'QuickCart', description: start.orderNumber, order_id: start.providerOrderId,
      handler: (response: any) => this.confirm(b, response),
      modal: { ondismiss: () => this.loadBids() },
    }).open();
    if (typeof Razorpay !== 'undefined') { open(); return; }
    const script = document.createElement('script');
    script.src = 'https://checkout.razorpay.com/v1/checkout.js';
    script.onload = open;
    script.onerror = () => this.error.set('Could not load the payment screen. Check your connection and try again.');
    document.head.appendChild(script);
  }

  private confirm(b: Bid, response: any) {
    this.busy.set(true);
    this.api.post<Bid>('/placement/bids/' + b.id + '/payment/confirm', {
      providerOrderId: response.razorpay_order_id,
      providerPaymentId: response.razorpay_payment_id,
      signature: response.razorpay_signature,
    }).subscribe({
      next: () => { this.busy.set(false); this.notice.set('Paid. Your store leads this pincode for that week.'); this.loadBids(); if (this.market()) this.loadMarket(true); },
      // The money is not lost: a higher bid paid first means a full refund, and a payment we could not confirm is found on its own.
      error: e => { this.busy.set(false); this.error.set((e?.message ?? 'We could not confirm the payment yet.') + ' If you were charged and did not win, it is refunded in full.'); this.loadBids(); if (this.market()) this.loadMarket(true); },
    });
  }
}
