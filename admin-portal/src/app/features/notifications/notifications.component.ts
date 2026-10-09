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
    :host { display: block; font-family: 'Inter', sans-serif; }
    .notifications-page { margin-bottom: 40px; max-width: 900px; margin-inline: auto; }
    .page-header h2 { font: 800 32px 'Space Grotesk', sans-serif; letter-spacing: -1px; margin: 0 0 6px; color: #fff; }
    .muted { color: #E9E4FF; font-size: 12px; margin: 0 0 26px; }
    .placeholder-panel { background: #FFF1A8; border-radius: 24px; padding: 40px; text-align: center; color: #141118; font-weight: 600; font-size: 13px; }
  `],
})
export class NotificationsComponent {}
