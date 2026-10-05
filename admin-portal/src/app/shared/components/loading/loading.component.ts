import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-loading',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="loading-container" [class.fullscreen]="fullscreen">
      <div class="spinner"></div>
      <p *ngIf="message">{{ message }}</p>
    </div>
  `,
  styles: [`
    .loading-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 16px;
      padding: 40px;
      min-height: 200px;
    }

    .loading-container.fullscreen {
      position: fixed;
      inset: 0;
      background: rgba(255, 255, 255, 0.9);
      z-index: 9999;
    }

    .spinner {
      width: 32px;
      height: 32px;
      border: 3px solid #e3eae5;
      border-top: 3px solid #6c9d1e;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    p {
      color: #82908b;
      font-size: 12px;
      margin: 0;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `],
})
export class LoadingComponent {
  @Input() message?: string;
  @Input() fullscreen = false;
}
