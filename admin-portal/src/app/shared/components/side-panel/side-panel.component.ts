import { Component, EventEmitter, HostListener, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

/**
 * Add and edit forms slide in from the right while the list stays behind. Put the fields inside, and the buttons in an element with the
 * `panelActions` attribute so they stay at the bottom. Escape, the cross or the shade close it.
 */
@Component({
  selector: 'app-side-panel',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <ng-container *ngIf="open">
      <div class="shade" (click)="closed.emit()"></div>
      <aside class="panel" role="dialog" aria-modal="true" [attr.aria-label]="title">
        <header><h3>{{ title }}</h3><button type="button" class="close" aria-label="Close" (click)="closed.emit()"><app-icon name="close"></app-icon></button></header>
        <div class="body"><ng-content></ng-content></div>
        <footer><ng-content select="[panelActions]"></ng-content></footer>
      </aside>
    </ng-container>
  `,
  styles: [`
    .shade{position:fixed;inset:0;background:rgba(20,17,24,.35);z-index:200}
    .panel{position:fixed;top:0;right:0;bottom:0;width:min(460px,100%);z-index:201;background:#fff;color:var(--ink);display:flex;flex-direction:column;box-shadow:-18px 0 40px rgba(20,17,24,.18)}
    header{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:18px 20px;border-bottom:1px solid var(--line)}
    h3{margin:0;font:800 18px var(--display)}
    .close{border:0;background:var(--card);border-radius:50%;width:34px;height:34px;display:grid;place-items:center;cursor:pointer;color:var(--ink)}
    .body{flex:1;overflow-y:auto;padding:18px 20px;display:grid;gap:14px;align-content:start}
    footer{display:flex;gap:10px;justify-content:flex-end;padding:14px 20px;border-top:1px solid var(--line)}
    footer:empty{display:none}
  `],
})
export class SidePanelComponent {
  @Input() open = false;
  @Input() title = '';
  @Output() closed = new EventEmitter<void>();

  @HostListener('document:keydown.escape')
  onEscape() { if (this.open) this.closed.emit(); }
}
