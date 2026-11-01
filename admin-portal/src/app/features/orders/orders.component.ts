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

@Component({ selector: 'app-orders', standalone: true, imports: [CommonModule, FormsModule], template: `<section class="page"><h2>Orders</h2><p class="muted">Orders returned for your authorized scope</p><p *ngIf="loading()">Loading orders...</p><p *ngIf="error()" class="error">{{ error() }}</p><p *ngIf="notice()" class="notice">{{ notice() }}</p><p *ngIf="!loading() && !error() && orders().length === 0">No orders found.</p><div class="table" *ngIf="orders().length"><div class="row head"><span>Order</span><span>Customer</span><span>Store</span><span>Order date</span><span>Amount</span><span>Status</span><span>Action</span></div><div class="row" [class.fresh]="order.status === 'Pending'" *ngFor="let order of orders()"><strong>{{ order.orderNumber }}<em class="new" *ngIf="order.status === 'Pending'">NEW</em></strong><span>{{ order.customer }}<small class="note" *ngIf="order.note">Note: {{ order.note }}</small><small class="note" *ngIf="order.outOfStockChoice">If an item is out of stock: {{ choiceLabel(order.outOfStockChoice) }}</small></span><span>{{ order.store }}</span><span>{{ order.createdAt | date:'short' }}</span><span>{{ order.totalAmount | currency:'INR':'symbol':'1.2-2' }}</span><span><span class="badge" [ngClass]="statusClass(order.status)">{{ label(order.status) }}</span><em class="by" *ngIf="order.deliveryPartner">by {{ order.deliveryPartner }}</em></span><span class="actions"><button *ngFor="let step of stepsFor(order)" class="act" [class.danger]="step.danger" [disabled]="busyId() === order.id" (click)="change(order, step)">{{ step.label }}</button><button class="act" *ngIf="order.status === 'Accepted' || order.status === 'Preparing'" [disabled]="busyId() === order.id" (click)="openChange(order)">Change items</button><ng-container *ngIf="order.status === 'Ready'"><select class="pick" [attr.aria-label]="'Delivery partner for ' + order.orderNumber" [ngModel]="picked()[order.id] ?? order.deliveryPartnerId ?? ''" (ngModelChange)="pick(order.id, $event)"><option value="">Delivery partner</option><option *ngFor="let p of partnersOf(order)" [value]="p.id">{{ p.name }} ({{ p.activeDeliveries }})</option></select><button class="act give" [disabled]="busyId() === order.id || !(picked()[order.id] || order.deliveryPartnerId) || (picked()[order.id] ?? order.deliveryPartnerId) === order.deliveryPartnerId" (click)="assign(order)">{{ order.deliveryPartnerId ? 'Change' : 'Give' }}</button></ng-container></span><div class="panel" *ngIf="editing() === order.id"><strong>Items the store cannot supply</strong><p class="note">Only reduce or remove. Delivery and handling fees stay as the customer saw them. If it was paid online, the difference is refunded automatically.</p><div class="line" *ngFor="let l of lines()"><span>{{ l.productNameSnapshot }}<small *ngIf="l.variantLabel"> {{ l.variantLabel }}</small> <small>({{ l.unitPrice | currency:'INR':'symbol':'1.0-0' }} each)</small></span><span>ordered {{ l.quantity }}</span><label>now <input type="number" min="0" [max]="l.quantity" [name]="'q' + l.productId + (l.variantId || '')" [ngModel]="newQty()[key(l)]" (ngModelChange)="setQty(l, $event)" /></label><button type="button" class="act ghost" [disabled]="!!waiting()" (click)="startReplace(l)">Replace</button></div><div class="replace" *ngIf="replacing() as r"><strong>Replace {{ r.productNameSnapshot }} with</strong><div class="line"><input class="reason" placeholder="Search products by name" [ngModel]="repSearch()" (ngModelChange)="repSearch.set($event)" name="repSearch" (keyup.enter)="searchReplacement()" /><button type="button" class="act" (click)="searchReplacement()">Search</button></div><div class="chips"><button type="button" class="chip" *ngFor="let p of repResults()" (click)="pickReplacement(p)">{{ p.name }}</button></div><div class="line" *ngIf="repVariants().length"><select [ngModel]="repVariantId()" (ngModelChange)="repVariantId.set($event)" name="repVariant"><option value="">Choose a size</option><option *ngFor="let v of repVariants()" [value]="v.id" [disabled]="!v.inStock">{{ v.label }} · {{ v.price | currency:'INR':'symbol':'1.0-2' }}{{ v.inStock ? '' : ' (out of stock)' }}</option></select><label>quantity <input type="number" min="1" max="99" [ngModel]="repQty()" (ngModelChange)="repQty.set($event)" name="repQty" /></label><button type="button" class="act" [disabled]="busyId() === order.id || !repVariantId()" (click)="offerReplacement(order, r)">Offer to customer</button></div></div><p class="note" *ngIf="waiting() as w">Waiting for the customer: {{ w.newName }} {{ w.newLabel }} × {{ w.newQuantity }} at {{ w.newUnitPrice | currency:'INR':'symbol':'1.0-2' }} (until {{ w.expiresAt | date:'shortTime' }}). The item is removed if they do not answer.</p><input class="reason" maxlength="200" placeholder="Reason (optional, the customer sees it)" [ngModel]="reason()" (ngModelChange)="reason.set($event)" name="reason" /><div class="panel-actions"><button class="act" [disabled]="busyId() === order.id || !changed()" (click)="applyChange(order)">Apply change</button><button class="act ghost" (click)="editing.set(null)">Close</button></div></div></div></div></section>`, styles: [`
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
    .note{display:block;font-size:11px;font-weight:700;margin-top:2px;color:#7A4B00;white-space:normal}
    .panel{grid-column:1/-1;background:#fff;border-radius:16px;padding:12px 14px;display:grid;gap:8px;color:#141118;margin-top:6px}
    .panel .note{margin:0;font-size:11px;color:#5E5A66}
    .panel .line{display:flex;gap:12px;align-items:center;font-size:12px}
    .panel .line>span:first-child{flex:1}
    .panel input{width:70px;border:1px solid rgba(20,17,24,.25);border-radius:10px;padding:6px 8px;font:inherit}
    .panel input.reason{width:100%}
    .replace{background:#F4F1FF;border-radius:12px;padding:10px;display:grid;gap:8px}
    .replace .chips{display:flex;flex-wrap:wrap;gap:6px}
    .replace .chip{border:1px solid rgba(20,17,24,.25);border-radius:999px;padding:4px 10px;font:700 11px 'Inter',sans-serif;background:#fff;cursor:pointer}
    .panel-actions{display:flex;gap:8px}
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
  choiceLabel(choice:string){return choice==='Remove'?'remove it':choice==='Replace'?'replace with a similar item':'call the customer'}
  label(status:string){return status==='Preparing'?'Packing':status}
  readonly replacing=signal<any|null>(null); readonly repSearch=signal(''); readonly repResults=signal<{id:string;name:string}[]>([]); readonly repVariants=signal<{id:string;label:string;price:number;inStock?:boolean}[]>([]); readonly repVariantId=signal(''); readonly repQty=signal(1); readonly repProductId=signal(''); readonly waiting=signal<any|null>(null);
  startReplace(l:any){this.replacing.set(l);this.repSearch.set('');this.repResults.set([]);this.repVariants.set([]);this.repVariantId.set('');this.repQty.set(l.quantity);this.repProductId.set('')}
  searchReplacement(){this.api.get<any[]>('/admin/campaigns/products?search='+encodeURIComponent(this.repSearch().trim())).subscribe({next:r=>this.repResults.set(r.data??[]),error:e=>this.error.set(e?.error?.message??e?.message??'Could not search.')})}
  pickReplacement(p:{id:string;name:string}){
    const order=this.orders().find(o=>o.id===this.editing());
    this.repProductId.set(p.id);this.repVariantId.set('');
    this.api.get<any>('/catalog/products/'+p.id+(order?'?storeId='+order.storeId:'')).subscribe({next:r=>this.repVariants.set((r.data?.variants??[]).map((v:any)=>({id:v.id,label:v.label,price:v.price,inStock:v.inStock!==false}))),error:e=>this.error.set(e?.error?.message??e?.message??'Could not open the product.')})
  }
  offerReplacement(order:AdminOrder,line:any){
    if(this.busyId()||!this.repVariantId()) return;
    this.busyId.set(order.id);this.error.set(null);this.notice.set(null);
    this.api.post<any>('/operations/orders/'+order.id+'/replacements',{productId:line.productId,variantId:line.variantId??null,replacementProductId:this.repProductId(),replacementVariantId:this.repVariantId(),quantity:Number(this.repQty())||1}).subscribe({
      next:r=>{this.busyId.set(null);this.replacing.set(null);this.waiting.set(r.data);this.notice.set(order.orderNumber+': replacement offered. The customer has 5 minutes.')},
      error:e=>{this.busyId.set(null);this.error.set(e?.error?.message??e?.message??'Could not offer the replacement.')}
    })
  }
  readonly editing=signal<string|null>(null); readonly lines=signal<{productId:string;variantId?:string|null;productNameSnapshot:string;variantLabel?:string|null;unitPrice:number;quantity:number}[]>([]); readonly newQty=signal<Record<string,number>>({}); readonly reason=signal('');
  key(l:{productId:string;variantId?:string|null}){return l.productId+'|'+(l.variantId??'')}
  setQty(l:{productId:string;variantId?:string|null},v:number){this.newQty.set({...this.newQty(),[this.key(l)]:Number(v)})}
  changed(){return this.lines().some(l=>(this.newQty()[this.key(l)]??l.quantity)<l.quantity)}
  openChange(order:AdminOrder){
    if(this.editing()===order.id){this.editing.set(null);return}
    this.error.set(null);this.notice.set(null);
    this.api.get<any>('/orders/'+order.id).subscribe({
      next:r=>{const items=(r.data?.items??[]) as any[];this.lines.set(items);this.newQty.set(Object.fromEntries(items.map(i=>[this.key(i),i.quantity])));this.reason.set('');this.replacing.set(null);this.waiting.set(null);this.editing.set(order.id);this.api.get<any[]>('/operations/orders/'+order.id+'/replacements').subscribe({next:p=>this.waiting.set((p.data??[]).find((x:any)=>x.status==='Pending')??null),error:()=>{}})},
      error:e=>this.error.set(e?.error?.message??e?.message??'Could not open the order.')
    })
  }
  applyChange(order:AdminOrder){
    if(this.busyId()||!this.changed()) return;
    const changes=this.lines().filter(l=>(this.newQty()[this.key(l)]??l.quantity)<l.quantity).map(l=>({productId:l.productId,variantId:l.variantId??null,quantity:this.newQty()[this.key(l)]}));
    this.busyId.set(order.id);this.error.set(null);this.notice.set(null);
    this.api.post<any>('/operations/orders/'+order.id+'/change',{changes,reason:this.reason().trim()||null}).subscribe({
      next:r=>{this.busyId.set(null);this.editing.set(null);const refund=r.data?.change?.refundAmount??0;this.notice.set(order.orderNumber+': changed. New total ₹'+r.data?.newTotal+(refund>0?'; ₹'+refund+' is being refunded to the customer.':'.'));this.load()},
      error:e=>{this.busyId.set(null);this.error.set(e?.error?.message??e?.message??'Could not change the order.');this.load()}
    })
  }
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
