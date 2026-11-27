import { Component, OnInit, computed, signal, inject, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { Router } from '@angular/router';
import { HeaderComponent } from '../header/header.component';
import { SidebarComponent, NavItem } from '../sidebar/sidebar.component';
import { OrgSwitcherComponent } from '../org-switcher/org-switcher.component';
import { AlertsService } from '../../core/services/alerts.service';
import { DecorateDirective } from './decorate';
import { menuFor } from './menu';
import { HelpTipComponent } from '../../shared/components/help-tip/help-tip.component';
import { tipForUrl } from '../../shared/components/help-tip/tips';
import { ToastContainerComponent } from '../../shared/components/toast-container/toast-container.component';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { Role } from '../../core/models/domain.model';

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
    DecorateDirective,
  ],
  template: `
    <main class="shell">
      <app-sidebar
        [navItems]="navItems()"
        (onUserMenu)="onUserMenu()"
      ></app-sidebar>

      <section class="content" appDecorate>
        <app-org-switcher></app-org-switcher>
        <app-header
          [dateLabel]="dateLabel()"
          [showApi]="isAdmin()"
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
          <button type="button" class="act" (click)="viewProfile()">View profile</button>
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
    .content ::ng-deep app-orders, .content ::ng-deep app-user-master, .content ::ng-deep app-stores, .content ::ng-deep app-categories, .content ::ng-deep app-delivery-slots, .content ::ng-deep app-commission, .content ::ng-deep app-ads, .content ::ng-deep app-coupons, .content ::ng-deep app-campaigns, .content ::ng-deep app-approvals, .content ::ng-deep app-complaints, .content ::ng-deep app-notifications, .content ::ng-deep app-placement { flex: 1 1 0; min-height: 430px; display: flex; flex-direction: column; }

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
export class ShellComponent implements OnInit, OnDestroy {
  readonly authService = inject(AuthService);
  private router = inject(Router);

  profileOpen = signal(false);
  isOnline = signal(true);
  private config = inject(ConfigService);
  private apiTimer?: ReturnType<typeof setInterval>;

  isAdmin(): boolean { return this.authService.hasRole(Role.ApplicationAdmin) || this.authService.hasRole(Role.Admin); }

  /** Any answer from the server, even a refusal, means it can be reached. */
  async checkApi(): Promise<void> {
    try {
      await fetch(this.config.getBaseUrl(), { cache: 'no-store' });
      this.isOnline.set(true);
    } catch {
      this.isOnline.set(false);
    }
  }

  // What this person may see (see menu.ts)
  private alerts = inject(AlertsService);
  navItems = computed(() => menuFor(this.authService.userContext()).map(item => (item.route === '/notifications' ? { ...item, badge: this.alerts.unread() } : item)));

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

  private alertTimer?: ReturnType<typeof setInterval>;

  ngOnDestroy(): void { clearInterval(this.alertTimer); clearInterval(this.apiTimer); }

  ngOnInit(): void {
    if (this.isAdmin()) {
      void this.checkApi();
      this.apiTimer = setInterval(() => void this.checkApi(), 60 * 1000);
    }
    if (menuFor(this.authService.userContext()).some(item => item.route === '/notifications')) {
      this.alerts.refresh();
      this.alertTimer = setInterval(() => this.alerts.refresh(), 5 * 60 * 1000);
    }
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
