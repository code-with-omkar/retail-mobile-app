import { Role, StaffCategory, UserContext } from '../../core/models/domain.model';
import { NavItem } from '../sidebar/sidebar.component';

const ADMINS = [Role.ApplicationAdmin, Role.Admin];
const EVERYONE = [Role.ApplicationAdmin, Role.Admin, Role.StoreStaff];
const PLATFORM = [Role.ApplicationAdmin];

/** Every entry of the portal menu, in order. The roles must match the route's roles in app.routes.ts. */
export const MENU: NavItem[] = [
  { group: 'Daily', label: 'Overview', icon: 'home', route: '/dashboard', requiredRoles: ADMINS },
  { group: 'Daily', label: 'Today', icon: 'today', route: '/today', requiredRoles: EVERYONE },
  { group: 'Daily', label: 'Orders', icon: 'orders', route: '/orders', requiredRoles: EVERYONE },
  { group: 'Daily', label: 'Approval requests', icon: 'check', route: '/approvals', requiredRoles: [Role.StoreStaff] },
  // Notifications is left out until the portal has a notifications screen (its route shows a placeholder).
  { group: 'Catalogue', label: 'Catalog', icon: 'box', route: '/products', requiredRoles: EVERYONE },
  { group: 'Catalogue', label: 'Categories', icon: 'grid', route: '/categories', requiredRoles: ADMINS },
  { group: 'Catalogue', label: 'Stock', icon: 'layers', route: '/stock', requiredRoles: EVERYONE },
  { group: 'Catalogue', label: 'Stores', icon: 'store', route: '/stores', requiredRoles: EVERYONE },
  { group: 'Marketing', label: 'Offers', icon: 'tag', route: '/campaigns', requiredRoles: EVERYONE },
  { group: 'Marketing', label: 'Ads', icon: 'image', route: '/ads', requiredRoles: ADMINS },
  { group: 'Marketing', label: 'Coupons', icon: 'ticket', route: '/coupons', requiredRoles: ADMINS },
  { group: 'Marketing', label: 'Placement', icon: 'up', route: '/placement', requiredRoles: EVERYONE },
  { group: 'Insights', label: 'Reports', icon: 'chart', route: '/reports', requiredRoles: EVERYONE },
  { group: 'Insights', label: 'Behaviour', icon: 'pulse', route: '/behaviour', requiredRoles: EVERYONE },
  { group: 'Settings', label: 'Delivery times', icon: 'clock', route: '/delivery-times', requiredRoles: ADMINS },
  { group: 'Settings', label: 'Commission', icon: 'percent', route: '/commission', requiredRoles: ADMINS },
  { group: 'Settings', label: 'Users', icon: 'users', route: '/admin/users', requiredRoles: PLATFORM },
  { group: 'Settings', label: 'Roles', icon: 'shield', route: '/admin/roles', requiredRoles: PLATFORM },
  { group: 'Settings', label: 'Permissions', icon: 'key', route: '/admin/permissions', requiredRoles: PLATFORM },
];

/**
 * What this person may see. Approval requests belong to store staff with a staff category, and are named by what they do there.
 * Store staff also need the orders permission for Orders; administrators are not limited by permissions.
 */
export function menuFor(context: UserContext | null | undefined): NavItem[] {
  const role = context?.role;
  if (!role) return [];
  return MENU.filter(item => item.requiredRoles!.includes(role)).flatMap(item => {
    if (item.route === '/approvals') {
      if (context!.staffCategory === StaffCategory.StoreManager) return [item];
      return context!.staffCategory === StaffCategory.StoreEmployee ? [{ ...item, label: 'My approval requests' }] : [];
    }
    if (item.route === '/orders' && role === Role.StoreStaff && !context!.permissions?.includes('orders:read')) return [];
    return [item];
  });
}
