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

    .header-left {
      flex: 1;
    }

    .eyebrow {
      margin: 0 0 7px;
      color: #899a94;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1.5px;
    }

    h1 {
      margin: 0;
      font-size: 25px;
      letter-spacing: -0.8px;
      font-family: Manrope, sans-serif;
    }

    h1 span {
      color: #9bb844;
      font-size: 20px;
    }

    .top-actions {
      display: flex;
      align-items: center;
      gap: 11px;
    }

    .status {
      color: #71817b;
      background: #fff;
      border: 1px solid #e3eae5;
      border-radius: 30px;
      padding: 8px 12px;
      font-size: 11px;
      display: flex;
      align-items: center;
      gap: 7px;
    }

    .status i {
      display: inline-block;
      width: 6px;
      height: 6px;
      background: #d19a45;
      border-radius: 50%;
    }

    .status i.online {
      background: #73bf52;
    }

    .icon-button {
      border: 1px solid #e1e8e3;
      background: #fff;
      color: #66756f;
      cursor: pointer;
      width: 32px;
      height: 32px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 14px;
      transition: all 0.2s;
    }

    .icon-button:hover {
      background: #eff2ef;
      border-color: #d1dcd6;
    }

    .logout-button {
      border: 1px solid #e1e8e3;
      background: #fff;
      color: #66756f;
      cursor: pointer;
      min-height: 32px;
      padding: 0 12px;
      border-radius: 7px;
      font-size: 11px;
      font-weight: 700;
    }

    .logout-button:hover {
      background: #fff3ed;
      border-color: #e6b89d;
      color: #9b4d2d;
    }

    .mini-avatar {
      display: grid;
      place-items: center;
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: #f0c8ae;
      color: #633a2b;
      font-weight: 700;
      font-size: 10px;
      cursor: pointer;
      transition: opacity 0.2s;
    }

    .mini-avatar:hover {
      opacity: 0.8;
    }

    @media (max-width: 1000px) {
      .status {
        display: none;
      }

      h1 {
        font-size: 20px;
      }
    }

    @media (max-width: 720px) {
      .topbar {
        gap: 15px;
      }

      h1 {
        font-size: 18px;
      }

      .icon-button {
        width: 28px;
        height: 28px;
      }
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
