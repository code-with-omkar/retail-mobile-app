import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AdminOrder, OrderStatus } from '../../core/models/domain.model';
import { CellDirective, DataColumn, DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { DEFAULT_PAGE_SIZE, PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { REJECTION_REASONS, rejectionName } from '../../shared/utils/rejections';
import { SidePanelComponent } from '../../shared/components/side-panel/side-panel.component';

// The API sends the order status as the enum's number (0 is Pending); older answers may send the name.
const STATUS_NAMES = ['Pending','Accepted','Preparing','Ready','Completed','Rejected','Confirmed','OutForDelivery','Delivered','Cancelled','AwaitingPayment'];
function statusName(status: unknown): OrderStatus { return (typeof status === 'number' ? STATUS_NAMES[status] ?? String(status) : String(status ?? '')) as OrderStatus; }

const NEXT_STEPS: Record<string, { label: string; code: number; danger?: boolean }[]> = {
  Pending: [{ label: 'Accept', code: 1 }, { label: 'Reject', code: 5, danger: true }],
  Accepted: [{ label: 'Start packing', code: 2 }],
  Preparing: [{ label: 'Mark ready', code: 3 }],
  Ready: [{ label: 'Complete', code: 4 }],
};

/** The status filter's choices, by the number the API understands. The first is the default. */
export const STATUS_FILTERS: { value: string; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: '0', label: 'New' }, { value: '1', label: 'Accepted' }, { value: '2', label: 'Packing' }, { value: '3', label: 'Ready' },
  { value: '7', label: 'Out for delivery' }, { value: '8', label: 'Delivered' }, { value: '4', label: 'Completed' },
  { value: '5', label: 'Rejected' }, { value: '9', label: 'Cancelled' },
];

interface Partner { id: string; name: string; phone?: string | null; activeDeliveries: number; }
interface OrderPage { items: AdminOrder[]; totalCount: number; page: number; pageSize: number; stores: { id: string; name: string }[]; }

/**
 * The row being worked on stays in sight: when a status filter would drop it from the page after an action, it is kept where it was,
 * showing its new status, until the filter, search or page changes.
 */
export function keepActive<T extends { id: string }>(shown: T[], fresh: T[], active: (T) | null): T[] {
  if (!active || fresh.some(o => o.id === active.id)) return fresh;
  const at = shown.findIndex(o => o.id === active.id);
  if (at < 0) return fresh;
  const out = [...fresh];
  out.splice(Math.min(at, out.length), 0, active);
  return out;
}

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule, FormsModule, DataTableComponent, CellDirective, PaginationComponent, SidePanelComponent],
  template: `
    <section class="page">
      <h2>Orders</h2>
      <p class="muted">Newest first. The order you act on is marked and stays in its row.</p>

      <div class="filters">
        <label>Search<input name="search" type="search" placeholder="Order number or customer" [ngModel]="search()" (ngModelChange)="search.set($event)" (keyup.enter)="filter()" /></label>
        <label>Status<select name="status" [ngModel]="status()" (ngModelChange)="status.set($event); filter()"><option *ngFor="let s of statusFilters" [value]="s.value">{{ s.label }}</option></select></label>
        <label *ngIf="stores().length > 1">Store<select name="storeFilter" [ngModel]="storeFilter()" (ngModelChange)="storeFilter.set($event); filter()"><option value="">All my stores</option><option *ngFor="let s of stores()" [value]="s.id">{{ s.name }}</option></select></label>
        <button type="button" class="act" (click)="filter()">Search</button>
      </div>

      <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
      <p *ngIf="notice()" class="notice" role="status">{{ notice() }}</p>

      <app-data-table [fill]="true" [columns]="columns" [rows]="orders()" [loading]="loading()" [activeId]="changedId()" [startIndex]="(page() - 1) * pageSize()" emptyText="No orders found.">
        <ng-template appCell="order" let-order>
          <strong>{{ order.orderNumber }}</strong><em class="new" *ngIf="order.status === 'Pending'">NEW</em>
          <small class="slot" *ngIf="order.deliverySlotStart">Deliver {{ windowLabel(order) }}</small>
        </ng-template>
        <ng-template appCell="customer" let-order>
          {{ order.customer }}
          <small class="note" *ngIf="order.note">Note: {{ order.note }}</small>
          <small class="note" *ngIf="order.outOfStockChoice && isOpen(order)">If an item is out of stock: {{ choiceLabel(order.outOfStockChoice) }}</small>
        </ng-template>
        <ng-template appCell="date" let-order>{{ order.createdAt | date:'short' }}</ng-template>
        <ng-template appCell="amount" let-order>{{ order.totalAmount | currency:'INR':'symbol':'1.2-2' }}</ng-template>
        <ng-template appCell="status" let-order>
          <span class="badge" [ngClass]="statusClass(order.status)">{{ label(order.status) }}</span>
          <small *ngIf="order.deliveryPartner">by {{ order.deliveryPartner }}</small>
          <small class="note" *ngIf="order.status === 'Rejected' && order.rejectionReason">{{ reasonName(order.rejectionReason) }}<ng-container *ngIf="order.rejectionNote">: {{ order.rejectionNote }}</ng-container><ng-container *ngIf="order.rejectedBy"> (by {{ order.rejectedBy }})</ng-container></small>
        </ng-template>
        <ng-template appCell="actions" let-order>
          <span class="actions">
            <button *ngFor="let step of stepsFor(order)" type="button" class="act" [class.danger]="step.danger" [disabled]="busyId() === order.id" (click)="change(order, step)">{{ step.label }}</button>
            <button type="button" class="act ghost" *ngIf="order.status === 'Accepted' || order.status === 'Preparing'" [disabled]="busyId() === order.id" (click)="openChange(order)">Change items</button>
            <ng-container *ngIf="order.status === 'Ready'">
              <select class="pick" [attr.aria-label]="'Delivery partner for ' + order.orderNumber" [ngModel]="picked()[order.id] ?? order.deliveryPartnerId ?? ''" (ngModelChange)="pick(order.id, $event)">
                <option value="">Delivery partner</option>
                <option *ngFor="let p of partnersOf(order)" [value]="p.id">{{ p.name }} ({{ p.activeDeliveries }})</option>
              </select>
              <button type="button" class="act give" [disabled]="busyId() === order.id || !(picked()[order.id] || order.deliveryPartnerId) || (picked()[order.id] ?? order.deliveryPartnerId) === order.deliveryPartnerId" (click)="assign(order)">{{ order.deliveryPartnerId ? 'Change' : 'Give' }}</button>
            </ng-container>
          </span>
        </ng-template>
      </app-data-table>

      <app-pagination [page]="page()" [pageSize]="pageSize()" [total]="total()" (pageChange)="goTo($event)" (pageSizeChange)="pageSize.set($event)"></app-pagination>

      <app-side-panel [open]="!!rejecting()" [title]="'Reject order · ' + (rejecting()?.orderNumber ?? '')" (closed)="rejecting.set(null)">
        <ng-container *ngIf="rejecting()">
          <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>
          <p class="hint">The customer is told the reason (not for Other) and is refunded if they paid. The remark stays with the team.</p>
          <label>Reason<select class="wide" name="rejectReason" [ngModel]="rejectReason()" (ngModelChange)="rejectReason.set($event)"><option *ngFor="let r of reasons" [value]="r.key">{{ r.name }}</option></select></label>
          <label>Remark {{ rejectReason() === 'Other' ? '(required)' : '(optional)' }}<input class="wide" name="rejectNote" maxlength="200" [ngModel]="rejectNote()" (ngModelChange)="rejectNote.set($event)" /></label>
        </ng-container>
        <div panelActions class="actions">
          <button type="button" class="act ghost" (click)="rejecting.set(null)">Keep the order</button>
          <button type="button" class="act danger" *ngIf="rejecting() as order" [disabled]="busyId() === order.id || (rejectReason() === 'Other' && !rejectNote().trim())" (click)="reject(order)">Reject order</button>
        </div>
      </app-side-panel>

      <app-side-panel [open]="!!editing()" [title]="'Change items · ' + (editingOrder()?.orderNumber ?? '')" (closed)="closePanel()">
        <ng-container *ngIf="editingOrder() as order">
          <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>
          <p class="notice" role="status" *ngIf="notice()">{{ notice() }}</p>
          <p class="hint">Only reduce or remove. Delivery and handling fees stay as the customer saw them. If it was paid online, the difference is refunded automatically.</p>
          <div class="line" *ngFor="let l of lines()">
            <span><strong>{{ l.productNameSnapshot }}</strong> <small *ngIf="l.variantLabel">{{ l.variantLabel }}</small><small>{{ l.unitPrice | currency:'INR':'symbol':'1.0-0' }} each · ordered {{ l.quantity }}</small></span>
            <label>Now <input type="number" min="0" [max]="l.quantity" [name]="'q' + l.productId + (l.variantId || '')" [ngModel]="newQty()[key(l)]" (ngModelChange)="setQty(l, $event)" /></label>
            <button type="button" class="act ghost" [disabled]="!!waiting()" (click)="startReplace(l)">Replace</button>
          </div>
          <div class="replace" *ngIf="replacing() as r">
            <strong>Replace {{ r.productNameSnapshot }} with</strong>
            <div class="line"><input class="wide" placeholder="Search products by name" [ngModel]="repSearch()" (ngModelChange)="repSearch.set($event)" name="repSearch" (keyup.enter)="searchReplacement()" /><button type="button" class="act" (click)="searchReplacement()">Search</button></div>
            <div class="chips"><button type="button" class="chip" *ngFor="let p of repResults()" [class.on]="repProductId() === p.id" (click)="pickReplacement(p)">{{ p.name }}</button></div>
            <div class="line" *ngIf="repVariants().length">
              <select class="wide" [ngModel]="repVariantId()" (ngModelChange)="repVariantId.set($event)" name="repVariant"><option value="">Choose a size</option><option *ngFor="let v of repVariants()" [value]="v.id" [disabled]="!v.inStock">{{ v.label }} · {{ v.price | currency:'INR':'symbol':'1.0-2' }}{{ v.inStock ? '' : ' (out of stock)' }}</option></select>
              <label>Quantity <input type="number" min="1" max="99" [ngModel]="repQty()" (ngModelChange)="repQty.set($event)" name="repQty" /></label>
              <button type="button" class="act" [disabled]="busyId() === order.id || !repVariantId()" (click)="offerReplacement(order, r)">Offer to customer</button>
            </div>
          </div>
          <p class="hint" *ngIf="waiting() as w">Waiting for the customer: {{ w.newName }} {{ w.newLabel }} × {{ w.newQuantity }} at {{ w.newUnitPrice | currency:'INR':'symbol':'1.0-2' }} (until {{ w.expiresAt | date:'shortTime' }}). The item is removed if they do not answer.</p>
          <input class="wide" maxlength="200" placeholder="Reason (optional, the customer sees it)" [ngModel]="reason()" (ngModelChange)="reason.set($event)" name="reason" />
        </ng-container>
        <div panelActions class="actions">
          <button type="button" class="act ghost" (click)="closePanel()">Close</button>
          <button type="button" class="act give" *ngIf="editingOrder() as order" [disabled]="busyId() === order.id || !changed()" (click)="applyChange(order)">Apply change</button>
        </div>
      </app-side-panel>
    </section>
  `,
  styles: [`
    :host{display:block;color:var(--ink)}
    h2{font-family:var(--display);font-size:28px;letter-spacing:-1px;margin:0 0 4px}
    .muted{color:var(--muted);font-size:12px;margin:0 0 16px}
    .page{max-width:1280px;width:100%;flex:1;min-height:0;display:flex;flex-direction:column}
    .filters{display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end;margin-bottom:12px}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;color:var(--muted)}
    input,select{border:1.5px solid #D9D7DF;border-radius:12px;padding:8px 10px;font:inherit;font-size:12px;background:#fff;color:var(--ink)}
    .filters input{min-width:240px}
    .error{color:var(--danger);font-weight:700;font-size:12px}
    .notice{color:var(--success);font-weight:700;font-size:12px}
    small{display:block;font-size:11px;color:var(--muted);margin-top:2px}
    .note{font-weight:700;color:#7A4B00}
    .slot{font-weight:800;color:#4B22C9}
    .new{font-style:normal;background:var(--yellow);color:var(--ink);border-radius:999px;padding:1px 8px;font-size:9px;font-weight:800;margin-left:6px}
    .badge{display:inline-block;padding:3px 10px;border-radius:999px;font-size:10px;font-weight:800;background:var(--line)}
    .badge-delivered{background:#E3F7EA;color:var(--success)}
    .badge-processing{background:var(--yellow)}
    .badge-cancelled{background:#FDECEA;color:#9B2414}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
    .act{border:1px solid #D5CEF5;border-radius:999px;padding:7px 13px;font:800 11px inherit;cursor:pointer;background:var(--soft);color:var(--ink)}
    .act.ghost{background:#fff;color:var(--ink);border-color:#D9D7DF}
    .act.give{background:var(--yellow);border-color:var(--yellow);color:var(--ink)}
    .act.danger{background:#FDECEA;border-color:#F5C6C0;color:#9B2414}
    .act:disabled{opacity:.45;cursor:default}
    .pick{max-width:180px}
    .hint{margin:0;font-size:12px;color:var(--muted)}
    .line{display:flex;gap:10px;align-items:center;flex-wrap:wrap;font-size:12px}
    .line>span:first-child{flex:1;min-width:160px}
    .line input[type=number]{width:76px}
    .wide{flex:1;width:100%}
    .replace{background:var(--card);border-radius:14px;padding:12px;display:grid;gap:8px}
    .chips{display:flex;flex-wrap:wrap;gap:6px}
    .chip{border:1px solid #D9D7DF;border-radius:999px;padding:5px 11px;font:700 11px inherit;background:#fff;cursor:pointer}
    .chip.on{background:var(--yellow);border-color:var(--yellow)}
  `],
})
export class OrdersComponent implements OnInit, OnDestroy {
  private api = inject(ApiService);

