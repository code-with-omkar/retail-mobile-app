import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { AdminOrder, OrderStatus } from '../../core/models/domain.model';

// The API sends the order status as the enum's number (0 is Pending); older answers may send the name.
const STATUS_NAMES = ['Pending','Accepted','Preparing','Ready','Completed','Rejected','Confirmed','OutForDelivery','Delivered','Cancelled','AwaitingPayment'];
function statusName(status: unknown): OrderStatus { return (typeof status === 'number' ? STATUS_NAMES[status] ?? String(status) : String(status ?? '')) as OrderStatus; }

const NEXT_STEPS: Record<string, { label: string; code: number; danger?: boolean }[]> = {
  Pending: [{ label: 'Accept', code: 1 }, { label: 'Reject', code: 5, danger: true }],
  Accepted: [{ label: 'Start packing', code: 2 }],
  Preparing: [{ label: 'Mark ready', code: 3 }],
  Ready: [{ label: 'Complete', code: 4 }],
};

@Component({ selector: 'app-orders', standalone: true, imports: [CommonModule], template: `<section class="page"><h2>Orders</h2><p class="muted">Orders returned for your authorized scope</p><p *ngIf="loading()">Loading orders...</p><p *ngIf="error()" class="error">{{ error() }}</p><p *ngIf="notice()" class="notice">{{ notice() }}</p><p *ngIf="!loading() && !error() && orders().length === 0">No orders found.</p><div class="table" *ngIf="orders().length"><div class="row head"><span>Order</span><span>Customer</span><span>Store</span><span>Order date</span><span>Amount</span><span>Status</span><span>Action</span></div><div class="row" *ngFor="let order of orders()"><strong>{{ order.orderNumber }}</strong><span>{{ order.customer }}</span><span>{{ order.store }}</span><span>{{ order.createdAt | date:'short' }}</span><span>{{ order.totalAmount | currency:'INR':'symbol':'1.2-2' }}</span><span class="badge" [ngClass]="statusClass(order.status)">{{ label(order.status) }}</span><span class="actions"><button *ngFor="let step of stepsFor(order)" class="act" [class.danger]="step.danger" [disabled]="busyId() === order.id" (click)="change(order, step)">{{ step.label }}</button></span></div></div></section>`, styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 20px}

    .page{max-width:1200px}
    .page>p:not(.muted){color:#E9E4FF;font-size:12px}
    .error{color:#FFC6E2 !important}

    .table{background:var(--card-bg,#EAE4FF);border-radius:24px;overflow:auto}
    .row{display:grid;grid-template-columns:1.4fr 1.2fr 1.2fr 1.2fr 1fr 1fr 1.6fr;gap:12px;padding:14px 18px;border-top:1px solid var(--row-line,rgba(20,17,24,.12));min-width:800px;font-size:12px;color:var(--on-card-muted,#5E5A66);align-items:center}
    .row strong{color:var(--on-card,#141118)}
    .head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .badge{justify-self:start;padding:3px 10px;border-radius:999px;font-size:10px;font-weight:800;text-transform:capitalize;background:#C9C2F0;color:#141118}
    .badge-delivered{background:#02F34C;color:#141118}
    .badge-processing{background:#FFD400;color:#141118}
    .badge-cancelled{background:#FB1A8E;color:#141118}
    .notice{color:#02F34C !important;font-weight:700}
    .actions{display:flex;gap:6px;flex-wrap:wrap}
    .act{border:0;border-radius:999px;padding:6px 12px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
    .act.danger{background:#FB1A8E;color:#141118}
    .act:disabled{opacity:.5;cursor:default}
  `] })
export class OrdersComponent implements OnInit { private api=inject(ApiService); readonly orders=signal<AdminOrder[]>([]); readonly loading=signal(false); readonly error=signal<string|null>(null); readonly notice=signal<string|null>(null); readonly busyId=signal<string|null>(null);
  label(status:string){return status==='Preparing'?'Packing':status}
  stepsFor(order:AdminOrder){return NEXT_STEPS[order.status]??[]}
  change(order:AdminOrder,step:{label:string;code:number}){
    if(this.busyId()) return;
    this.busyId.set(order.id); this.error.set(null); this.notice.set(null);
    this.api.post<unknown>('/operations/orders/'+order.id+'/status',{status:step.code}).subscribe({
      next:()=>{this.busyId.set(null);this.notice.set(order.orderNumber+': '+step.label+' done.');this.load()},
      error:e=>{this.busyId.set(null);this.error.set(e?.error?.message??e?.message??'Could not change the status.');this.load()}
    });
  }
  load(){this.api.get<AdminOrder[]>('/orders').subscribe({next:r=>{this.orders.set((r.data??[]).map(o=>({...o,status:statusName(o.status)})));this.loading.set(false)},error:e=>{this.error.set(e.message??'Unable to load orders');this.loading.set(false)}})} statusClass(status:string){const s=(status??'').toLowerCase();return s==='delivered'||s==='completed'?'badge-delivered':s==='cancelled'?'badge-cancelled':s==='processing'||s==='pending'||s==='confirmed'||s==='preparing'?'badge-processing':''} ngOnInit(){this.loading.set(true);this.load()} }
