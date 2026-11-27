import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { OfferCardDesign, chipText, designOf } from './offer-card-designs';

/**
 * An offer's festival card, 350 x 176: the festival's colours, countdown chip, English and Marathi name, the offer's own line, a button and a
 * motif. With an uploaded picture the picture is the card and only the chip and the button sit on it. Decorative parts are hidden from screen readers.
 */
@Component({
  selector: 'app-offer-card',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card" *ngIf="design as d" [style.background]="imageUrl ? '#141118' : d.bg" [style.color]="d.ink">
      <img *ngIf="imageUrl" class="art" [src]="imageUrl" alt="" />
      <svg *ngIf="!imageUrl" class="motif" width="104" height="104" viewBox="0 0 96 96" aria-hidden="true" [ngSwitch]="d.key">
        <g *ngSwitchCase="'diwali'"><path d="M48 16 C56 30 58 40 48 50 C38 40 40 30 48 16Z" [attr.fill]="d.m2"/><path d="M48 30 C52 38 52 44 48 48 C44 44 44 38 48 30Z" [attr.fill]="d.m3"/><path d="M12 54 Q48 92 84 54 Z" [attr.fill]="d.m1"/><ellipse cx="48" cy="80" rx="16" ry="5" [attr.fill]="d.m1"/></g>
        <g *ngSwitchCase="'ganesh'"><path d="M48 10 C62 30 76 46 76 62 A28 22 0 0 1 20 62 C20 46 34 30 48 10Z" [attr.fill]="d.m1"/><path d="M48 12 L38 82 M48 12 L58 82 M48 12 L26 76 M48 12 L70 76" [attr.stroke]="d.m2" stroke-width="3" stroke-linecap="round" fill="none"/><circle cx="48" cy="12" r="4" [attr.fill]="d.m3"/></g>
        <g *ngSwitchCase="'navratri'"><path d="M18 82 L78 14" [attr.stroke]="d.m1" stroke-width="9" stroke-linecap="round"/><path d="M18 14 L78 82" [attr.stroke]="d.m2" stroke-width="9" stroke-linecap="round"/><circle cx="30" cy="68" r="5" [attr.fill]="d.m3"/><circle cx="66" cy="68" r="5" [attr.fill]="d.m3"/><circle cx="48" cy="48" r="7" [attr.fill]="d.m3"/></g>
        <g *ngSwitchCase="'holi'"><circle cx="36" cy="38" r="24" [attr.fill]="d.m1"/><circle cx="64" cy="58" r="22" [attr.fill]="d.m2"/><circle cx="38" cy="72" r="13" [attr.fill]="d.m3"/><circle cx="74" cy="24" r="8" [attr.fill]="d.m1"/><circle cx="16" cy="66" r="5" [attr.fill]="d.m2"/></g>
        <g *ngSwitchCase="'sankranti'"><path d="M48 6 L78 38 L48 70 L18 38 Z" [attr.fill]="d.m1"/><path d="M48 6 L48 70 M18 38 L78 38" [attr.stroke]="d.m2" stroke-width="2.5"/><path d="M48 70 C38 78 58 82 48 92" [attr.stroke]="d.m3" stroke-width="3" fill="none" stroke-linecap="round"/><path d="M44 80 L38 84 M52 86 L58 82" [attr.stroke]="d.m3" stroke-width="3" stroke-linecap="round"/></g>
        <g *ngSwitchCase="'padwa'"><path d="M48 18 L48 94" [attr.stroke]="d.m2" stroke-width="4" stroke-linecap="round"/><path d="M38 26 L58 26 L66 62 L30 62 Z" [attr.fill]="d.m1"/><circle cx="48" cy="16" r="10" [attr.fill]="d.m3"/><circle cx="36" cy="30" r="3" [attr.fill]="d.m2"/><circle cx="48" cy="32" r="3" [attr.fill]="d.m2"/><circle cx="60" cy="30" r="3" [attr.fill]="d.m2"/></g>
        <g *ngSwitchCase="'eid'"><circle cx="42" cy="50" r="32" [attr.fill]="d.m1"/><circle cx="56" cy="42" r="28" [attr.fill]="d.bg"/><path d="M72 30 L75 38 L84 38 L77 43 L80 52 L72 47 L64 52 L67 43 L60 38 L69 38Z" [attr.fill]="d.m2"/></g>
        <g *ngSwitchCase="'christmas'"><path d="M48 12 L72 44 L58 44 L80 76 L16 76 L38 44 L24 44Z" [attr.fill]="d.m1"/><rect x="42" y="76" width="12" height="12" rx="2" [attr.fill]="d.m2"/><circle cx="48" cy="10" r="6" [attr.fill]="d.m3"/><circle cx="40" cy="58" r="3" [attr.fill]="d.m3"/><circle cx="58" cy="66" r="3" [attr.fill]="d.m3"/></g>
        <g *ngSwitchCase="'fresh'"><path d="M18 80 C18 42 48 14 84 14 C84 52 56 80 18 80Z" [attr.fill]="d.m1"/><path d="M24 74 L70 28" [attr.stroke]="d.bg" stroke-width="3" stroke-linecap="round"/><circle cx="80" cy="78" r="6" [attr.fill]="d.m2"/><circle cx="14" cy="22" r="4" [attr.fill]="d.m3"/></g>
        <g *ngSwitchCase="'weekend'"><rect x="14" y="24" width="68" height="58" rx="10" [attr.fill]="d.m2"/><path d="M14 44 L14 34 A10 10 0 0 1 24 24 L72 24 A10 10 0 0 1 82 34 L82 44 Z" [attr.fill]="d.m1"/><circle cx="32" cy="62" r="5" [attr.fill]="d.m3"/><circle cx="48" cy="62" r="5" [attr.fill]="d.m3"/><circle cx="64" cy="62" r="5" [attr.fill]="d.m3"/><path d="M32 16 L32 30 M64 16 L64 30" [attr.stroke]="d.m2" stroke-width="4" stroke-linecap="round"/></g>
        <g *ngSwitchCase="'bigsaver'"><circle cx="48" cy="48" r="36" [attr.fill]="d.m1"/><circle cx="36" cy="36" r="7" [attr.fill]="d.bg"/><circle cx="60" cy="60" r="7" [attr.fill]="d.bg"/><path d="M62 32 L34 64" [attr.stroke]="d.bg" stroke-width="6" stroke-linecap="round"/><circle cx="84" cy="12" r="5" [attr.fill]="d.m2"/><circle cx="10" cy="84" r="4" [attr.fill]="d.m3"/></g>
        <g *ngSwitchCase="'combo'"><rect x="10" y="30" width="44" height="44" rx="12" [attr.fill]="d.m1"/><rect x="42" y="22" width="44" height="44" rx="12" [attr.fill]="d.m2"/><path d="M48 74 L48 90 M40 82 L56 82" [attr.stroke]="d.m3" stroke-width="4" stroke-linecap="round"/></g>
        <g *ngSwitchCase="'freeship'"><rect x="6" y="34" width="52" height="34" rx="5" [attr.fill]="d.m1"/><path d="M58 44 L76 44 L90 58 L90 68 L58 68Z" [attr.fill]="d.m2"/><circle cx="24" cy="72" r="9" [attr.fill]="d.m3"/><circle cx="72" cy="72" r="9" [attr.fill]="d.m3"/><circle cx="24" cy="72" r="3.5" [attr.fill]="d.bg"/><circle cx="72" cy="72" r="3.5" [attr.fill]="d.bg"/></g>
        <g *ngSwitchCase="'newin'"><path d="M46 8 L55 34 L84 42 L55 50 L46 80 L37 50 L8 42 L37 34Z" [attr.fill]="d.m1"/><circle cx="82" cy="14" r="5" [attr.fill]="d.m2"/><circle cx="14" cy="78" r="4" [attr.fill]="d.m3"/><circle cx="80" cy="78" r="3" [attr.fill]="d.m3"/></g>
      </svg>
      <div class="text">
        <span class="chip" [style.background]="imageUrl ? 'rgba(20,17,24,0.72)' : d.chipBg" [style.color]="imageUrl ? '#FFFFFF' : d.ink">{{ chip() }}</span>
        <span class="name" *ngIf="!imageUrl">{{ d.name }} <span class="mr" lang="mr" [style.color]="d.muted">{{ d.mr }}</span></span>
        <span class="line" *ngIf="!imageUrl" [style.color]="d.muted">{{ title || 'Your offer line appears here' }}</span>
      </div>
      <span class="cta" [style.background]="d.ctaBg" [style.color]="d.ctaInk">{{ d.cta }} ›</span>
    </div>
  `,
  styles: [`
    :host{display:inline-block}
    .card{width:350px;height:176px;box-sizing:border-box;position:relative;overflow:hidden;border-radius:20px;padding:16px 18px;display:flex;flex-direction:column;justify-content:space-between;font-family:'Inter',sans-serif;font-weight:500}
    .art{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
    .motif{position:absolute;right:12px;bottom:12px}
    .text{position:relative;display:flex;flex-direction:column;gap:4px;max-width:220px}
    .chip{align-self:flex-start;font-size:10px;font-weight:800;letter-spacing:.06em;border-radius:999px;padding:3px 9px;white-space:nowrap}
    .name{font:800 24px/28px 'Space Grotesk',sans-serif;letter-spacing:-.02em;margin-top:4px}
    .mr{font-size:17px;font-weight:600;letter-spacing:0}
    .line{font-size:13px;line-height:18px;overflow:hidden;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical}
    .cta{position:relative;align-self:flex-start;height:36px;padding:0 16px;border-radius:999px;font-size:13px;font-weight:800;display:flex;align-items:center}
  `],
})
export class OfferCardComponent {
  /** The design key (diwali, ganesh ...). Nothing is drawn for an unknown key. */
  @Input() set designKey(key: string | null | undefined) { this.design = designOf(key); }
  /** The offer's own line (its English title). */
  @Input() title: string | null = null;
  /** A full link to an uploaded card picture, which replaces the built-in artwork. */
  @Input() imageUrl: string | null = null;
  /** When the offer's discount ends, for the countdown chip. */
  @Input() endsAt: string | null = null;

  design: OfferCardDesign | null = null;

  chip(): string { return chipText(this.endsAt, new Date(), this.design?.festive !== false); }
}
