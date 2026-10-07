import { Component, Input, Output, EventEmitter, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule],
  template: `
    <header class="topbar">
      <div class="header-left">
        <p class="eyebrow">{{ dateLabel }}</p>
        <h1>Good morning, {{ userName() }} <span>✦</span></h1>
      </div>
      <div class="top-actions">
        <div class="status" [class.online]="isOnline">
          <i [class.online]="isOnline"></i>
          {{ isOnline ? 'API connected' : 'Demo mode' }}
        </div>
        <button class="icon-button" type="button" (click)="onSearch.emit()" title="Search">
          ⌕
        </button>
        <button class="icon-button" type="button" (click)="onNotifications.emit()" title="Notifications">
          ♧
        </button>
        <button class="logout-button" type="button" (click)="onLogout.emit()">
          Logout
        </button>
        <div class="mini-avatar" (click)="onUserMenu.emit()">
          {{ userInitials() }}
        </div>
      </div>
    </header>
  `,
  styles: [`
    .topbar {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 38px;
    }

    .header-left { flex: 1; }

    .eyebrow {
      margin: 0 0 7px;
      color: #E9E4FF;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1.5px;
      font-family: 'Inter', sans-serif;
      text-transform: uppercase;
    }

    h1 {
      margin: 0;
      font-size: 25px;
      letter-spacing: -0.8px;
      font-family: 'Space Grotesk', sans-serif;
      font-weight: 800;
      color: #fff;
    }

    h1 span { color: #FFD400; font-size: 20px; }

    .top-actions { display: flex; align-items: center; gap: 11px; }

    .status {
      color: #141118;
      background: #02F34C;
      border-radius: 999px;
      padding: 8px 14px;
      font-size: 11px;
      font-weight: 700;
      display: flex;
      align-items: center;
      gap: 7px;
      font-family: 'Inter', sans-serif;
    }

    .status i { display: inline-block; width: 6px; height: 6px; background: #fff; border-radius: 50%; }
    .status i.online { background: #fff; }
    .status:not(.online) { background: #FFD400; }

    .icon-button {
      border: 0;
      background: #EAE4FF;
      color: #141118;
      cursor: pointer;
      width: 36px;
      height: 36px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 15px;
      transition: background .2s;
    }

    .icon-button:hover { background: #FFF1A8; }

    .logout-button {
      border: 0;
      background: #EAE4FF;
      color: #141118;
      cursor: pointer;
      min-height: 36px;
      padding: 0 16px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 800;
      font-family: 'Inter', sans-serif;
      transition: background .2s;
    }

    .logout-button:hover { background: #FB1A8E; }

    .mini-avatar {
      display: grid;
      place-items: center;
      width: 36px;
      height: 36px;
      border-radius: 50%;
      background: #FFD400;
      color: #141118;
      font-weight: 800;
      font-size: 11px;
      cursor: pointer;
      transition: transform .2s;
      font-family: 'Inter', sans-serif;
    }

    .mini-avatar:hover { transform: scale(1.06); }

    @media (max-width: 1000px) {
      .status { display: none; }
      h1 { font-size: 20px; }
    }

    @media (max-width: 720px) {
      .topbar { gap: 15px; }
      h1 { font-size: 18px; }
      .icon-button { width: 32px; height: 32px; }
    }
  `],
})
export class HeaderComponent {
  private authService = inject(AuthService);

  @Input() dateLabel = '';
  @Input() isOnline = true;
  @Output() onSearch = new EventEmitter<void>();
  @Output() onNotifications = new EventEmitter<void>();
  @Output() onUserMenu = new EventEmitter<void>();
  @Output() onLogout = new EventEmitter<void>();

  readonly userName = computed(() => this.authService.userContext()?.displayName || 'User');
  readonly userInitials = computed(() => {
    const name = this.authService.userContext()?.displayName;
    return name ? name.split(' ').map((n: string) => n[0]).join('').toUpperCase().slice(0, 2) : 'AK';
  });
}
