import { Component, Output, EventEmitter, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { TIPS, tipForUrl } from '../../shared/components/help-tip/tips';
import { RouterLink, RouterLinkActive } from '@angular/router';

export interface NavItem {
  label: string;
  icon: string;
  /** The heading the entry sits under. */
  group?: string;
  route?: string;
  requiredRoles?: string[];
  requiredPermissions?: string[];
  children?: NavItem[];
  action?: () => void;
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, IconComponent],
  template: `
    <aside class="sidebar">
      <div class="brand">
        <span class="brand-mark">Q</span>
        <span>quickcart<span class="brand-dot">.</span></span>
      </div>
      <div class="workspace-label">OPERATIONS CONSOLE</div>

      <nav>
        <ng-container *ngFor="let item of navItems; let i = index">
          <div class="group" *ngIf="item.group && item.group !== navItems[i - 1]?.group">{{ item.group }}</div>
          <a [routerLink]="item.route" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: false }" class="nav-link" [attr.title]="titleFor(item)">
            <app-icon [name]="item.icon"></app-icon>
            <span class="nav-label">{{ item.label }}</span>
          </a>
        </ng-container>
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
      color: var(--ink);
      padding: 24px 14px 22px;
      display: flex;
      flex-direction: column;
      flex-shrink: 0;
      overflow-y: auto;
      scrollbar-width: thin;
      /* the bar shows only while the pointer is over the menu, so the menu stays part of the page */
      scrollbar-color: transparent transparent;
    }

    .sidebar:hover { scrollbar-color: #B9B2D9 transparent; }

    .brand {
      display: flex;
      align-items: center;
      gap: 10px;
      color: var(--ink);
      font: 800 21px 'Bricolage Grotesque', sans-serif;
      letter-spacing: -0.8px;
      padding: 0 12px 10px;
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
      color: #6F6A80;
      opacity: .75;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1.5px;
      padding: 0 14px 2px;
      font-family: 'Plus Jakarta Sans', sans-serif;
    }

    nav {
      display: grid;
      gap: 1px;
      align-content: start;
      flex: 1;
    }

    .group {
      font-size: 10px;
      font-weight: 800;
      letter-spacing: 1.2px;
      text-transform: uppercase;
      color: #6F6A80;
      padding: 9px 14px 2px;
    }

    .nav-link {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 6px 14px;
      border-radius: 999px;
      text-decoration: none;
      color: var(--ink);
      font-size: 13px;
      font-family: 'Plus Jakarta Sans', sans-serif;
      font-weight: 600;
      transition: background .15s ease, color .15s ease, box-shadow .15s ease;
    }

    .nav-link:hover { background: var(--soft); }

    .nav-link.active {
      background: #FFD400;
      color: #141118;
      font-weight: 800;
          }

    .nav-label { flex: 1; }

    @media (max-width: 1000px) {
      .sidebar { width: 76px; padding: 24px 10px; }
      .brand { padding: 0 6px 36px; }
      .brand > span:last-child,
      .workspace-label,
      .group,
      .nav-label { display: none; }
      .nav-link { justify-content: center; padding: 12px; }
    }

    @media (max-width: 460px) {
      .sidebar { width: 58px; padding: 16px 6px; }
    }
  `],
})
export class SidebarComponent {
  @Input() navItems: NavItem[] = [];
  @Output() onUserMenu = new EventEmitter<void>();

  /** Pointing at a menu entry explains it in English and Marathi. */
  titleFor(item: NavItem): string {
    const key = tipForUrl(item.route ?? '');
    return key ? `${item.label}\n${TIPS[key].en}\n${TIPS[key].mr}` : item.label;
  }
}
