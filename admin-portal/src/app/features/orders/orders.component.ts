import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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

interface Partner { id: string; name: string; phone?: string | null; activeDeliveries: number; }

@Component({ selector: 'app-orders', standalone: true, imports: [CommonModule, FormsModule], template: `<section class="page"><h2>Orders</h2><p class="muted">Orders returned for your authorized scope</p><p *ngIf="loading()">Loading orders...</p><p *ngIf="error()" class="error">{{ error() }}</p><p *ngIf="notice()" class="notice">{{ notice() }}</p><p *ngIf="!loading() && !error() && orders().length === 0">No orders found.</p><div class="table" *ngIf="orders().length"><div class="row head"><span>Order</span><span>Customer</span><span>Store</span><span>Order date</span><span>Amount</span><span>Status</span><span>Action</span></div><div class="row" [class.fresh]="order.status === 'Pending'" *ngFor="let order of orders()"><strong>{{ order.orderNumber }}<em class="new" *ngIf="order.status === 'Pending'">NEW</em></strong><span>{{ order.customer }}</span><span>{{ order.store }}</span><span>{{ order.createdAt | date:'short' }}</span><span>{{ order.totalAmount | currency:'INR':'symbol':'1.2-2' }}</span><span><span class="badge" [ngClass]="statusClass(order.status)">{{ label(order.status) }}</span><em class="by" *ngIf="order.deliveryPartner">by {{ order.deliveryPartner }}</em></span><span class="actions"><button *ngFor="let step of stepsFor(order)" class="act" [class.danger]="step.danger" [disabled]="busyId() === order.id" (click)="change(order, step)">{{ step.label }}</button><ng-container *ngIf="order.status === 'Ready'"><select class="pick" [attr.aria-label]="'Delivery partner for ' + order.orderNumber" [ngModel]="picked()[order.id] ?? order.deliveryPartnerId ?? ''" (ngModelChange)="pick(order.id, $event)"><option value="">Delivery partner</option><option *ngFor="let p of partnersOf(order)" [value]="p.id">{{ p.name }} ({{ p.activeDeliveries }})</option></select><button class="act give" [disabled]="busyId() === order.id || !(picked()[order.id] || order.deliveryPartnerId) || (picked()[order.id] ?? order.deliveryPartnerId) === order.deliveryPartnerId" (click)="assign(order)">{{ order.deliveryPartnerId ? 'Change' : 'Give' }}</button></ng-container></span></div></div></section>`, styles: [`
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
    .fresh{background:rgba(255,212,0,.35)}
    .new{font-style:normal;background:#141118;color:#FFD400;border-radius:999px;padding:1px 8px;font-size:9px;font-weight:800;margin-left:6px;vertical-align:middle}
    .act.give{background:#02F34C;color:#141118}
    .by{display:block;font-style:normal;font-size:10px;margin-top:2px}
    .pick{border:1px solid rgba(20,17,24,.2);border-radius:999px;padding:5px 10px;font:700 11px 'Inter',sans-serif;background:#fff;color:#141118;max-width:170px}
  `] })
export class OrdersComponent implements OnInit, OnDestroy { private api=inject(ApiService); readonly orders=signal<AdminOrder[]>([]); readonly loading=signal(false); readonly error=signal<string|null>(null); readonly notice=signal<string|null>(null); readonly busyId=signal<string|null>(null); readonly partners=signal<Record<string,Partner[]>>({}); readonly picked=signal<Record<string,string>>({});
  partnersOf(order:AdminOrder){return this.partners()[order.storeId]??[]}
  pick(orderId:string,partnerId:string){this.picked.set({...this.picked(),[orderId]:partnerId})}
  assign(order:AdminOrder){
    const partnerId=this.picked()[order.id]; if(!partnerId||this.busyId()) return;
    this.busyId.set(order.id); this.error.set(null); this.notice.set(null);
    this.api.post<unknown>('/operations/orders/'+order.id+'/assign',{partnerId}).subscribe({
      next:()=>{this.busyId.set(null);const next={...this.picked()};delete next[order.id];this.picked.set(next);this.notice.set(order.orderNumber+': given to the delivery partner.');this.load()},
      error:e=>{this.busyId.set(null);this.error.set(e?.error?.message??e?.message??'Could not give the order to the delivery partner.');this.load()}
    });
  }
  private loadPartners(orders:AdminOrder[]){
    for(const storeId of new Set(orders.filter(o=>o.status==='Ready').map(o=>o.storeId))){
      this.api.get<Partner[]>('/delivery/partners',{params:{storeId}}).subscribe({next:r=>this.partners.set({...this.partners(),[storeId]:r.data??[]}),error:()=>{}});
    }
  }
  label(status:string){return status==='Preparing'?'Packing':status}
  stepsFor(order:AdminOrder){return (NEXT_STEPS[order.status]??[]).filter(s=>!(order.deliveryPartnerId&&s.code===4))}
  change(order:AdminOrder,step:{label:string;code:number}){
    if(this.busyId()) return;
    this.busyId.set(order.id); this.error.set(null); this.notice.set(null);
    this.api.post<unknown>('/operations/orders/'+order.id+'/status',{status:step.code}).subscribe({
      next:()=>{this.busyId.set(null);this.notice.set(order.orderNumber+': '+step.label+' done.');this.load()},
      error:e=>{this.busyId.set(null);this.error.set(e?.error?.message??e?.message??'Could not change the status.');this.load()}
    });
  }
  /** The server sends the list with new orders first, then those being handled, finished ones last. */
  load(quiet=false){this.api.get<AdminOrder[]>('/orders').subscribe({next:r=>{const list=(r.data??[]).map(o=>({...o,status:statusName(o.status)}));this.orders.set(list);this.loadPartners(list);this.loading.set(false);if(quiet)this.error.set(null)},error:e=>{if(!quiet)this.error.set(e.message??'Unable to load orders');this.loading.set(false)}})} statusClass(status:string){const s=(status??'').toLowerCase();return s==='delivered'||s==='completed'?'badge-delivered':s==='cancelled'?'badge-cancelled':s==='processing'||s==='pending'||s==='confirmed'||s==='preparing'?'badge-processing':''} private timer?:ReturnType<typeof setInterval>;
  /** New orders appear by themselves: the list is read again every 15 seconds while the page is open and in front. */
  ngOnInit(){this.loading.set(true);this.load();this.timer=setInterval(()=>{if(!this.busyId()&&document.visibilityState==='visible')this.load(true)},15000)}
  ngOnDestroy(){if(this.timer)clearInterval(this.timer)} }
