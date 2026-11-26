import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TIPS } from './tips';

/** A small "?" that explains something in English and Marathi when pointed at or focused. `<app-tip key="orders"></app-tip>` */
@Component({
  selector: 'app-tip',
  standalone: true,
  imports: [CommonModule],
  template: `
    <span class="wrap" *ngIf="tip as t">
      <button type="button" class="mark" [attr.aria-label]="t.en + ' ' + t.mr">?</button>
      <span class="bubble" role="tooltip"><span lang="en">{{ t.en }}</span><span lang="mr">{{ t.mr }}</span></span>
    </span>
  `,
  styles: [`
    :host{display:inline-flex;vertical-align:middle}
    .wrap{position:relative;display:inline-flex}
    .mark{width:20px;height:20px;border-radius:50%;border:1px solid #C9C0F0;background:var(--soft);color:var(--ink);font:800 11px 'Plus Jakarta Sans',sans-serif;cursor:help;padding:0}
    .bubble{position:absolute;left:0;top:26px;z-index:300;display:grid;gap:6px;width:260px;background:#fff;color:var(--ink);border:1px solid var(--line);border-radius:14px;padding:11px 13px;box-shadow:0 14px 34px rgba(40,24,90,.18);font:600 12px/1.5 'Plus Jakarta Sans',sans-serif;letter-spacing:0;text-transform:none;text-align:left;opacity:0;visibility:hidden;transform:translateY(-4px);transition:opacity .15s ease,transform .15s ease}
    .bubble span[lang=mr]{color:var(--muted);border-top:1px dashed var(--line);padding-top:6px}
    .wrap:hover .bubble,.wrap:focus-within .bubble{opacity:1;visibility:visible;transform:none}
  `],
})
export class HelpTipComponent {
  @Input() key = '';
  get tip() { return TIPS[this.key] ?? null; }
}
