import { Component, Output, EventEmitter, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';

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
    </aside>
  `,
  styles: [`
    .sidebar {
      width: 244px;
      height: 100vh;
      position: sticky;
      top: 0;
      box-sizing: border-box;
      background: transparent;
      color: #E9E4FF;
      padding: 34px 20px 22px;
      display: flex;
      flex-direction: column;
      flex-shrink: 0;
      overflow-y: auto;
      scrollbar-width: none;
    }

    .brand {
      display: flex;
      align-items: center;
      gap: 10px;
      color: #fff;
      font: 800 21px 'Space Grotesk', sans-serif;
      letter-spacing: -0.8px;
      padding: 0 12px 44px;
    }

    .brand-mark {
      display: grid;
      place-items: center;
      width: 32px;
      height: 32px;
      border-radius: 12px;
      background: #FFD400;
      color: #141118;
      font-size: 17px;
      font-weight: 800;
    }

    .brand-dot { color: #FFD400; }

    .workspace-label {
      color: #E9E4FF;
      opacity: .75;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1.5px;
      padding: 0 14px 14px;
      font-family: 'Inter', sans-serif;
    }

    nav {
      display: grid;
      gap: 4px;
      align-content: start;
      flex: 1;
    }

    .nav-link {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 12px 16px;
      border-radius: 999px;
      text-decoration: none;
      color: #fff;
      font-size: 13px;
      font-family: 'Inter', sans-serif;
      font-weight: 600;
      transition: background .15s ease, color .15s ease, box-shadow .15s ease;
    }

    .nav-link:hover { background: rgba(255,255,255,0.14); }

    .nav-link.active {
      background: #FFD400;
      color: #141118;
      font-weight: 800;
      box-shadow: 0 12px 32px rgba(20,17,24,.18);
    }

    .nav-icon {
      width: 20px;
      font-size: 17px;
      text-align: center;
      color: inherit;
      flex-shrink: 0;
    }

    .nav-label { flex: 1; }

    @media (max-width: 1000px) {
      .sidebar { width: 76px; padding: 24px 10px; }
      .brand { padding: 0 6px 36px; }
      .brand > span:last-child,
      .workspace-label,
      .nav-label { display: none; }
      .nav-link { justify-content: center; padding: 12px; }
    }

    @media (max-width: 460px) {
      .sidebar { display: none; }
    }
  `],
})
export class SidebarComponent {
  @Input() navItems: NavItem[] = [];
  @Output() onUserMenu = new EventEmitter<void>();
}
