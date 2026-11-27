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
import { ApprovalRequest, ApprovalService } from '../../core/services/approval.service';
import { AuthService } from '../../core/services/auth.service';
import { StaffCategory, Store } from '../../core/models/domain.model';
import { ApiService } from '../../core/services/api.service';

@Component({
  selector: 'app-approvals',
  standalone: true,
  imports: [CommonModule, FormsModule, FilterLayoutComponent, DataTableComponent, CellDirective, PaginationComponent, SidePanelComponent],
  template: `
    <section class="page">
      <div class="top"><div><h2>{{ isManager() ? 'Approval Requests' : 'My Approval Requests' }}</h2>
      <p class="muted">{{ isManager() ? 'Pending requests for your assigned stores.' : 'Requests submitted for manager review.' }}</p></div><div class="top-actions"><button type="button" class="primary" *ngIf="isEmployee()" (click)="creating.set(true)">＋ New request</button></div></div>
      <p class="error" role="alert" *ngIf="error() && !creating()">{{ error() }}</p>
      <app-filter-layout [fill]="true" [groups]="groups()" [selected]="filters.selected()" (selectedChange)="onFilters($event)" (cleared)="clearFilters()">
        <app-data-table [fill]="true" [columns]="columns" [rows]="pageRows()" [loading]="loading()" [startIndex]="(shownPage() - 1) * pageSize()" emptyText="No approval requests.">
          <ng-template appCell="request" let-r><strong>{{ r.entityType }} · {{ r.approvalType }}</strong><small *ngIf="r.rejectionReason">{{ r.rejectionReason }}</small></ng-template>
          <ng-template appCell="shop" let-r>{{ r.storeName }}</ng-template>
          <ng-template appCell="by" let-r>{{ r.requestedBy }}</ng-template>
          <ng-template appCell="date" let-r>{{ r.createdAt | date:'short' }}</ng-template>
          <ng-template appCell="status" let-r><span class="status" [class.pending]="r.status === 'Pending'" [class.approved]="r.status === 'Approved'" [class.rejected]="r.status === 'Rejected'">{{ r.status }}</span></ng-template>
          <ng-template appCell="action" let-r><span class="actions" *ngIf="isManager() && r.status === 'Pending'"><button type="button" class="approve" (click)="decide(r, true)">Approve</button><button type="button" class="reject" (click)="decide(r, false)">Reject</button></span></ng-template>
        </app-data-table>
        <app-pagination [page]="shownPage()" [pageSize]="pageSize()" [total]="shown().length" (pageChange)="page.set($event)" (pageSizeChange)="pageSize.set($event)"></app-pagination>
      </app-filter-layout>
      <app-side-panel [open]="creating()" title="New request" (closed)="creating.set(false)">
        <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>
      <form class="request-form" (submit)="$event.preventDefault(); submit()">
        <div class="fields">
          <label>Entity type<input name="entityType" [(ngModel)]="entityType" placeholder="Entity type" /></label>
          <label>Entity ID<input name="entityId" [(ngModel)]="entityId" placeholder="Existing entity ID" /></label>
          <label>Approval type<input name="approvalType" [(ngModel)]="approvalType" placeholder="Approval type" /></label>
          <label>Store<select name="storeId" [(ngModel)]="storeId"><option *ngFor="let store of stores()" [value]="store.id">{{ store.name }}</option></select></label>
        </div>
        <button type="submit" class="submit" [disabled]="submitting()">Submit request</button>
      </form>
      </app-side-panel>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Plus Jakarta Sans',sans-serif;color:var(--ink)}
    .page{max-width:960px;margin:auto;display:grid;gap:18px}
    .eyebrow{display:inline-block;background:#02F34C;color:#141118;font-size:10px;font-weight:800;letter-spacing:1.5px;padding:4px 10px;border-radius:999px}
    h2{margin:10px 0 6px;font:800 32px 'Bricolage Grotesque',sans-serif;letter-spacing:-1px;color:var(--ink)}
    .muted{margin:0;color:var(--muted);font-size:12px}
    .stats{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}
    .stat{border-radius:24px;padding:16px 18px;display:flex;gap:12px;align-items:center}
    .stat.yellow{background:#FFF1A8}.stat.green{background:#B9FFCF}.stat.lavender{background:#F5F5F7}.stat.pink{background:#FFC6E2}
    .stat p{margin:0 0 4px;font-size:11px;font-weight:600;color:rgba(20,17,24,.7)}
    .stat strong{font:800 24px 'Bricolage Grotesque',sans-serif;letter-spacing:-.8px}
    .icon{display:grid;place-items:center;width:36px;height:36px;border-radius:14px;font-weight:800}
    .yellow .icon{background:#FFD400}.green .icon{background:#02F34C}.lavender .icon{background:#B9A8FF}.pink .icon{background:#C42F1D}
    .request-form{display:grid;gap:14px}
    .section-head{margin:0;display:inline-flex;align-items:center;gap:10px;justify-self:start;padding:6px 16px 6px 6px;border-radius:999px;background:#C42F1D;font:800 13px 'Bricolage Grotesque',sans-serif}
    .section-head .num{display:grid;place-items:center;width:24px;height:24px;border-radius:50%;background:#F5F5F7;font-size:13px}
    .fields{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}
    label{display:grid;gap:6px;color:var(--muted);font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.5px}
    input,select{border:2px solid #FFD400;border-radius:14px;padding:11px 12px;font:12px 'Plus Jakarta Sans',sans-serif;background:#F5F5F7;color:#141118;text-transform:none;letter-spacing:0;font-weight:500}
    input::placeholder{color:#5E5A66}
    input:focus,select:focus{outline:none;box-shadow:0 0 0 3px rgba(255,212,0,.3)}
    .submit{justify-self:start;border:0;border-radius:999px;background:#FFD400;color:#141118;padding:12px 22px;font:800 12px 'Plus Jakarta Sans',sans-serif;cursor:pointer;transition:transform .15s,box-shadow .15s}
    .submit:hover:not(:disabled){transform:translateY(-1px);box-shadow:0 12px 32px rgba(20,17,24,.18)}
    .submit:disabled{opacity:.5;cursor:not-allowed}
    .list{display:grid;gap:10px}
    article{display:flex;justify-content:space-between;align-items:center;gap:18px;border-radius:24px;padding:16px 20px}
    .list article:nth-child(4n+1){background:#FFF1A8}
    .list article:nth-child(4n+2){background:#B9FFCF}
    .list article:nth-child(4n+3){background:#F5F5F7}
    .list article:nth-child(4n+4){background:#FFC6E2}
    .stats .stat{border-radius:24px}
    .info{display:grid;gap:5px;min-width:0}
    .top{display:flex;align-items:center;gap:10px;flex-wrap:wrap}
    .top strong{font-weight:800;font-size:13px}
    .meta{color:#5E5A66;font-size:12px}
    small{color:#5E5A66;font-size:11px}
    em{color:#141118;background:rgba(196,47,29,.25);border-radius:12px;padding:3px 10px;font-style:normal;font-size:11px;justify-self:start}
    .status{border-radius:999px;padding:2px 10px;font-size:10px;font-weight:800;background:#ECECF0}
    .status.pending{background:#FFD400}.status.approved{background:#02F34C}.status.rejected{background:#C42F1D}
    .actions{display:flex;gap:8px;flex-shrink:0}
    .actions button{border:0;border-radius:999px;padding:9px 18px;font:800 11px 'Plus Jakarta Sans',sans-serif;color:#141118;cursor:pointer;transition:transform .15s,box-shadow .15s}
    .actions button:hover{transform:translateY(-1px);box-shadow:0 8px 20px rgba(20,17,24,.2)}
    .approve{background:#02F34C}.reject{background:#C42F1D}
    .note{margin:0;color:var(--muted);font-size:12px}
    .error{margin:0;background:#FFC6E2;border-radius:16px;padding:10px 14px;font-size:12px;font-weight:700}
    @media(max-width:860px){.stats{grid-template-columns:repeat(2,1fr)}.fields{grid-template-columns:1fr 1fr}}
    @media(max-width:560px){.stats,.fields{grid-template-columns:1fr}article{flex-direction:column;align-items:flex-start}}
  
    :host{display:flex;flex-direction:column;color:var(--ink)}
    .page{flex:1;min-height:0;width:100%;display:flex!important;flex-direction:column;gap:12px}
    .top{display:flex;justify-content:space-between;align-items:flex-end;gap:12px;flex-wrap:wrap}
    .top h2{margin:0 0 4px}
    .top .muted{margin:0;font-size:12px;color:var(--muted)}
    .top-actions{display:flex;gap:10px;flex-wrap:wrap}
    app-data-table small{display:block;font-size:11px;color:var(--muted);margin-top:2px}
    .actions{display:flex;gap:6px}
    .status{border-radius:999px;padding:3px 10px;font-size:10px;font-weight:800;color:#141118}
    app-side-panel .request-form{background:transparent;padding:0}
`],
})
export class ApprovalsComponent implements OnInit {
  readonly columns: DataColumn[] = [{ key: 'request', label: 'Request' }, { key: 'shop', label: 'Store' }, { key: 'by', label: 'Requested by' }, { key: 'date', label: 'When' }, { key: 'status', label: 'Status' }, { key: 'action', label: 'Action' }];
  readonly creating = signal(false);
  readonly filters = filterState('approvals');
  private readonly found = computed(() => facets<ApprovalRequest>(this.requests(), this.filters.selected(), { status: r => String(r.status), shop: r => r.storeName }));
  readonly shown = computed(() => this.found().shown);
  readonly groups = computed<FilterGroup[]>(() => [
    { key: 'status', label: 'Status', options: ['Pending', 'Approved', 'Rejected'].map(v => ({ value: v, label: v, count: this.found().count('status', v) })) },
    { key: 'shop', label: 'Store', options: [...new Set(this.requests().map(r => r.storeName))].sort().map(v => ({ value: v, label: v, count: this.found().count('shop', v) })) },
  ]);
  onFilters(next: Selection) { this.filters.set(next); this.page.set(1); }
  clearFilters() { this.filters.clear(); this.page.set(1); }
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  readonly shownPage = computed(() => Math.min(this.page(), pageCount(this.shown().length, this.pageSize())));
  readonly pageRows = computed(() => this.shown().slice((this.shownPage() - 1) * this.pageSize(), this.shownPage() * this.pageSize()));
  private readonly service = inject(ApprovalService);
  private readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  readonly requests = signal<ApprovalRequest[]>([]); readonly stores = signal<Store[]>([]); readonly loading = signal(false); readonly error = signal<string|null>(null); readonly submitting = signal(false);
  entityType = ''; entityId = ''; approvalType = ''; storeId = '';
  count(status: string): number { return this.requests().filter(request => request.status === status).length; }
  isManager(): boolean { return this.auth.userContext()?.staffCategory === StaffCategory.StoreManager; }
  ngOnInit(): void { this.load(); if (this.isEmployee()) this.api.get<Store[]>('/stores').subscribe(response => { this.stores.set(response.data ?? []); this.storeId = this.auth.getStoreId() ?? this.stores()[0]?.id ?? ''; }); }
  isEmployee(): boolean { return this.auth.userContext()?.staffCategory === StaffCategory.StoreEmployee; }
  load(): void { this.loading.set(true); const request = this.isManager() ? this.service.pending() : this.service.mine(); request.subscribe({ next: response => { this.requests.set(response.data ?? []); this.loading.set(false); }, error: error => { this.error.set(error.message ?? 'Unable to load approval requests.'); this.loading.set(false); } }); }
  decide(request: ApprovalRequest, approve: boolean): void { const reason = approve ? '' : window.prompt('Rejection reason')?.trim() ?? ''; if (!approve && !reason) return; const operation = approve ? this.service.approve(request.id) : this.service.reject(request.id, reason); operation.subscribe({ next: () => this.load(), error: error => this.error.set(error.message ?? 'Unable to update approval request.') }); }
  submit(): void { if (!this.entityType.trim() || !this.entityId.trim() || !this.approvalType.trim() || !this.storeId) { this.error.set('Complete all request fields.'); return; } this.submitting.set(true); this.service.create({ entityType: this.entityType.trim(), entityId: this.entityId.trim(), approvalType: this.approvalType.trim(), storeId: this.storeId }).subscribe({ next: () => { this.entityType = ''; this.entityId = ''; this.approvalType = ''; this.submitting.set(false); this.creating.set(false); this.load(); }, error: error => { this.error.set(error.message ?? 'Unable to create approval request.'); this.submitting.set(false); } }); }
}