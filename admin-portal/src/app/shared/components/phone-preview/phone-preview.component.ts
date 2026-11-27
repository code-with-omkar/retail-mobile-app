import { Component, Input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { OfferCardComponent } from '../offer-card/offer-card.component';
import { designOf } from '../offer-card/offer-card-designs';
import { CARD_ANIMATIONS, animationName } from '../offer-card/card-extras';

export interface OfferPreview { titleEn: string; titleMr: string; bodyEn: string; bodyMr: string; discountPercent: number; design: string; imageUrl: string | null; endsAt: string | null; popup: boolean; ribbon?: string; animation?: string; }
export interface AdPreview { placement: string; title: string; imageUrl: string | null; }
export interface CouponPreview { code: string; kind: 'Percent' | 'Flat' | 'FreeDelivery'; value: number; cap: number; minSubtotal: number; design?: string; imageUrl?: string | null; ribbon?: string; popup?: boolean; animation?: string; }

/** What a coupon would take off an order of this size, as the app would show it. */
export function couponSaving(c: CouponPreview, subtotal: number, deliveryFee: number): number {
  if (subtotal < c.minSubtotal) return 0;
  if (c.kind === 'FreeDelivery') return deliveryFee;
  const off = c.kind === 'Flat' ? c.value : Math.floor(subtotal * c.value / 100);
  return Math.max(0, Math.min(subtotal, c.cap > 0 && c.kind === 'Percent' ? Math.min(off, c.cap) : off));
}

/** Where each ad place sits, as the customer reads the screen. */
const PLACES: Record<string, { screen: string; before: string[]; after: string[] }> = {
  home: { screen: 'Home', before: ['Search', 'Shopping from your store'], after: ['Your stores', 'Offers'] },
  home_middle: { screen: 'Home', before: ['Popular near you'], after: ['Forgot something?', 'Categories'] },
  categories: { screen: 'Categories', before: ['Your store'], after: ['Products of the category'] },
  search: { screen: 'Search', before: ['Search'], after: ['Results'] },
  cart: { screen: 'Cart', before: ['Your items'], after: ['Bill'] },
  order: { screen: 'Your order', before: ['Arrival time'], after: ['Order steps', 'Items'] },
  product: { screen: 'Product', before: ['Product picture and price', 'You might also like'], after: [] },
};

/**
 * A small phone showing how an offer, an ad or a coupon appears in the customer app. It follows the app's look (white page, rounded cards,
 * yellow actions); sizes are approximate.
 */
@Component({
  selector: 'app-phone-preview',
  standalone: true,
  imports: [CommonModule, OfferCardComponent],
  template: `
    <div class="wrap">
      <div class="top"><strong>On the customer's phone</strong>
        <span class="lang" *ngIf="offer"><button type="button" [class.on]="!marathi()" (click)="marathi.set(false)">EN</button><button type="button" [class.on]="marathi()" (click)="marathi.set(true)">मराठी</button></span>
      </div>
      <div class="phone-holder">
      <div class="phone">
        <div class="notch"></div>
        <div class="screen">
          <!-- offer -->
          <ng-container *ngIf="offer as o">
            <div class="title">Home</div>
            <div class="block">Shopping from your store</div>
            <div class="section">Offers for you</div>
            <div class="dressed"><span class="ribbon" *ngIf="o.ribbon?.trim()">{{ o.ribbon }}</span>
            <app-offer-card *ngIf="o.design; else slim" [designKey]="o.design" [title]="offerTitle(o)" [imageUrl]="o.imageUrl" [endsAt]="o.endsAt"></app-offer-card>
            <ng-template #slim>
              <div class="slim"><img *ngIf="o.imageUrl; else tagIcon" [src]="o.imageUrl" alt="" /><ng-template #tagIcon><span class="tag">%</span></ng-template>
                <span class="text"><b>{{ offerTitle(o) || 'Offer title' }}</b><small>{{ offerLine(o) }}</small></span><span class="go">›</span></div>
            </ng-template>
            </div>
            <div class="block tall">Popular near you</div>
            <div class="veil" *ngIf="o.popup">
              <div class="popup"><span class="x">×</span><span class="ribbon" *ngIf="o.ribbon?.trim()">{{ o.ribbon }}</span><img *ngIf="o.imageUrl" [src]="o.imageUrl" alt="" /><b>{{ offerTitle(o) || 'Offer title' }}</b><small>{{ offerLine(o) }}</small><span class="cta">Shop now</span></div>
            </div>
          </ng-container>

          <!-- ad -->
          <ng-container *ngIf="ad as a">
            <div class="title">{{ place(a).screen }}</div>
            <div class="block" *ngFor="let b of place(a).before">{{ b }}</div>
            <div class="ad">
              <img *ngIf="a.imageUrl; else noPicture" [src]="a.imageUrl" alt="" />
              <ng-template #noPicture><span class="no-picture">Add a picture (3:1)</span></ng-template>
              <span class="mark">Ad</span><span class="caption" *ngIf="a.title">{{ a.title }}</span>
            </div>
            <div class="block tall" *ngFor="let b of place(a).after">{{ b }}</div>
          </ng-container>

          <!-- coupon -->
          <ng-container *ngIf="coupon as c">
            <div class="title">Your cart</div>
            <div class="block">2 items · {{ rupees(sample) }}</div>
            <div class="coupon" [class.themed]="theme(c)" [style.background]="theme(c)?.bg" [style.color]="theme(c)?.ink">
              <span class="ribbon" *ngIf="c.ribbon?.trim()">{{ c.ribbon }}</span>
              <img class="thumb" *ngIf="c.imageUrl; else percent" [src]="c.imageUrl" alt="" /><ng-template #percent><span class="tag">%</span></ng-template>
              <span class="text"><b>{{ c.code || 'CODE' }}</b><small>{{ saving(c) > 0 ? 'Applied · you save ' + rupees(saving(c)) : 'Add ' + rupees(c.minSubtotal - sample) + ' more to use it' }}</small></span>
              <span class="remove">Remove</span>
            </div>
            <div class="bill">
              <div><span>Items</span><span>{{ rupees(sample) }}</span></div>
              <div><span>Delivery fee</span><span>{{ rupees(fee) }}</span></div>
              <div class="off" *ngIf="saving(c) > 0"><span>Coupon</span><span>−{{ rupees(saving(c)) }}</span></div>
              <div class="total"><span>To pay</span><span>{{ rupees(sample + fee - saving(c)) }}</span></div>
            </div>
            <div class="cta full">Proceed to checkout</div>
            <div class="veil" *ngIf="c.popup && !closed()">
              <div class="popup" [style.background]="theme(c)?.bg" [style.color]="theme(c)?.ink">
                <button type="button" class="x" aria-label="Close the pop-up preview" (click)="closed.set(true)">×</button>
                <span class="ribbon" *ngIf="c.ribbon?.trim()">{{ c.ribbon }}</span>
                <img *ngIf="c.imageUrl" [src]="c.imageUrl" alt="" />
                <b>{{ couponLine(c) }}</b><small>Use code {{ c.code || 'CODE' }}</small><span class="cta">Apply</span>
              </div>
            </div>
          </ng-container>
        </div>
      </div>
        <div class="burst" *ngIf="playing() as p" aria-hidden="true"><span *ngFor="let piece of p; let i = index" [style.left.%]="(i * 37) % 92" [style.animationDelay.ms]="(i % 6) * 110">{{ piece }}</span></div>
      </div>
      <div class="replay" *ngIf="animation() as key">
        <button type="button" (click)="play(key)">Play {{ nameOf(key) }}</button>
        <button type="button" *ngIf="coupon?.popup && closed()" (click)="closed.set(false)">Show pop-up</button>
      </div>
      <div class="replay" *ngIf="!animation() && coupon?.popup && closed()"><button type="button" (click)="closed.set(false)">Show pop-up</button></div>
      <small class="note">A preview, not the app itself.</small>
    </div>
  `,
  styles: [`
    :host{display:block}
    .wrap{display:grid;gap:8px;justify-items:center}
    .top{display:flex;gap:10px;align-items:center;justify-content:space-between;width:300px;font-size:12px}
    .lang button{border:1px solid #D9D7DF;background:#fff;font:800 10px 'Plus Jakarta Sans',sans-serif;padding:3px 9px;cursor:pointer}
    .lang button:first-child{border-radius:999px 0 0 999px}.lang button:last-child{border-radius:0 999px 999px 0}
    .lang button.on{background:var(--yellow);border-color:var(--yellow)}
    .phone{width:300px;height:580px;border:9px solid #1C1A22;border-radius:38px;background:#fff;position:relative;overflow:hidden;box-shadow:0 22px 50px rgba(40,24,90,.22)}
    .notch{position:absolute;top:6px;left:50%;transform:translateX(-50%);width:70px;height:14px;background:#1C1A22;border-radius:999px;z-index:3}
    .screen{height:100%;overflow-y:auto;padding:30px 12px 14px;display:flex;flex-direction:column;gap:9px;color:#141118;font-size:11px}
    .screen>*{flex-shrink:0}
    .title{font:800 18px var(--display)}
    .section{font:800 13px var(--display);margin-top:2px}
    .block{background:#F5F5F7;border-radius:14px;padding:12px;color:#8A8694;font-weight:700}
    .block.tall{padding:30px 12px}
    .slim,.coupon{display:flex;gap:9px;align-items:center;background:#FFF4D6;border-radius:16px;padding:10px}
    .coupon{background:#E7F6E7;border:1px dashed #14A44D}
    .slim img{width:38px;height:38px;border-radius:50%;object-fit:cover}
    .tag{width:34px;height:34px;border-radius:50%;background:#fff;display:grid;place-items:center;font-weight:800;flex-shrink:0}
    .text{flex:1;min-width:0;display:grid}
    .text b,.popup b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:12px}
    .text small,.popup small{color:#5E5A66;font-size:10px;font-weight:600}
    .go{font-size:18px}
    .remove{font-weight:800;color:#C42F1D}
    .ad{position:relative;aspect-ratio:3/1;border-radius:16px;overflow:hidden;background:#EEEAFF;display:grid;place-items:center}
    .ad img{width:100%;height:100%;object-fit:cover}
    .no-picture{color:#6F6A80;font-weight:700}
    .mark{position:absolute;top:6px;right:6px;background:#ffffffcc;border-radius:5px;padding:1px 6px;font-size:9px;font-weight:800}
    .caption{position:absolute;left:0;right:0;bottom:0;padding:14px 10px 7px;background:linear-gradient(transparent,#141118b3);color:#fff;font-weight:800;font-size:12px}
    .veil{position:absolute;inset:0;background:rgba(20,17,24,.55);display:grid;place-items:center;padding:20px;z-index:2}
    .popup{position:relative;width:100%;background:#fff;border-radius:22px;padding:18px 14px;display:grid;gap:8px;justify-items:center;text-align:center}
    .popup img{width:100%;max-height:150px;object-fit:cover;border-radius:14px}
    .popup b{max-width:100%;font-size:15px}
    .x{position:absolute;top:8px;right:10px;width:22px;height:22px;border-radius:50%;background:#F5F5F7;display:grid;place-items:center;font-size:15px}
    .cta{background:var(--yellow);border-radius:999px;padding:8px 18px;font-weight:800;text-align:center}
    .cta.full{padding:12px;margin-top:auto}
    .bill{border:1px solid #ECECF0;border-radius:16px;padding:10px 12px;display:grid;gap:6px}
    .bill div{display:flex;justify-content:space-between}
    .bill .off{color:#0B6B33;font-weight:800}
    .bill .total{border-top:1px dashed #ECECF0;padding-top:6px;font-weight:800;font-size:13px}
    .note{color:var(--muted);font-size:10px}
    .dressed,.coupon{position:relative}
    .coupon.themed{border:0}
    .coupon.themed .text small{color:inherit;opacity:.85}
    .coupon.themed .remove{color:inherit}
    .coupon.themed .tag{color:#141118}
    .coupon .thumb{width:38px;height:38px;border-radius:12px;object-fit:cover}
    .ribbon{position:absolute;top:-6px;left:10px;z-index:1;background:#C42F1D;color:#fff;border-radius:999px;padding:2px 9px;font-size:9px;font-weight:800;text-transform:uppercase;white-space:nowrap}
    button.x{border:0;cursor:pointer;color:#141118;padding:0}
    .burst{position:absolute;pointer-events:none;inset:9px;overflow:hidden;z-index:4;border-radius:30px}
    .phone-holder{position:relative}
    .burst span{position:absolute;top:-24px;font-size:18px;animation:fall 1.6s ease-in forwards}
    @keyframes fall{to{transform:translateY(600px) rotate(220deg);opacity:.1}}
    @media(prefers-reduced-motion:reduce){.burst{display:none}}
    .replay{display:flex;gap:8px}
    .replay button{border:1px solid #D9D7DF;background:#fff;border-radius:999px;padding:6px 14px;font:800 11px 'Plus Jakarta Sans',sans-serif;cursor:pointer;color:#141118}
  `],
})
export class PhonePreviewComponent {
  @Input() offer: OfferPreview | null = null;
  @Input() ad: AdPreview | null = null;
  @Input() coupon: CouponPreview | null = null;

  readonly marathi = signal(false);
  /** The sample order the coupon is shown on. */
  readonly sample = 300;
  readonly fee = 25;

  offerTitle(o: OfferPreview) { return (this.marathi() && o.titleMr.trim()) || o.titleEn.trim(); }
  offerLine(o: OfferPreview) {
    if (o.discountPercent > 0) return this.marathi() ? `${o.discountPercent}% सूट` : `${o.discountPercent}% off`;
    return (this.marathi() && o.bodyMr.trim()) || o.bodyEn.trim() || 'Your message';
  }
  place(a: AdPreview) { return PLACES[a.placement] ?? PLACES['home']; }
  saving(c: CouponPreview) { return couponSaving(c, this.sample, this.fee); }
  theme(c: CouponPreview) { return designOf(c.design); }
  couponLine(c: CouponPreview) {
    if (c.kind === 'FreeDelivery') return 'Free delivery';
    if (c.kind === 'Flat') return this.rupees(c.value) + ' off';
    return (c.value || 0) + '% off';
  }

  /** The pop-up preview was closed with its ×, as a customer would. */
  readonly closed = signal(false);
  /** The pieces falling over the phone while an animation plays. */
  readonly playing = signal<string[] | null>(null);
  animation() { return (this.offer?.animation || this.coupon?.animation) ?? ''; }
  nameOf(key: string) { return animationName(key).toLowerCase(); }
  private stop: ReturnType<typeof setTimeout> | undefined;
  play(key: string) {
    const found = CARD_ANIMATIONS.find(a => a.key === key);
    if (!found) return;
    clearTimeout(this.stop);
    this.playing.set(null);
    setTimeout(() => this.playing.set(Array.from({ length: 18 }, (_, i) => found.pieces[i % found.pieces.length])));
    this.stop = setTimeout(() => this.playing.set(null), 2400);
  }
  rupees(n: number) { return '₹' + Math.max(0, Math.round(n)); }
}
