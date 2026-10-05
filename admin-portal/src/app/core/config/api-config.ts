/**
 * API configuration with environment support.
 * Override API_BASE_URL via build configuration or environment variables.
 */
export const API_CONFIG = {
  baseUrl: (typeof window !== 'undefined' && (window as any)['__API_BASE_URL__']) || 
           'http://localhost:5000/api',
  endpoints: {
    // Catalog
    categories: '/categories',
    products: '/products',
    productById: (id: string) => `/products/${id}`,
    stores: '/stores',
    nearestStore: '/stores/nearest',
    
    // Orders
    orders: '/orders',
    orderById: (id: string) => `/orders/${id}`,
    
    // Notifications
    notifications: '/customer/notifications',
    markNotificationRead: (id: string) => `/customer/notifications/${id}/read`,
  },
  // Timeouts in milliseconds
  timeout: 30000,
};
