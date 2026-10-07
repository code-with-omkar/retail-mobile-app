import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { Store } from '../../core/models/domain.model';

@Component({ selector: 'app-stores', standalone: true, imports: [CommonModule], template: `<section class="page"><h2>Stores</h2><p class="muted">Active locations in your authorized scope</p><p *ngIf="loading()">Loading stores...</p><p *ngIf="error()" class="error">{{ error() }}</p><p *ngIf="!loading() && !error() && stores().length === 0">No stores found.</p><div class="list"><article *ngFor="let store of stores; let i = index"><div class="top"><div class="avatar" [ngClass]="'t'+(i%3)">{{ store.name.charAt(0).toUpperCase() }}</div><div class="info"><strong>{{ store.name }}</strong><span>{{ store.address }}</span></div></div><span class="pill" [class.active]="store.isActive">{{ store.isActive ? 'Active' : 'Inactive' }}</span></article></div></section>`, styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 20px}

    .page{max-width:1200px}
    .page>p:not(.muted){color:#E9E4FF;font-size:12px}
    .error{color:#FFC6E2 !important}
    article:nth-child(4n+1){background:#FFF1A8}
    article:nth-child(4n+2){background:#B9FFCF}
    article:nth-child(4n+3){background:#EAE4FF}
    article:nth-child(4n+4){background:#FFC6E2}
    article:nth-child(4n+1) .avatar{background:#FFD400}
    article:nth-child(4n+2) .avatar{background:#02F34C}
    article:nth-child(4n+3) .avatar{background:#B9A8FF}
    article:nth-child(4n+4) .avatar{background:#FB1A8E}

    .list{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:10px}
    article{background:var(--card-bg,#EAE4FF);color:var(--on-card,#141118);border-radius:24px;padding:16px;display:flex;gap:14px;align-items:center}
    .avatar{width:44px;height:44px;border-radius:18px;display:grid;place-items:center;font-family:'Space Grotesk',sans-serif;font-weight:800;font-size:17px;flex-shrink:0;color:#141118;background:#FFC6E2}
    .avatar.t1{background:#FFF1A8}
    .avatar.t2{background:#B9FFCF}
    .info{min-width:0;flex:1;display:grid;gap:4px}
    .info strong{color:var(--on-card,#141118);font-size:13px;font-weight:800}
    .info span{color:var(--on-card-muted,#5E5A66);font-size:11px}

    article{flex-direction:column;align-items:flex-start;gap:12px}
    .top{display:flex;align-items:center;gap:12px;width:100%}
    .pill{padding:3px 10px;border-radius:999px;font-size:10px;font-weight:800;background:#C9C2F0;color:#141118}
    .pill.active{background:#02F34C;color:#141118}
  `] })
export class StoresComponent implements OnInit { private api=inject(ApiService); readonly stores=signal<Store[]>([]); readonly loading=signal(false); readonly error=signal<string|null>(null); ngOnInit(){this.loading.set(true);this.api.get<Store[]>('/stores').subscribe({next:r=>{this.stores.set(r.data??[]);this.loading.set(false)},error:e=>{this.error.set(e.message??'Unable to load stores');this.loading.set(false)}})} }
