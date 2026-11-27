import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { CellDirective, DataColumn, DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { DEFAULT_PAGE_SIZE, PaginationComponent, pageCount } from '../../shared/components/pagination/pagination.component';
import { SidePanelComponent } from '../../shared/components/side-panel/side-panel.component';
import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection, facets, filterState } from '../../shared/components/filter-layout/filter-state';

export interface Complaint {
  id: string; orderNumber: string; storeId: string; store: string; customer: string; kind: string; note: string | null;
  status: 'Open' | 'Approved' | 'Refused'; claimedAmount: number; approvedAmount: number | null; decisionNote: string | null;
  refundMethod: 'Online' | 'Cash' | null; cashPaidAt: string | null; createdAt: string; canDecide: boolean;
  items: { name: string; label: string | null; quantity: number; amount: number }[];
}

export const KINDS: Record<string, string> = { Missing: 'Missing item', Damaged: 'Damaged', WrongItem: 'Wrong item', Quality: 'Poor quality' };

@Component({
  selector: 'app-complaints',
  standalone: true,
  imports: [CommonModule, FormsModule, DataTableComponent, CellDirective, PaginationComponent, SidePanelComponent, FilterLayoutComponent],
  template: `
    <section class="page">
      <h2>Complaints</h2>
      <p class="muted">Problems customers report within 48 hours of delivery. A store manager or an administrator decides.</p>
      <p class="error" role="alert" *ngIf="error() && !open()">{{ error() }}</p>
      <p class="notice" role="status" *ngIf="notice()">{{ notice() }}</p>

      <app-filter-layout [fill]="true" [groups]="groups()" [selected]="filters.selected()" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <app-data-table [fill]="true" [columns]="columns" [rows]="pageRows()" [loading]="loading()" [startIndex]="(shownPage() - 1) * pageSize()" minWidth="900px" emptyText="No records found.">
          <ng-template appCell="order" let-c><strong>{{ c.orderNumber }}</strong><small>{{ c.createdAt | date:'short' }}</small></ng-template>
          <ng-template appCell="who" let-c>{{ c.customer }}<small>{{ c.store }}</small></ng-template>
          <ng-template appCell="what" let-c>{{ kind(c.kind) }}<small>{{ summary(c) }}</small></ng-template>
          <ng-template appCell="amount" let-c>{{ c.claimedAmount | currency:'INR':'symbol':'1.2-2' }}<small *ngIf="c.approvedAmount !== null">giving back {{ c.approvedAmount | currency:'INR':'symbol':'1.2-2' }}</small></ng-template>
          <ng-template appCell="status" let-c><span class="badge" [ngClass]="c.status.toLowerCase()">{{ c.status }}</span><small>{{ refundLine(c) }}</small></ng-template>
          <ng-template appCell="action" let-c><span class="actions"><button type="button" class="act" (click)="view(c)">{{ c.canDecide ? 'Decide' : 'View' }}</button><button type="button" class="act give" *ngIf="cashDue(c)" [disabled]="busy()" (click)="cashPaid(c)">Cash paid</button></span></ng-template>
        </app-data-table>
        <app-pagination [page]="shownPage()" [pageSize]="pageSize()" [total]="shown().length" (pageChange)="page.set($event)" (pageSizeChange)="pageSize.set($event)"></app-pagination>
      </app-filter-layout>

      <app-side-panel [open]="!!open()" [title]="'Complaint · ' + (open()?.orderNumber ?? '')" (closed)="open.set(null)">
        <ng-container *ngIf="open() as c">
          <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>
          <p><strong>{{ c.customer }}</strong> · {{ c.store }} · {{ kind(c.kind) }}</p>
          <p *ngIf="c.note">“{{ c.note }}”</p>
          <div class="line" *ngFor="let l of c.items"><span>{{ l.name }} <small>{{ l.label }}</small></span><span>× {{ l.quantity }}</span><strong>{{ l.amount | currency:'INR':'symbol':'1.2-2' }}</strong></div>
          <div class="line total"><span>Claimed</span><strong>{{ c.claimedAmount | currency:'INR':'symbol':'1.2-2' }}</strong></div>
          <ng-container *ngIf="c.canDecide; else decided">
            <label>Give back (₹)<input type="number" name="amount" min="0.01" step="0.01" [max]="c.claimedAmount" [ngModel]="amount()" (ngModelChange)="amount.set($event)" /></label>
            <label>Message to the customer (required to refuse)<input name="note" maxlength="500" [ngModel]="note()" (ngModelChange)="note.set($event)" /></label>
            <p class="hint">An online payment is refunded automatically. For cash on delivery, pay the customer and then press Cash paid.</p>
          </ng-container>
          <ng-template #decided>
            <p>{{ c.status }}<ng-container *ngIf="c.approvedAmount !== null">: {{ c.approvedAmount | currency:'INR':'symbol':'1.2-2' }}</ng-container></p>
            <p *ngIf="c.decisionNote">{{ c.decisionNote }}</p>
            <p>{{ refundLine(c) }}</p>
          </ng-template>
        </ng-container>
        <div panelActions class="actions">
          <button type="button" class="act ghost" (click)="open.set(null)">Close</button>
          <ng-container *ngIf="open()?.canDecide">
            <button type="button" class="act danger" [disabled]="busy() || !note().trim()" (click)="decide(false)">Refuse</button>
            <button type="button" class="primary" [disabled]="busy() || !(amount() > 0)" (click)="decide(true)">Give refund</button>
          </ng-container>
        </div>
      </app-side-panel>
    </section>
  `,
  styles: [`
    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{flex:1;min-height:0;width:100%;display:flex;flex-direction:column;gap:10px}
    .muted{margin:0;font-size:12px;color:var(--muted)}
    app-data-table small{display:block;font-size:11px;color:var(--muted);margin-top:2px}
    .badge{border-radius:999px;padding:3px 10px;font-size:10px;font-weight:800;background:#FFD400;color:#141118}
    .badge.approved{background:#E3F7EA;color:#0B7A3E}.badge.refused{background:#FDECEA;color:#9B2414}
    .actions{display:flex;gap:6px}
    .line{display:flex;justify-content:space-between;gap:10px;font-size:13px;border-top:1px solid var(--line);padding-top:8px}
    .line.total{font-weight:800}
    label{display:grid;gap:4px;font-size:11px;font-weight:700}
    input{border:1.5px solid #D9D7DF;border-radius:12px;padding:8px 10px;font:inherit;font-size:13px}
    .hint{font-size:12px;color:var(--muted)}
  `],
})
export class ComplaintsComponent implements OnInit {
  private api = inject(ApiService);

