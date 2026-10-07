import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApprovalRequest, ApprovalService } from '../../core/services/approval.service';
import { AuthService } from '../../core/services/auth.service';
import { StaffCategory, Store } from '../../core/models/domain.model';
import { ApiService } from '../../core/services/api.service';

@Component({
  selector: 'app-approvals',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <header class="heading">
        <span class="eyebrow">APPROVALS</span>
        <h2>{{ isManager() ? 'Approval Requests' : 'My Approval Requests' }}</h2>
        <p class="muted">{{ isManager() ? 'Pending requests for your assigned stores.' : 'Requests submitted for manager review.' }}</p>
      </header>

      <div class="stats">
        <article class="stat yellow"><span class="icon">▤</span><div><p>Total</p><strong>{{ requests().length }}</strong></div></article>
        <article class="stat green"><span class="icon">◷</span><div><p>Pending</p><strong>{{ count('Pending') }}</strong></div></article>
        <article class="stat lavender"><span class="icon">✓</span><div><p>Approved</p><strong>{{ count('Approved') }}</strong></div></article>
        <article class="stat pink"><span class="icon">×</span><div><p>Rejected</p><strong>{{ count('Rejected') }}</strong></div></article>
      </div>

      <form *ngIf="isEmployee()" class="request-form" (submit)="$event.preventDefault(); submit()">
        <h3 class="section-head"><span class="num">＋</span>New request</h3>
        <div class="fields">
          <label>Entity type<input name="entityType" [(ngModel)]="entityType" placeholder="Entity type" /></label>
          <label>Entity ID<input name="entityId" [(ngModel)]="entityId" placeholder="Existing entity ID" /></label>
          <label>Approval type<input name="approvalType" [(ngModel)]="approvalType" placeholder="Approval type" /></label>
          <label>Store<select name="storeId" [(ngModel)]="storeId"><option *ngFor="let store of stores()" [value]="store.id">{{ store.name }}</option></select></label>
        </div>
        <button type="submit" class="submit" [disabled]="submitting()">Submit request</button>
      </form>

      <p class="note" *ngIf="loading()">Loading requests...</p>
      <p class="error" *ngIf="error()">{{ error() }}</p>
      <p class="note" *ngIf="!loading() && !error() && requests().length === 0">No approval requests.</p>

      <div class="list">
        <article *ngFor="let request of requests()">
          <div class="info">
            <div class="top">
              <strong>{{ request.entityType }} · {{ request.approvalType }}</strong>
              <span class="status" [class.pending]="request.status === 'Pending'" [class.approved]="request.status === 'Approved'" [class.rejected]="request.status === 'Rejected'">{{ request.status }}</span>
            </div>
            <span class="meta">{{ request.storeName }} · requested by {{ request.requestedBy }}</span>
            <small>{{ request.createdAt | date:'short' }}</small>
            <em *ngIf="request.rejectionReason">{{ request.rejectionReason }}</em>
          </div>
          <div class="actions" *ngIf="isManager() && request.status === 'Pending'">
            <button type="button" class="approve" (click)="decide(request, true)">Approve</button>
            <button type="button" class="reject" (click)="decide(request, false)">Reject</button>
          </div>
        </article>
      </div>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#141118}
    .page{max-width:960px;margin:auto;display:grid;gap:18px}
    .eyebrow{display:inline-block;background:#02F34C;color:#141118;font-size:10px;font-weight:800;letter-spacing:1.5px;padding:4px 10px;border-radius:999px}
    h2{margin:10px 0 6px;font:800 32px 'Space Grotesk',sans-serif;letter-spacing:-1px;color:#fff}
    .muted{margin:0;color:#E9E4FF;font-size:12px}
    .stats{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}
    .stat{border-radius:24px;padding:16px 18px;display:flex;gap:12px;align-items:center}
    .stat.yellow{background:#FFF1A8}.stat.green{background:#B9FFCF}.stat.lavender{background:#EAE4FF}.stat.pink{background:#FFC6E2}
    .stat p{margin:0 0 4px;font-size:11px;font-weight:600;color:rgba(20,17,24,.7)}
    .stat strong{font:800 24px 'Space Grotesk',sans-serif;letter-spacing:-.8px}
    .icon{display:grid;place-items:center;width:36px;height:36px;border-radius:14px;font-weight:800}
    .yellow .icon{background:#FFD400}.green .icon{background:#02F34C}.lavender .icon{background:#B9A8FF}.pink .icon{background:#FB1A8E}
    .request-form{display:grid;gap:14px}
    .section-head{margin:0;display:inline-flex;align-items:center;gap:10px;justify-self:start;padding:6px 16px 6px 6px;border-radius:999px;background:#FB1A8E;font:800 13px 'Space Grotesk',sans-serif}
    .section-head .num{display:grid;place-items:center;width:24px;height:24px;border-radius:50%;background:#EAE4FF;font-size:13px}
    .fields{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}
    label{display:grid;gap:6px;color:#E9E4FF;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.5px}
    input,select{border:2px solid #FFD400;border-radius:14px;padding:11px 12px;font:12px 'Inter',sans-serif;background:#EAE4FF;color:#141118;text-transform:none;letter-spacing:0;font-weight:500}
    input::placeholder{color:#5E5A66}
    input:focus,select:focus{outline:none;box-shadow:0 0 0 3px rgba(255,212,0,.3)}
    .submit{justify-self:start;border:0;border-radius:999px;background:#FFD400;color:#141118;padding:12px 22px;font:800 12px 'Inter',sans-serif;cursor:pointer;transition:transform .15s,box-shadow .15s}
    .submit:hover:not(:disabled){transform:translateY(-1px);box-shadow:0 12px 32px rgba(20,17,24,.18)}
    .submit:disabled{opacity:.5;cursor:not-allowed}
    .list{display:grid;gap:10px}
    article{display:flex;justify-content:space-between;align-items:center;gap:18px;border-radius:24px;padding:16px 20px}
    .list article:nth-child(4n+1){background:#FFF1A8}
    .list article:nth-child(4n+2){background:#B9FFCF}
    .list article:nth-child(4n+3){background:#EAE4FF}
    .list article:nth-child(4n+4){background:#FFC6E2}
    .stats .stat{border-radius:24px}
    .info{display:grid;gap:5px;min-width:0}
    .top{display:flex;align-items:center;gap:10px;flex-wrap:wrap}
    .top strong{font-weight:800;font-size:13px}
    .meta{color:#5E5A66;font-size:12px}
    small{color:#5E5A66;font-size:11px}
    em{color:#141118;background:rgba(251,26,142,.25);border-radius:12px;padding:3px 10px;font-style:normal;font-size:11px;justify-self:start}
    .status{border-radius:999px;padding:2px 10px;font-size:10px;font-weight:800;background:#C9C2F0}
    .status.pending{background:#FFD400}.status.approved{background:#02F34C}.status.rejected{background:#FB1A8E}
    .actions{display:flex;gap:8px;flex-shrink:0}
    .actions button{border:0;border-radius:999px;padding:9px 18px;font:800 11px 'Inter',sans-serif;color:#141118;cursor:pointer;transition:transform .15s,box-shadow .15s}
    .actions button:hover{transform:translateY(-1px);box-shadow:0 8px 20px rgba(20,17,24,.2)}
    .approve{background:#02F34C}.reject{background:#FB1A8E}
    .note{margin:0;color:#E9E4FF;font-size:12px}
    .error{margin:0;background:#FFC6E2;border-radius:16px;padding:10px 14px;font-size:12px;font-weight:700}
    @media(max-width:860px){.stats{grid-template-columns:repeat(2,1fr)}.fields{grid-template-columns:1fr 1fr}}
    @media(max-width:560px){.stats,.fields{grid-template-columns:1fr}article{flex-direction:column;align-items:flex-start}}
  `],
})
export class ApprovalsComponent implements OnInit {
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
  submit(): void { if (!this.entityType.trim() || !this.entityId.trim() || !this.approvalType.trim() || !this.storeId) { this.error.set('Complete all request fields.'); return; } this.submitting.set(true); this.service.create({ entityType: this.entityType.trim(), entityId: this.entityId.trim(), approvalType: this.approvalType.trim(), storeId: this.storeId }).subscribe({ next: () => { this.entityType = ''; this.entityId = ''; this.approvalType = ''; this.submitting.set(false); this.load(); }, error: error => { this.error.set(error.message ?? 'Unable to create approval request.'); this.submitting.set(false); } }); }
}