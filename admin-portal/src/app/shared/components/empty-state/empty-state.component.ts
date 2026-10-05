import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="empty-container">
      <div class="empty-icon">{{ icon }}</div>
      <h3>{{ title }}</h3>
      <p>{{ message }}</p>
    </div>
  `,
  styles: [`
    .empty-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 60px 40px;
      text-align: center;
      color: #82908b;
    }

    .empty-icon {
      font-size: 48px;
      opacity: 0.5;
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
    }
  `],
})
export class EmptyStateComponent {
  @Input() icon = '◯';
  @Input() title = 'No data';
  @Input() message = 'There is nothing to display';
}