  readonly columns: DataColumn[] = [{ key: 'order', label: 'Order' }, { key: 'who', label: 'Customer and store' }, { key: 'what', label: 'Problem' }, { key: 'amount', label: 'Amount' }, { key: 'status', label: 'Status' }, { key: 'action', label: 'Action' }];
  readonly all = signal<Complaint[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly open = signal<Complaint | null>(null);
  readonly amount = signal(0);
  readonly note = signal('');
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  readonly filters = filterState('complaints');
  private readonly found = computed(() => facets<Complaint>(this.all(), this.filters.selected(), { status: c => c.status, refund: c => (this.cashDue(c) ? 'cash' : 'none'), store: c => c.store }));
  readonly shown = computed(() => this.found().shown);
  readonly shownPage = computed(() => Math.min(this.page(), pageCount(this.shown().length, this.pageSize())));
  readonly pageRows = computed(() => this.shown().slice((this.shownPage() - 1) * this.pageSize(), this.shownPage() * this.pageSize()));
  readonly groups = computed<FilterGroup[]>(() => {
    const found = this.found();
    return [
      { key: 'status', label: 'Status', options: ['Open', 'Approved', 'Refused'].map(v => ({ value: v, label: v, count: found.count('status', v) })) },
      { key: 'refund', label: 'Cash refunds', options: [{ value: 'cash', label: 'Waiting to be paid', count: found.count('refund', 'cash') }] },
      { key: 'store', label: 'Store', options: [...new Set(this.all().map(c => c.store))].sort().map(v => ({ value: v, label: v, count: found.count('store', v) })) },
    ];
  });

  ngOnInit() { this.loading.set(true); this.load(); }

  load() {
    this.api.get<Complaint[]>('/issues').subscribe({
      next: r => { this.all.set(r.data ?? []); this.loading.set(false); },
      // Nothing found is an empty list, whichever way the server says it.
      error: e => { if (e?.status === 404) this.all.set([]); else this.error.set(e?.message ?? 'Could not load complaints.'); this.loading.set(false); },
    });
  }

  onFilters(next: Selection) { this.filters.set(next); this.page.set(1); }
  clearFilters() { this.filters.clear(); this.page.set(1); }

  kind(key: string) { return KINDS[key] ?? key; }
  summary(c: Complaint) { return c.items.map(l => l.quantity + ' × ' + l.name).join(', '); }
  cashDue(c: Complaint) { return c.status === 'Approved' && c.refundMethod === 'Cash' && !c.cashPaidAt; }
  refundLine(c: Complaint) {
    if (c.status !== 'Approved') return '';
    if (c.refundMethod === 'Online') return 'Refunded to the online payment';
    return c.cashPaidAt ? 'Cash paid' : 'Cash to be paid';
  }

  view(c: Complaint) { this.error.set(null); this.notice.set(null); this.amount.set(c.claimedAmount); this.note.set(''); this.open.set(c); }

  decide(approve: boolean) {
    const c = this.open();
    if (!c || this.busy() || (!approve && !this.note().trim())) return;
    if (!window.confirm(approve ? `Give back ₹${this.amount()} for ${c.orderNumber}?` : `Refuse the complaint for ${c.orderNumber}?`)) return;
    this.send('/issues/' + c.id + '/decide', { approve, amount: approve ? Number(this.amount()) : null, note: this.note().trim() || null }, c.orderNumber + (approve ? ': refund approved.' : ': refused.'));
  }

  cashPaid(c: Complaint) {
    if (this.busy() || !window.confirm(`Have you paid ${c.customer} ₹${c.approvedAmount}?`)) return;
    this.send('/issues/' + c.id + '/cash-paid', {}, c.orderNumber + ': cash refund marked as paid.');
  }

  private send(url: string, body: unknown, done: string) {
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    this.api.post<Complaint>(url, body).subscribe({
      next: () => { this.busy.set(false); this.open.set(null); this.notice.set(done); this.load(); },
      error: e => { this.busy.set(false); this.error.set(e?.error?.message ?? e?.message ?? 'Could not save.'); },
    });
  }
}
