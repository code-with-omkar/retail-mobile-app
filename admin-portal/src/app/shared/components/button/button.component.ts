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
    button {
      border: none;
      border-radius: 7px;
      padding: 12px 16px;
      font-size: 12px;
      font-weight: 700;
      cursor: pointer;
      transition: all 0.2s ease;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
    }

    /* Primary variant */
    button.primary {
      background: #172523;
      color: #e7f5b7;
    }
    button.primary:hover:not(:disabled) {
      background: #1a2b27;
    }
    button.primary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    /* Secondary variant */
    button.secondary {
      background: #fff;
      color: #172523;
      border: 1px solid #e3eae5;
    }
    button.secondary:hover:not(:disabled) {
      background: #f9faf8;
    }

    /* Danger variant */
    button.danger {
      background: #cf7c4e;
      color: #fff;
    }
    button.danger:hover:not(:disabled) {
      background: #b8664a;
    }

    /* Ghost variant */
    button.ghost {
      background: transparent;
      color: #66756f;
    }
    button.ghost:hover:not(:disabled) {
      background: #f3f6f3;
    }

    /* Sizes */
    button.sm {
      padding: 8px 12px;
      font-size: 10px;
    }

    button.lg {
      padding: 14px 20px;
      font-size: 14px;
    }

    .loading-spinner {
      display: inline-block;
      width: 12px;
      height: 12px;
      border: 2px solid rgba(255, 255, 255, 0.3);
      border-top: 2px solid #fff;
      border-radius: 50%;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
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
