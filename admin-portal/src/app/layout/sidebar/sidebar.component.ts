import { Component, Output, EventEmitter, Input, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

export interface NavItem {
  label: string;
  icon: string;
  route?: string;
  requiredRoles?: string[];
  requiredPermissions?: string[];
  children?: NavItem[];
  action?: () => void;
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  template: `
    <aside class="sidebar">
      <div class="brand">
        <span class="brand-mark">Q</span>
        <span>quickcart<span class="brand-dot">.</span></span>
      </div>
      <div class="workspace-label">OPERATIONS CONSOLE</div>

      <nav>
        <a
          *ngFor="let item of navItems"
          [routerLink]="item.route"
          routerLinkActive="active"
          [routerLinkActiveOptions]="{ exact: false }"
          class="nav-link"
        >
          <span class="nav-icon">{{ item.icon }}</span>
          <span class="nav-label">{{ item.label }}</span>
        </a>
      </nav>

      <div class="sidebar-footer">
        <div class="avatar">{{ userInitials() }}</div>
        <div>
          <strong>{{ userName() }}</strong>
          <small>{{ userRole() }}</small>
        </div>
        <button class="more-btn" (click)="onUserMenu.emit()" title="User menu">•••</button>
      </div>
    </aside>
  `,
  styles: [`
    .sidebar {
      width: 244px;
      height: 100vh;
      position: sticky;
      top: 0;
      box-sizing: border-box;
      background: #172523;
      color: #d8e1dc;
      padding: 34px 20px 22px;
      display: flex;
      flex-direction: column;
      flex-shrink: 0;
      border-right: 1px solid #1a2b27;
      overflow-y: auto;
      scrollbar-width: thin;
      scrollbar-color: #34433f transparent;
    }

    .brand {
      display: flex;
      align-items: center;
      gap: 10px;
      color: #fff;
      font: 800 21px Manrope, sans-serif;
      letter-spacing: -0.8px;
      padding: 0 12px 52px;
    }

    .brand-mark {
      display: grid;
      place-items: center;
      width: 28px;
      height: 28px;
      border-radius: 9px;
      background: #d8f16e;
      color: #172523;
      font-size: 17px;
    }

    .brand-dot {
      color: #d8f16e;
    }

    .workspace-label {
      color: #899a94;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1.5px;
      padding: 0 14px 14px;
    }

    nav {
      display: grid;
      gap: 5px;
      flex: 1;
    }

    .nav-link {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 12px 14px;
      border-radius: 8px;
      text-decoration: none;
      color: #9aaca6;
      font-size: 13px;
      transition: all 0.2s;
    }

    .nav-link:hover {
      background: rgba(216, 241, 110, 0.1);
      color: #b7c6c0;
    }

    .nav-link.active {
      background: #d8f16e;
      color: #172523;
    }

    .nav-link.active .nav-icon {
      color: #172523;
    }

    .nav-icon {
      width: 18px;
      font-size: 17px;
      color: #b7c6c0;
    }

    .nav-label {
      flex: 1;
    }

    .sidebar-footer {
      border-top: 1px solid #34433f;
      padding: 22px 8px 0;
      margin-top: auto;
      display: flex;
      align-items: center;
      gap: 9px;
      font-size: 11px;
    }

    .avatar {
      display: grid;
      place-items: center;
      width: 31px;
      height: 31px;
      border-radius: 50%;
      background: #f0c8ae;
      color: #633a2b;
      font-weight: 700;
      font-size: 10px;
      flex-shrink: 0;
    }

    .sidebar-footer strong,
    .sidebar-footer small {
      display: block;
    }

    .sidebar-footer strong {
      color: #d8e1dc;
    }

    .sidebar-footer small {
      color: #83938e;
      margin-top: 3px;
    }

    .more-btn {
      margin-left: auto;
      background: none;
      border: none;
      color: #83938e;
      cursor: pointer;
      font-size: 14px;
      letter-spacing: 1px;
      transition: color 0.2s;
    }

    .more-btn:hover {
      color: #d8e1dc;
    }

    @media (max-width: 1000px) {
      width: 70px;
      padding: 24px 10px;

      .brand {
        padding: 0 11px 45px;
      }

      .brand > span:last-child,
      .workspace-label,
      .nav-label {
        font-size: 0;
      }

      .brand-mark {
        flex-shrink: 0;
      }

      .nav-link {
        justify-content: center;
        padding: 12px;
      }

      .nav-icon {
        font-size: 17px;
      }

      .sidebar-footer {
        display: none;
      }
    }

    @media (max-width: 460px) {
      display: none;
    }
  `],
})
export class SidebarComponent {
  private authService = inject(AuthService);

  @Input() navItems: NavItem[] = [];
  @Output() onUserMenu = new EventEmitter<void>();

  readonly userName = computed(() => this.authService.userContext()?.displayName || 'User');
  readonly userRole = computed(() => this.authService.userContext()?.role || 'User');
  readonly userInitials = computed(() => {
    const name = this.authService.userContext()?.displayName;
    return name ? name.split(' ').map((n: string) => n[0]).join('').toUpperCase().slice(0, 2) : 'AK';
  });
}