  readonly columns: DataColumn[] = [
    { key: 'order', label: 'Order' }, { key: 'customer', label: 'Customer' }, { key: 'store', label: 'Store' }, { key: 'date', label: 'Ordered' },
    { key: 'amount', label: 'Amount' }, { key: 'status', label: 'Status' }, { key: 'actions', label: 'Action' },
  ];
  readonly statusFilters = STATUS_FILTERS;

  readonly orders = signal<AdminOrder[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  readonly search = signal('');
  readonly status = signal('');
  readonly storeFilter = signal('');
  readonly stores = signal<{ id: string; name: string }[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly busyId = signal<string | null>(null);
  readonly partners = signal<Record<string, Partner[]>>({});
  readonly picked = signal<Record<string, string>>({});
  /** The order just acted on, marked in the list. */
  readonly changedId = signal<string | null>(null);
  /** That order as it is now, so it can stay in sight when the status filter would drop it. */
  private active: AdminOrder | null = null;
  private asked = 0;
  private timer?: ReturnType<typeof setInterval>;

  /** "Today, 2 pm - 3 pm" in India time, for a scheduled order. */
  windowLabel(order: AdminOrder): string {
    if (!order.deliverySlotStart || !order.deliverySlotEnd) return '';
    const zone = 'Asia/Kolkata';
    const day = (d: Date) => d.toLocaleDateString('en-CA', { timeZone: zone });
    const start = new Date(order.deliverySlotStart), end = new Date(order.deliverySlotEnd), now = new Date();
    const clock = (d: Date) => d.toLocaleTimeString('en-IN', { timeZone: zone, hour: 'numeric', minute: '2-digit', hour12: true }).replace(':00', '').toLowerCase();
    const tomorrow = new Date(now.getTime() + 86400000);
    const label = day(start) === day(now) ? 'Today' : day(start) === day(tomorrow) ? 'Tomorrow' : start.toLocaleDateString('en-IN', { timeZone: zone, weekday: 'short', day: 'numeric', month: 'short' });
    return label + ', ' + clock(start) + ' - ' + clock(end);
  }

  partnersOf(order: AdminOrder) { return this.partners()[order.storeId] ?? []; }
  pick(orderId: string, partnerId: string) { this.picked.set({ ...this.picked(), [orderId]: partnerId }); this.changedId.set(orderId); }

  assign(order: AdminOrder) {
    const partnerId = this.picked()[order.id];
    if (!partnerId || this.busyId()) return;
    this.start(order);
    this.api.post<unknown>('/operations/orders/' + order.id + '/assign', { partnerId }).subscribe({
      next: () => {
        this.busyId.set(null);
        const next = { ...this.picked() }; delete next[order.id]; this.picked.set(next);
        const partner = this.partnersOf(order).find(p => p.id === partnerId);
        this.active = { ...order, deliveryPartnerId: partnerId, deliveryPartner: partner?.name ?? order.deliveryPartner };
        this.notice.set(order.orderNumber + ': given to the delivery partner.');
        this.load();
      },
      error: e => { this.busyId.set(null); this.error.set(e?.error?.message ?? e?.message ?? 'Could not give the order to the delivery partner.'); this.load(); },
    });
  }

  private loadPartners(orders: AdminOrder[]) {
    for (const storeId of new Set(orders.filter(o => o.status === 'Ready').map(o => o.storeId))) {
      this.api.get<Partner[]>('/delivery/partners', { params: { storeId } }).subscribe({ next: r => this.partners.set({ ...this.partners(), [storeId]: r.data ?? [] }), error: () => {} });
    }
  }

  /** Still being handled, so what the customer wants for a missing item still matters. */
  isOpen(order: AdminOrder) { return ['Pending', 'Accepted', 'Preparing'].includes(order.status); }

  choiceLabel(choice: string) { return choice === 'Remove' ? 'remove it' : choice === 'Replace' ? 'replace with a similar item' : 'call the customer'; }
  label(status: string) { return status === 'Preparing' ? 'Packing' : status === 'OutForDelivery' ? 'Out for delivery' : status === 'Pending' ? 'New' : status; }
  statusClass(status: string) {
    const s = (status ?? '').toLowerCase();
    return s === 'delivered' || s === 'completed' ? 'badge-delivered' : s === 'cancelled' || s === 'rejected' ? 'badge-cancelled' : ['processing', 'pending', 'confirmed', 'preparing'].includes(s) ? 'badge-processing' : '';
  }

  // ---- changing items, in the side panel ----
  readonly replacing = signal<any | null>(null);
  readonly repSearch = signal('');
  readonly repResults = signal<{ id: string; name: string }[]>([]);
  readonly repVariants = signal<{ id: string; label: string; price: number; inStock?: boolean }[]>([]);
  readonly repVariantId = signal('');
  readonly repQty = signal(1);
  readonly repProductId = signal('');
  readonly waiting = signal<any | null>(null);
  readonly editing = signal<string | null>(null);
  readonly editingOrder = signal<AdminOrder | null>(null);
  readonly lines = signal<{ productId: string; variantId?: string | null; productNameSnapshot: string; variantLabel?: string | null; unitPrice: number; quantity: number }[]>([]);
  readonly newQty = signal<Record<string, number>>({});
  readonly reason = signal('');

  startReplace(l: any) { this.replacing.set(l); this.repSearch.set(''); this.repResults.set([]); this.repVariants.set([]); this.repVariantId.set(''); this.repQty.set(l.quantity); this.repProductId.set(''); }

  searchReplacement() {
    this.api.get<any[]>('/admin/campaigns/products?search=' + encodeURIComponent(this.repSearch().trim())).subscribe({ next: r => this.repResults.set(r.data ?? []), error: e => this.error.set(e?.error?.message ?? e?.message ?? 'Could not search.') });
  }

  pickReplacement(p: { id: string; name: string }) {
    const order = this.editingOrder();
    this.repProductId.set(p.id); this.repVariantId.set('');
    this.api.get<any>('/catalog/products/' + p.id + (order ? '?storeId=' + order.storeId : '')).subscribe({
      next: r => this.repVariants.set((r.data?.variants ?? []).map((v: any) => ({ id: v.id, label: v.label, price: v.price, inStock: v.inStock !== false }))),
      error: e => this.error.set(e?.error?.message ?? e?.message ?? 'Could not open the product.'),
    });
  }

  offerReplacement(order: AdminOrder, line: any) {
    if (this.busyId() || !this.repVariantId()) return;
    this.start(order);
    this.api.post<any>('/operations/orders/' + order.id + '/replacements', { productId: line.productId, variantId: line.variantId ?? null, replacementProductId: this.repProductId(), replacementVariantId: this.repVariantId(), quantity: Number(this.repQty()) || 1 }).subscribe({
      next: r => { this.busyId.set(null); this.replacing.set(null); this.waiting.set(r.data); this.notice.set(order.orderNumber + ': replacement offered. The customer has 5 minutes.'); },
      error: e => { this.busyId.set(null); this.error.set(e?.error?.message ?? e?.message ?? 'Could not offer the replacement.'); },
    });
  }

  key(l: { productId: string; variantId?: string | null }) { return l.productId + '|' + (l.variantId ?? ''); }
  setQty(l: { productId: string; variantId?: string | null }, v: number) { this.newQty.set({ ...this.newQty(), [this.key(l)]: Number(v) }); }
  changed() { return this.lines().some(l => (this.newQty()[this.key(l)] ?? l.quantity) < l.quantity); }
  closePanel() { this.editing.set(null); this.editingOrder.set(null); }

  openChange(order: AdminOrder) {
    if (this.editing() === order.id) { this.closePanel(); return; }
    this.error.set(null); this.notice.set(null); this.changedId.set(order.id);
    this.api.get<any>('/orders/' + order.id).subscribe({
      next: r => {
        const items = (r.data?.items ?? []) as any[];
        this.lines.set(items); this.newQty.set(Object.fromEntries(items.map(i => [this.key(i), i.quantity]))); this.reason.set(''); this.replacing.set(null); this.waiting.set(null);
        this.editingOrder.set(order); this.editing.set(order.id);
        this.api.get<any[]>('/operations/orders/' + order.id + '/replacements').subscribe({ next: p => this.waiting.set((p.data ?? []).find((x: any) => x.status === 'Pending') ?? null), error: () => {} });
      },
      error: e => this.error.set(e?.error?.message ?? e?.message ?? 'Could not open the order.'),
    });
  }

  applyChange(order: AdminOrder) {
    if (this.busyId() || !this.changed()) return;
    const changes = this.lines().filter(l => (this.newQty()[this.key(l)] ?? l.quantity) < l.quantity).map(l => ({ productId: l.productId, variantId: l.variantId ?? null, quantity: this.newQty()[this.key(l)] }));
    this.start(order);
    this.api.post<any>('/operations/orders/' + order.id + '/change', { changes, reason: this.reason().trim() || null }).subscribe({
      next: r => {
        this.busyId.set(null); this.closePanel();
        const refund = r.data?.change?.refundAmount ?? 0;
        this.active = { ...order, totalAmount: r.data?.newTotal ?? order.totalAmount };
        this.notice.set(order.orderNumber + ': changed. New total ₹' + r.data?.newTotal + (refund > 0 ? '; ₹' + refund + ' is being refunded to the customer.' : '.'));
        this.load();
      },
      error: e => { this.busyId.set(null); this.error.set(e?.error?.message ?? e?.message ?? 'Could not change the order.'); this.load(); },
    });
  }

  // ---- rejecting, in the side panel ----
  readonly reasons = REJECTION_REASONS;
  readonly rejecting = signal<AdminOrder | null>(null);
  readonly rejectReason = signal('OutOfStock');
  readonly rejectNote = signal('');
  reasonName(key: string) { return rejectionName(key); }

  reject(order: AdminOrder) {
    if (this.busyId() || (this.rejectReason() === 'Other' && !this.rejectNote().trim())) return;
    const reason = this.rejectReason(), note = this.rejectNote().trim() || null;
    this.start(order);
    this.api.post<unknown>('/operations/orders/' + order.id + '/status', { status: 5, reason, note }).subscribe({
      next: () => { this.busyId.set(null); this.rejecting.set(null); this.active = { ...order, status: statusName(5), rejectionReason: reason, rejectionNote: note }; this.notice.set(order.orderNumber + ': rejected.'); this.load(); },
      error: e => { this.busyId.set(null); this.error.set(e?.error?.message ?? e?.message ?? 'Could not reject the order.'); this.load(); },
    });
  }

  // ---- steps and the list ----
  stepsFor(order: AdminOrder) { return (NEXT_STEPS[order.status] ?? []).filter(s => !(order.deliveryPartnerId && s.code === 4)); }

  private start(order: AdminOrder) {
    this.busyId.set(order.id); this.changedId.set(order.id); this.active = order;
    this.error.set(null); this.notice.set(null);
  }

  change(order: AdminOrder, step: { label: string; code: number }) {
    if (this.busyId()) return;
    if (step.code === 5) {
      this.closePanel(); this.error.set(null); this.notice.set(null); this.changedId.set(order.id);
      this.rejectReason.set('OutOfStock'); this.rejectNote.set(''); this.rejecting.set(order);
      return;
    }
    this.start(order);
    this.api.post<unknown>('/operations/orders/' + order.id + '/status', { status: step.code }).subscribe({
      next: () => { this.busyId.set(null); this.active = { ...order, status: statusName(step.code) }; this.notice.set(order.orderNumber + ': ' + step.label + ' done.'); this.load(); },
      error: e => { this.busyId.set(null); this.error.set(e?.error?.message ?? e?.message ?? 'Could not change the status.'); this.load(); },
    });
  }

  /** A new search, filter or page: the list starts again and nothing is held over. */
  filter() { this.page.set(1); this.reset(); }
  goTo(page: number) { this.page.set(page); this.reset(); }
  private reset() { this.active = null; this.changedId.set(null); this.loading.set(true); this.load(); }

  load(quiet = false) {
    const params: Record<string, string | number> = { page: this.page(), pageSize: this.pageSize() };
    if (this.search().trim()) params['search'] = this.search().trim();
    if (this.status()) params['status'] = this.status();
    if (this.storeFilter()) params['storeId'] = this.storeFilter();
    const request = ++this.asked;
    this.api.get<OrderPage>('/orders/paged', { params }).subscribe({
      next: r => {
        if (request !== this.asked) return; // an older answer arriving late
        const fresh = (r.data?.items ?? []).map(o => ({ ...o, status: statusName(o.status) }));
        // The server's copy of the active order replaces the one held here as soon as it is on the page.
        this.active = fresh.find(o => o.id === this.active?.id) ?? this.active;
        this.orders.set(keepActive(this.orders(), fresh, this.active));
        this.total.set(r.data?.totalCount ?? 0);
        this.stores.set(r.data?.stores ?? []);
        this.loadPartners(this.orders());
        this.loading.set(false);
        if (quiet) this.error.set(null);
      },
      error: e => { if (request !== this.asked) return; if (!quiet) this.error.set(e.message ?? 'Unable to load orders'); this.loading.set(false); },
    });
  }

  /** New orders appear by themselves: the page is read again every 15 seconds while it is open and in front. */
  ngOnInit() { this.loading.set(true); this.load(); this.timer = setInterval(() => { if (!this.busyId() && document.visibilityState === 'visible') this.load(true); }, 15000); }
  ngOnDestroy() { if (this.timer) clearInterval(this.timer); }
}
