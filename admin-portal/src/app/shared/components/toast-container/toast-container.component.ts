import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Toast, ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="toast-container">
      <div
        *ngFor="let toast of toastService.toasts$()"
        [class]="'toast ' + toast.type"
        [@toastAnimation]
      >
        <span class="toast-icon">{{ getIcon(toast.type) }}</span>
        <span class="toast-message">{{ toast.message }}</span>
        <button
          class="toast-close"
          (click)="toastService.dismiss(toast.id)"
          aria-label="Close toast"
        >
          ×
        </button>
      </div>
    </div>
  `,
  styles: [`
    .toast-container {
      position: fixed;
      top: 20px;
      right: 20px;
      z-index: 9998;
      display: flex;
      flex-direction: column;
      gap: 12px;
      max-width: 400px;
      pointer-events: none;
    }

    .toast {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      border-radius: 8px;
      font-size: 12px;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      pointer-events: all;
      animation: slideIn 0.3s ease;
    }

    .toast.success {
      background: #e4f4d8;
      color: #327a1a;
      border-left: 4px solid #66a744;
    }

    .toast.error {
      background: #fae4d9;
      color: #8b3a1e;
      border-left: 4px solid #cf7c4e;
    }

    .toast.warning {
      background: #fff1cf;
      color: #8b6a1e;
      border-left: 4px solid #c09137;
    }

    .toast.info {
      background: #deedf3;
      color: #1e5a73;
      border-left: 4px solid #4c9bb3;
    }

    .toast-icon {
      font-weight: 700;
      font-size: 14px;
      flex-shrink: 0;
    }

    .toast-message {
      flex: 1;
    }

    .toast-close {
      background: none;
      border: none;
      font-size: 18px;
      cursor: pointer;
      padding: 0;
      color: inherit;
      opacity: 0.6;
      flex-shrink: 0;
    }

    .toast-close:hover {
      opacity: 1;
    }

    @keyframes slideIn {
      from {
        transform: translateX(400px);
        opacity: 0;
      }
      to {
        transform: translateX(0);
        opacity: 1;
      }
    }
  `],
})
export class ToastContainerComponent {
  readonly toastService = inject(ToastService);

  getIcon(type: string): string {
    const icons: Record<string, string> = {
      success: '✓',
      error: '!',
      warning: '⚠',
      info: 'ℹ',
    };
    return icons[type] || 'ℹ';
  }
}
