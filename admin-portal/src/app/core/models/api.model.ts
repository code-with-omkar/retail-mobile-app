/**
 * API request/response DTOs
 */

import { UserContext } from './domain.model';

export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  message?: string;
  errors?: string[];
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  tokenType: string;
  user: AuthenticationUser;
  userContext?: UserContext;
}

export interface AuthenticationUser {
  id: string;
  displayName: string;
  email?: string;
  organizationId: string;
  roles: string[];
  permissions: string[];
  staffCategory?: string;
}

export interface CreateOrderRequest {
  userId: string;
  latitude: number;
  longitude: number;
  deliveryAddress: string;
  items: OrderLineRequest[];
}

export interface OrderLineRequest {
  productId: string;
  quantity: number;
}

export interface MarkNotificationReadRequest {
  isRead: boolean;
}
