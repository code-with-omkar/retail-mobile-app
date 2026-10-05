import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="notifications-page">
      <div class="page-header">
        <h2>Notifications</h2>
        <p class="muted">View your notifications</p>
      </div>
      <div class="placeholder-panel">
        <p>Notifications module - To be implemented with notifications API integration</p>
      </div>
    </div>
  `,
  styles: [`
    .notifications-page {
      margin-bottom: 40px;
    }

    .page-header h2 {
      font-size: 30px;
      letter-spacing: -1.3px;
      margin: 0 0 4px;
      font-family: Manrope, sans-serif;
    }

    .muted {
      color: #82908b;
      font-size: 12px;
      margin: 0 0 26px;
    }

    .placeholder-panel {
      background: #fff;
      border: 1px solid #e5ebe6;
      border-radius: 10px;
      padding: 40px;
      text-align: center;
      color: #82908b;
    }
  `],
})
export class NotificationsComponent {}
