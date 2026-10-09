import { Component, OnInit, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { Router } from '@angular/router';
import { HeaderComponent } from '../header/header.component';
import { SidebarComponent, NavItem } from '../sidebar/sidebar.component';
import { ToastContainerComponent } from '../../shared/components/toast-container/toast-container.component';
import { AuthService } from '../../core/services/auth.service';
import { Role, StaffCategory } from '../../core/models/domain.model';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    HeaderComponent,
    SidebarComponent,
    ToastContainerComponent,
  ],
  template: `
    <main class="shell">
      <app-sidebar
        [navItems]="navItems()"
        (onUserMenu)="onUserMenu()"
      ></app-sidebar>

      <section class="content">
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
      background: linear-gradient(165deg, #2A1BFF 0%, #6A11E8 45%, #B00699 100%);
      overflow: hidden;
    }

    .content {
      width: min(100%, 1440px);
      height: 100vh;
      min-height: 0;
      padding: 38px 52px 56px 28px;
      margin: auto;
      flex: 1;
      overflow-y: auto;
      position: relative;
      scrollbar-width: thin;
      scrollbar-color: rgba(255,255,255,0.3) transparent;
    }

    .profile-menu {
      position: absolute;
      z-index: 100;
      top: 78px;
      right: 52px;
      display: grid;
      gap: 8px;
      min-width: 220px;
      padding: 18px;
      background: var(--card-bg, #EAE4FF);
      border-radius: 24px;
      box-shadow: 0 12px 32px rgba(20,17,24,0.18);
    }

    .profile-menu strong {
      color: var(--on-card, #141118);
      font-family: 'Space Grotesk', sans-serif;
      font-size: 14px;
      font-weight: 800;
    }

    .profile-menu span {
      color: var(--on-card-muted, #5E5A66);
      font-size: 11px;
      font-family: 'Inter', sans-serif;
    }

    .profile-menu button {
      border: 0;
      background: transparent;
      padding: 7px 0;
      text-align: left;
      color: var(--on-card, #141118);
      cursor: pointer;
      font-size: 13px;
      font-family: 'Inter', sans-serif;
      font-weight: 700;
      transition: color 0.15s;
    }

    .profile-menu button:hover { color: #B0008A; }
    .profile-menu button:last-child:hover { color: #FB1A8E; }

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

  // Computed navigation based on user role
  navItems = computed(() => {
    const baseItems: NavItem[] = [
      { label: 'Overview', icon: '◈', route: '/dashboard', requiredRoles: [Role.Admin, Role.ApplicationAdmin, Role.StoreStaff] },
      { label: 'Catalog', icon: '▦', route: '/products', requiredRoles: [Role.Admin, Role.ApplicationAdmin, Role.StoreStaff] },
      { label: 'Orders', icon: '↗', route: '/orders', requiredRoles: [Role.Admin, Role.ApplicationAdmin, Role.StoreStaff], requiredPermissions: ['orders:read'] },
      { label: 'Offers', icon: '✦', route: '/campaigns', requiredRoles: [Role.ApplicationAdmin] },
      { label: 'Notifications', icon: '♧', route: '/notifications', requiredRoles: [Role.Admin, Role.ApplicationAdmin, Role.StoreStaff, Role.Customer] },
    ];

    // Admin-only items
    if (this.authService.hasRole(Role.Admin)) {
      baseItems.splice(2, 0, { label: 'Stores', icon: '⌂', route: '/stores', requiredRoles: [Role.Admin, Role.ApplicationAdmin, Role.StoreStaff] });
      baseItems.splice(3, 0, { label: 'Users', icon: '◎', route: '/admin/users', requiredRoles: [Role.ApplicationAdmin], requiredPermissions: ['users:manage'] });
      baseItems.splice(4, 0, { label: 'Roles', icon: '◇', route: '/admin/roles', requiredRoles: [Role.ApplicationAdmin], requiredPermissions: ['roles:manage'] });
      baseItems.splice(5, 0, { label: 'Permissions', icon: '⊙', route: '/admin/permissions', requiredRoles: [Role.ApplicationAdmin], requiredPermissions: ['permissions:manage'] });
    }

    const context = this.authService.userContext();
    if (context?.staffCategory === StaffCategory.StoreManager) {
      baseItems.push({ label: 'Approval Requests', icon: '✓', route: '/approvals', requiredRoles: [Role.StoreStaff] });
    } else if (context?.staffCategory === StaffCategory.StoreEmployee) {
      baseItems.push({ label: 'My Approval Requests', icon: '✓', route: '/approvals', requiredRoles: [Role.StoreStaff] });
    }
    return baseItems.filter(item => {
      const roleAllowed = !item.requiredRoles?.length || item.requiredRoles.includes(context?.role ?? '');
      const permissionAllowed = !item.requiredPermissions?.length || item.requiredPermissions.some(permission => context?.permissions?.includes(permission));
      return roleAllowed && permissionAllowed;
    });
  });

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
