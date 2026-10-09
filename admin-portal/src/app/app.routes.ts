import { Routes } from '@angular/router';
import { AuthGuard, RoleGuard, NoAuthGuard } from './core/guards/auth.guard';
import { Role } from './core/models/domain.model';
import { ShellComponent } from './layout/shell/shell.component';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [NoAuthGuard],
    loadComponent: () =>
      import('./features/auth/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [AuthGuard],
    children: [
      {
        path: 'admin/dashboard',
        canActivate: [RoleGuard],
        data: { roles: [Role.Admin, Role.ApplicationAdmin] },
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then(
            m => m.DashboardComponent
          ),
      },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then(
            m => m.DashboardComponent
          ),
      },
      {
        path: 'products',
        loadComponent: () =>
          import('./features/products/products.component').then(
            m => m.ProductsComponent
          ),
      },
      {
        path: 'orders',
        loadComponent: () =>
          import('./features/orders/orders.component').then(
            m => m.OrdersComponent
          ),
      },
      {
        path: 'campaigns',
        canActivate: [RoleGuard],
        data: { roles: [Role.ApplicationAdmin] },
        loadComponent: () =>
          import('./features/campaigns/campaigns.component').then(
            m => m.CampaignsComponent
          ),
      },
      {
        path: 'admin/users',
        canActivate: [RoleGuard],
        data: { roles: [Role.ApplicationAdmin] },
        loadComponent: () =>
          import('./features/user-master/user-master.component').then(
            m => m.UserMasterComponent
          ),
      },
      {
        path: 'admin/roles',
        canActivate: [RoleGuard],
        data: { roles: [Role.ApplicationAdmin] },
        loadComponent: () =>
          import('./features/role-master/role-master.component').then(
            m => m.RoleMasterComponent
          ),
      },
      {
        path: 'admin/permissions',
        canActivate: [RoleGuard],
        data: { roles: [Role.ApplicationAdmin] },
        loadComponent: () =>
          import('./features/permission-master/permission-master.component').then(
            m => m.PermissionMasterComponent
          ),
      },
      {
        path: 'notifications',
        loadComponent: () =>
          import('./features/notifications/notifications.component').then(
            m => m.NotificationsComponent
          ),
      },
      {
        path: 'profile',
        loadComponent: () =>
          import('./features/profile/profile.component').then(m => m.ProfileComponent),
      },
      {
        path: 'approvals',
        canActivate: [RoleGuard],
        data: { roles: [Role.StoreStaff] },
        loadComponent: () =>
          import('./features/approvals/approvals.component').then(m => m.ApprovalsComponent),
      },
      {
        path: 'stores',
        canActivate: [RoleGuard],
        data: { roles: [Role.Admin, Role.ApplicationAdmin, Role.StoreStaff] },
        loadComponent: () =>
          import('./features/stores/stores.component').then(
            m => m.StoresComponent
          ),
      },
      {
        path: '',
        redirectTo: 'dashboard',
        pathMatch: 'full',
      },
    ],
  },
  {
    path: 'unauthorized',
    loadComponent: () =>
      import('./features/unauthorized/unauthorized.component').then(
        m => m.UnauthorizedComponent
      ),
  },
  {
    path: '**',
    loadComponent: () =>
      import('./features/not-found/not-found.component').then(
        m => m.NotFoundComponent
      ),
  },
];
