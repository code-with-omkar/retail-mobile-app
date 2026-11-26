import { Component, OnInit, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { Router } from '@angular/router';
import { HeaderComponent } from '../header/header.component';
import { SidebarComponent, NavItem } from '../sidebar/sidebar.component';
import { OrgSwitcherComponent } from '../org-switcher/org-switcher.component';
import { menuFor } from './menu';
import { HelpTipComponent } from '../../shared/components/help-tip/help-tip.component';
import { tipForUrl } from '../../shared/components/help-tip/tips';
import { ToastContainerComponent } from '../../shared/components/toast-container/toast-container.component';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    HeaderComponent,
    SidebarComponent,
    ToastContainerComponent,
    OrgSwitcherComponent,
    HelpTipComponent,
  ],
  template: `
    <main class="shell">
      <app-sidebar
        [navItems]="navItems()"
        (onUserMenu)="onUserMenu()"
      ></app-sidebar>

      <section class="content">
        <app-org-switcher></app-org-switcher>
        <app-header
          [dateLabel]="dateLabel()"
          [isOnline]="isOnline()"
          (onSearch)="onSearch()"
          (onNotifications)="onNotifications()"
          (onUserMenu)="onUserMenu()"
          (onLogout)="logout()"
        ></app-header>

        <div *ngIf="profileOpen()" class="profile-menu">
          <strong>{{ authService.userContext()?.displayName || 'User' }}</strong>
          <span>{{ authService.userContext()?.email || 'Username unavailable' }}</span>
          <span>{{ authService.userContext()?.role || 'User' }}</span>
          <button type="button" (click)="viewProfile()">View Profile</button>
          <button type="button" (click)="logout()">Logout</button>
        </div>

        <div class="page-help" *ngIf="tipKey() as key"><app-tip [key]="key"></app-tip><span>Help · मदत</span></div>
        <router-outlet></router-outlet>
      </section>
    </main>

    <app-toast-container></app-toast-container>
  `,
  styles: [`
    .shell {
      height: 100vh;
      align-items: flex-start;
      display: flex;
      background: transparent;
      overflow: hidden;
    }

    .content {
      width: min(100%, 1440px);
      height: 100vh;
      min-height: 0;
      display: flex;
      flex-direction: column;
      padding: 22px 40px 14px 28px;
      margin: auto;
      flex: 1;
      overflow-y: auto;
      position: relative;
      scrollbar-width: thin;
      scrollbar-color: #B9B2D9 transparent;
    }

    /* other screens keep their natural height and the page scrolls; a list screen takes the rest of the window */
    .content > * { flex-shrink: 0; }
    .content ::ng-deep app-orders, .content ::ng-deep app-user-master, .content ::ng-deep app-stores, .content ::ng-deep app-categories, .content ::ng-deep app-delivery-slots, .content ::ng-deep app-commission { flex: 1 1 0; min-height: 430px; display: flex; flex-direction: column; }

    .page-help { display: flex; align-items: center; gap: 7px; margin: -8px 0 8px; font-size: 11px; font-weight: 700; color: var(--muted); }

    .profile-menu {
      position: absolute;
      z-index: 100;
      top: 78px;
      right: 52px;
      display: grid;
      gap: 8px;
      min-width: 220px;
      padding: 18px;
      background: #fff;
      border-radius: 24px;
      border: 1px solid var(--line);
      box-shadow: 0 12px 32px rgba(20,17,24,0.18);
    }

    .profile-menu strong {
      color: var(--on-card, #141118);
      font-family: 'Bricolage Grotesque', sans-serif;
      font-size: 14px;
      font-weight: 800;
    }

    .profile-menu span {
      color: var(--on-card-muted, #5E5A66);
      font-size: 11px;
      font-family: 'Plus Jakarta Sans', sans-serif;
    }

    .profile-menu button {
      border: 0;
      background: transparent;
      padding: 7px 0;
      text-align: left;
      color: var(--on-card, #141118);
      cursor: pointer;
      font-size: 13px;
      font-family: 'Plus Jakarta Sans', sans-serif;
      font-weight: 700;
      transition: color 0.15s;
    }

    .profile-menu button:hover { color: var(--danger); }
    .profile-menu button:last-child:hover { color: #C42F1D; }

    @media (max-width: 1000px) {
      .content { padding: 30px 25px 45px 16px; }
    }

    @media (max-width: 720px) {
      .content { padding: 24px 16px; }
    }

    @media (max-width: 460px) {
      .content { padding: 22px 13px; }
    }
  `],
})
export class ShellComponent implements OnInit {
  readonly authService = inject(AuthService);
  private router = inject(Router);

  isOnline = signal(true);
  profileOpen = signal(false);

  // What this person may see (see menu.ts)
  navItems = computed(() => menuFor(this.authService.userContext()));

  /** The help for the page in front, if it has any. */
  tipKey(): string | null { return tipForUrl(this.router.url ?? ''); }

  dateLabel = computed(() => {
    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const today = new Date();
    return days[today.getDay()] + ', ' + today.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  });

  ngOnInit(): void {
    // Optionally check API connectivity
    // this.checkApiStatus();
  }

  onSearch(): void {
    console.log('Search clicked');
  }

  onNotifications(): void {
    console.log('Notifications clicked');
  }

  onUserMenu(): void {
    this.profileOpen.update(open => !open);
  }

  viewProfile(): void { this.profileOpen.set(false); void this.router.navigate(['/profile']); }

  logout(): void {
    this.authService.logout();
    void this.router.navigate(['/login']);
  }
}
