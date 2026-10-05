import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonComponent } from '../button/button.component';

@Component({
  selector: 'app-error-state',
  standalone: true,
  imports: [CommonModule, ButtonComponent],
  template: `
    <div class="error-container">
      <div class="error-icon">!</div>
      <h3>{{ title }}</h3>
      <p>{{ message }}</p>
      <app-button
        *ngIf="retryLabel"
        [label]="retryLabel"
        variant="primary"
        size="sm"
        (onClick)="onRetry.emit()"
      ></app-button>
    </div>
  `,
  styles: [`
    .error-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 60px 40px;
      text-align: center;
    }

    .error-icon {
      display: grid;
      place-items: center;
      width: 48px;
      height: 48px;
      border-radius: 50%;
      background: #fae4d9;
      color: #cf7c4e;
      font-size: 24px;
      font-weight: 700;
    }

    h3 {
      font-size: 14px;
      font-weight: 700;
      color: #172523;
      margin: 0;
    }

    p {
      font-size: 12px;
      color: #99a39f;
      margin: 0;
      max-width: 300px;
    }
  `],
})
export class ErrorStateComponent {
  @Input() icon = '!';
  @Input() title = 'Error';
  @Input() message = 'Something went wrong. Please try again.';
  @Input() retryLabel?: string;
  @Output() onRetry = new EventEmitter<void>();
}
