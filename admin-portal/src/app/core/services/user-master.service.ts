import { Injectable, signal } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/api.model';
import { User, Organization, Store, Role, UserStatus, Permission, StaffCategory } from '../models/domain.model';
import { ApiService } from './api.service';

export interface CreateUserRequest {
  firstName: string;
  lastName: string;
  email: string;
  organizationId: string;
  storeId?: string;
  storeIds?: string[];
  role: Role;
  status: UserStatus;
  staffCategory?: StaffCategory;
  permissions: string[];
}

export interface RoleDefinition {
  id: string;
  code: string;
  name: string;
  description?: string;
  isActive: boolean;
  permissions: Permission[];
}

export type UpdateUserRequest = Partial<CreateUserRequest> & {
  id?: string;
};

@Injectable({ providedIn: 'root' })
export class UserMasterService {
  private readonly permissionsSignal = signal<Permission[]>([]);
  readonly permissions = this.permissionsSignal.asReadonly();

  constructor(private readonly api: ApiService) {}

  get defaultPermissions(): Permission[] {
    return this.permissionsSignal();
  }

  getUsers(): Observable<ApiResponse<User[]>> {
    return this.api.get<BackendUser[]>('/users').pipe(
      map(response => ({
        ...response,
        data: response.data?.map(user => ({
          ...user,
          firstName: user.firstName ?? user.fullName,
          lastName: user.lastName ?? '',
          fullName: user.fullName,
          email: user.email ?? '',
          status: user.isActive ? UserStatus.Active : UserStatus.Inactive,
          permissions: user.permissions ?? [],
        })) as User[] | undefined,
      }))
    );
  }

  getOrganizations(): Observable<ApiResponse<Organization[]>> {
    return this.api.get<Organization[]>('/organizations');
  }

  getStores(organizationId?: string): Observable<ApiResponse<Store[]>> {
    return this.api.get<Store[]>('/stores', { params: { organizationId } });
  }

  createUser(request: CreateUserRequest): Observable<ApiResponse<User>> {
    return this.api.post<User>('/users', {
      ...request,
      storeIds: request.storeIds ?? (request.storeId ? [request.storeId] : []),
      isActive: request.status === UserStatus.Active,
    });
  }

  updateUser(id: string, request: Partial<CreateUserRequest>): Observable<ApiResponse<User>> {
    return this.api.put<User>(`/users/${id}`, {
      firstName: request.firstName,
      lastName: request.lastName,
      storeId: request.storeId,
      storeIds: request.storeIds,
      role: request.role,
      staffCategory: request.staffCategory,
      isActive: request.status === UserStatus.Active,
    });
  }

  toggleUserStatus(id: string, status: UserStatus): Observable<ApiResponse<User>> {
    return this.api.patch<User>(`/users/${id}/status`, {
      isActive: status === UserStatus.Active,
    });
  }

  getRoles(): Observable<ApiResponse<RoleDefinition[]>> {
    return this.api.get<RoleDefinition[]>('/roles');
  }

  getPermissions(): Observable<ApiResponse<Permission[]>> {
    return this.api.get<Permission[]>('/permissions').pipe(
      map(response => {
        this.permissionsSignal.set(response.data ?? []);
        return response;
      })
    );
  }

  updateRolePermissions(roleId: string, permissionIds: string[]): Observable<ApiResponse<RoleDefinition>> {
    return this.api.put<RoleDefinition>(`/roles/${roleId}/permissions`, { permissionIds });
  }
}

interface BackendUser extends Omit<User, 'status'> {
  status?: UserStatus;
}
