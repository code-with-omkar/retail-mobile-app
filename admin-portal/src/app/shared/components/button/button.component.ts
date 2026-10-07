import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
type ButtonSize = 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-button',
  standalone: true,
  imports: [CommonModule],
  template: `
    <button
      [class]="getClasses()"
      [disabled]="disabled || loading"
      (click)="onClick.emit()"
      [type]="type"
      [attr.aria-label]="ariaLabel"
    >
      <span *ngIf="!loading">{{ label }}</span>
      <span *ngIf="loading" class="loading-spinner"></span>
    </button>
  `,
  styles: [`
    button{border:1px solid transparent;border-radius:999px;padding:12px 18px;font-family:'Inter',sans-serif;font-size:12px;font-weight:800;cursor:pointer;transition:all .2s ease;display:inline-flex;align-items:center;justify-content:center;gap:8px}
    button:disabled{opacity:.5;cursor:not-allowed}
    button.primary{background:#FFD400;color:#141118}
    button.primary:hover:not(:disabled){box-shadow:0 12px 32px rgba(20,17,24,.18),0 6px 20px rgba(255,212,0,.35);transform:translateY(-1px)}
    button.secondary{background:#EAE4FF;color:#141118}
    button.secondary:hover:not(:disabled){background:#FFF1A8}
    button.danger{background:#FB1A8E;color:#141118}
    button.danger:hover:not(:disabled){box-shadow:0 12px 32px rgba(251,26,142,.35);transform:translateY(-1px)}
    button.ghost{background:transparent;color:#fff}
    button.ghost:hover:not(:disabled){background:rgba(255,255,255,.12)}
    button.sm{padding:8px 14px;font-size:10px}
    button.lg{padding:14px 22px;font-size:14px}
    .loading-spinner{display:inline-block;width:12px;height:12px;border:2px solid rgba(20,17,24,.25);border-top:2px solid currentColor;border-radius:50%;animation:spin .6s linear infinite}
    @keyframes spin{to{transform:rotate(360deg)}}
  `],
})
export class ButtonComponent {
  @Input() label: string = 'Button';
  @Input() variant: ButtonVariant = 'primary';
  @Input() size: ButtonSize = 'md';
  @Input() type: 'button' | 'submit' | 'reset' = 'button';
  @Input() disabled = false;
  @Input() loading = false;
  @Input() ariaLabel?: string;
  @Output() onClick = new EventEmitter<void>();

  getClasses(): string {
    return `${this.variant} ${this.size}`;
  }
}
