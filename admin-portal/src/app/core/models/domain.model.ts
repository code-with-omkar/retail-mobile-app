export interface AdminDashboard {
  totalStores: number;
  activeStores: number;
  totalProducts: number;
  activeProducts: number;
  totalCustomers: number;
  totalOrders: number;
  todaysOrders: number;
  activeOrders: number;
  completedOrders: number;
  pendingOrders: number;
  cancelledOrders: number;
  totalSales: number;
  todaysSales: number;
  activeOrdersList: ActiveOrderSummary[];
}

export interface ActiveOrderSummary {
  id: string;
  orderNumber: string;
  customer: string;
  store: string;
  status: OrderStatus;
  orderTime: string;
  amount: number;
  deliveryPartner?: string;
}
/**
 * Domain models mirroring backend entities.
 */

export enum Role {
  Customer = 'Customer',
  StoreStaff = 'StoreStaff',
  Admin = 'Admin',
  ApplicationAdmin = 'ApplicationAdmin',
  DeliveryPartner = 'DeliveryPartner',
}

export enum StaffCategory {
  StoreManager = 'StoreManager',
  StoreEmployee = 'StoreEmployee',
}

export enum OrderStatus {
  Pending = 'Pending',
  Accepted = 'Accepted',
  Preparing = 'Preparing',
  Ready = 'Ready',
  Completed = 'Completed',
  Rejected = 'Rejected',
  Confirmed = 'Confirmed',
  OutForDelivery = 'OutForDelivery',
  Delivered = 'Delivered',
  Cancelled = 'Cancelled',
}

export enum UserStatus {
  Active = 'Active',
  Inactive = 'Inactive',
  Pending = 'Pending',
  Suspended = 'Suspended',
}

export interface Permission {
isActive: any;
  id: string;
  code: string;
  name: string;
  description: string;
}

export interface Organization {
  id: string;
  name: string;
  isActive: boolean;
}

export interface UserContext {
  userId: string;
  displayName: string;
  email?: string;
  organizationId: string;
  storeId?: string;
  role: Role;
  staffCategory?: StaffCategory;
  roles?: string[];
  permissions?: string[];
}

export interface User {
  id: string;
  firstName: string;
  lastName: string;
  fullName: string;
  email: string;
  organizationId: string;
  storeId?: string;
  storeIds?: string[];
  role: Role;
  staffCategory?: StaffCategory;
  status: UserStatus;
  isActive: boolean;
  permissions: Permission[];
  createdAt?: string;
  updatedAt?: string;
}

export interface Category {
  id: string;
  name: string;
  parentCategoryId?: string;
  isActive: boolean;
}

export interface Product {
  id: string;
  sku: string;
  name: string;
  description: string;
  price: number;
  unitOfMeasure: string;
  categoryId: string;
  imageUrl?: string;
  isActive: boolean;
}

export interface Store {
  id: string;
  name: string;
  address: string;
  latitude: number;
  longitude: number;
  serviceRadiusKm: number;
  isActive: boolean;
}

export interface OrderLineItem {
  productId: string;
  productNameSnapshot: string;
  unitPrice: number;
  quantity: number;
  totalPrice: number;
}

export interface OrderStatusHistory {
  status: OrderStatus;
  changedAt: string;
}

export interface Order {
  id: string;
  orderNumber: string;
  userId: string;
  storeId: string;
  totalAmount: number;
  status: OrderStatus;
  deliveryAddress: string;
  latitude: number;
  longitude: number;
  createdAt: string;
  items: OrderLineItem[];
  statusHistory: OrderStatusHistory[];
}

export interface AdminOrder {
  id: string;
  orderNumber: string;
  userId: string;
  customer: string;
  storeId: string;
  store: string;
  totalAmount: number;
  status: OrderStatus;
  createdAt: string;
}

export interface Notification {
  id: string;
  userId: string;
  title: string;
  message: string;
  orderId?: string;
  isRead: boolean;
  createdAt: string;
}
