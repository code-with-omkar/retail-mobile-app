import { Component, Input } from '@angular/core';

/** Shown in the middle of the content while data loads: three cards dealing themselves, one after the other. */
@Component({
  selector: 'app-loader',
  standalone: true,
  template: `
    <div class="cards" aria-hidden="true">
      <span><i></i><i></i><i></i></span>
      <span><i></i><i></i><i></i></span>
      <span><i></i><i></i><i></i></span>
    </div>
    <p>{{ message }}</p>
  `,
  host: { role: 'status', 'aria-live': 'polite' },
  styles: [`
    :host{flex:1;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px;min-height:240px;width:100%;grid-column:1/-1}
    .cards{position:relative;width:96px;height:76px}
    .cards span{position:absolute;left:14px;top:6px;width:68px;height:62px;border-radius:14px;background:#fff;box-shadow:0 10px 24px rgba(52,20,130,.28);display:flex;flex-direction:column;gap:7px;padding:11px 10px;box-sizing:border-box;animation:deal 1.5s cubic-bezier(.45,.05,.3,1) infinite}
    .cards span:nth-child(2){animation-delay:-.5s}
    .cards span:nth-child(3){animation-delay:-1s}
    .cards i{height:6px;border-radius:99px;background:linear-gradient(90deg,#3A0CA3,#7C45FF)}
    .cards i:nth-child(2){width:70%;background:#E4E0F2}
    .cards i:nth-child(3){width:45%;background:#FFD400}
    @keyframes deal{
      0%{transform:translate(0,0) rotate(0) scale(1);z-index:3;opacity:1}
      30%{transform:translate(46px,-8px) rotate(14deg) scale(.96);z-index:3;opacity:.95}
      60%{transform:translate(0,8px) rotate(-4deg) scale(.86);z-index:1;opacity:.75}
      100%{transform:translate(0,0) rotate(0) scale(1);z-index:3;opacity:1}
    }
    p{margin:0;font-size:12px;font-weight:800;color:var(--ink)}
    @media(prefers-reduced-motion:reduce){.cards span{animation:none}.cards span:nth-child(1){transform:rotate(-8deg)}.cards span:nth-child(3){transform:rotate(8deg)}}
  `],
})
export class LoaderComponent {
  @Input() message = 'Loading…';
}
