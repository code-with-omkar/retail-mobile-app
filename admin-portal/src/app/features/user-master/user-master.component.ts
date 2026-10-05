import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonComponent } from '../../shared/components/button/button.component';
import { InputComponent } from '../../shared/components/input/input.component';
import { SelectComponent, SelectOption } from '../../shared/components/select/select.component';
import { TableColumn, TableComponent } from '../../shared/components/table/table.component';
import { Organization, Permission, Role, StaffCategory, Store, User, UserStatus } from '../../core/models/domain.model';
import { UserMasterService, CreateUserRequest, UpdateUserRequest } from '../../core/services/user-master.service';

@Component({
  selector: 'app-user-master',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonComponent,
    InputComponent,
    SelectComponent,
    TableComponent,
  ],
  templateUrl: './user-master.component.html',
  styleUrls: ['./user-master.component.css'],
})
export class UserMasterComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly userMasterService = inject(UserMasterService);

  readonly users = signal<User[]>([]);
  readonly organizations = signal<Organization[]>([]);
  readonly stores = signal<Store[]>([]);
  readonly isLoading = signal(false);
  readonly apiError = signal<string | null>(null);
  readonly selectedUser = signal<User | null>(null);
  readonly isFormVisible = signal(false);
  readonly mode = signal<'create' | 'edit' | 'view'>('create');
  readonly searchTerm = signal('');
  readonly statusFilter = signal('all');
  readonly roleFilter = signal('all');
  readonly organizationFilter = signal('all');
  readonly sortBy = signal('fullName');
  readonly sortOrder = signal<'asc' | 'desc'>('asc');
  readonly page = signal(1);
  readonly pageSize = signal(10);

  readonly permissionCatalog = this.userMasterService.permissions;
  readonly roleDefinitions = signal<{ id: string; code: string; name: string; description?: string; isActive: boolean; permissions: Permission[] }[]>([]);
  readonly effectiveRolePermissions = computed(() => this.roleDefinitions().find(role => role.code === this.form.controls.role.value)?.permissions ?? []);

  readonly roleOptions = computed<SelectOption[]>(() => this.roleDefinitions().map(role => ({ value: role.code, label: role.name })));
  readonly staffCategoryOptions: SelectOption[] = [
    { value: StaffCategory.StoreManager, label: 'Store Manager' },
    { value: StaffCategory.StoreEmployee, label: 'Store Employee' },
  ];

  readonly statusOptions: SelectOption[] = [
    { value: 'all', label: 'All statuses' },
    ...Object.values(UserStatus).map(status => ({ value: status, label: status })),
  ];

  readonly organizationOptions = computed<SelectOption[]>(() => [
    { value: 'all', label: 'All organizations' },
    ...this.organizations().map(org => ({ value: org.id, label: org.name })),
  ]);

  readonly organizationSelectOptions = computed<SelectOption[]>(() =>
    this.organizationOptions().filter(option => option.value !== 'all')
  );

  readonly storeOptions = computed<SelectOption[]>(() => [
    { value: '', label: 'All stores' },
    ...this.stores().map(store => ({ value: store.id, label: store.name })),
  ]);

  readonly storeSelectOptions = computed<SelectOption[]>(() =>
    this.storeOptions().filter(option => option.value !== '')
  );

  readonly nonAllStatusOptions = computed<SelectOption[]>(() =>
    this.statusOptions.filter(option => option.value !== 'all')
  );

  readonly filteredUsers = computed(() => {
    const search = this.searchTerm().trim().toLowerCase();
    const selectedStatus = this.statusFilter();
    const selectedRole = this.roleFilter();
    const selectedOrganization = this.organizationFilter();

    const filtered = this.users().filter(user => {
      const matchesSearch =
        !search ||
        user.email.toLowerCase().includes(search) ||
        user.fullName.toLowerCase().includes(search) ||
        user.role.toLowerCase().includes(search);

      const matchesStatus =
        selectedStatus === 'all' || user.status === selectedStatus;

      const matchesRole =
        selectedRole === 'all' || user.role === selectedRole;

      const matchesOrg =
        selectedOrganization === 'all' || user.organizationId === selectedOrganization;

      return matchesSearch && matchesStatus && matchesRole && matchesOrg;
    });

    const currentSortBy = this.sortBy();
    const currentSortOrder = this.sortOrder();

    filtered.sort((a, b) => {
      const valueA = this.getSortValue(a, currentSortBy);
      const valueB = this.getSortValue(b, currentSortBy);

      if (valueA < valueB) {
        return currentSortOrder === 'asc' ? -1 : 1;
      }
      if (valueA > valueB) {
        return currentSortOrder === 'asc' ? 1 : -1;
      }
      return 0;
    });

    return filtered;
  });

  readonly totalPages = computed(() => {
    const total = this.filteredUsers().length;
    return Math.max(1, Math.ceil(total / this.pageSize()));
  });

  readonly paginatedUsers = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    const end = start + this.pageSize();
    return this.filteredUsers().slice(start, end);
  });

  readonly tableColumns = computed<TableColumn<User>[]>(() => [
    { key: 'fullName', label: 'User', sortable: true, formatter: (_, row) => `${row.firstName} ${row.lastName}` },
    { key: 'email', label: 'Email', sortable: true },
    { key: 'organizationId', label: 'Organization', sortable: false, formatter: (_, row) => this.getOrganizationName(row.organizationId) },
    { key: 'storeId', label: 'Store', sortable: false, formatter: (_, row) => this.getStoreName(row.storeId) },
    { key: 'role', label: 'Role', sortable: true },
    { key: 'status', label: 'Status', sortable: true, formatter: (value) => String(value) },
    {
      key: 'id',
      label: 'Actions',
      sortable: false,
      formatter: (_, row) => `${row.status === UserStatus.Active ? 'Deactivate' : 'Activate'} / View / Edit`,
    },
  ]);

  form = this.fb.nonNullable.group({
    firstName: ['', [Validators.required, Validators.minLength(2)]],
    lastName: ['', [Validators.required, Validators.minLength(2)]],
    email: ['', [Validators.required, Validators.email]],
    organizationId: ['', Validators.required],
    storeId: [''],
    storeIds: [[] as string[]],
    role: [Role.Customer, Validators.required],
    staffCategory: [null as StaffCategory | null],
    status: [UserStatus.Active, Validators.required],
    permissions: [[] as string[]],
  });

  ngOnInit(): void {
    this.form.controls.role.valueChanges.subscribe(role => this.updateStaffCategoryValidation(role));
    this.loadOrganizations();
    this.loadUsers();
    this.userMasterService.getRoles().subscribe({
      next: response => this.roleDefinitions.set(response.data ?? []),
      error: error => this.apiError.set(error?.message ?? 'Unable to load roles.'),
    });
    this.userMasterService.getPermissions().subscribe({
      error: error => this.apiError.set(error?.message ?? 'Unable to load permissions.'),
    });
  }

  onPageChange(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.totalPages()));
  }

  onSortChange(event: { sortBy: string; sortOrder: 'asc' | 'desc' }): void {
    this.sortBy.set(event.sortBy);
    this.sortOrder.set(event.sortOrder);
    this.page.set(1);
  }

  openCreateForm(): void {
    this.mode.set('create');
    this.selectedUser.set(null);
    this.form.reset({
      firstName: '',
      lastName: '',
      email: '',
      organizationId: '',
      storeId: '',
      storeIds: [],
      role: Role.Customer,
      staffCategory: null,
      status: UserStatus.Active,
      permissions: [],
    });
    this.isFormVisible.set(true);
  }

  openEditForm(user: User): void {
    this.mode.set('edit');
    this.selectedUser.set(user);
    this.form.patchValue({
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      organizationId: user.organizationId,
      storeId: user.storeId ?? '',
      storeIds: user.storeIds ?? (user.storeId ? [user.storeId] : []),
      role: user.role,
      staffCategory: user.staffCategory ?? null,
      status: user.status,
      permissions: user.permissions.map(permission => permission.code),
    });
    this.loadStores(user.organizationId);
    this.isFormVisible.set(true);
  }

  openViewForm(user: User): void {
    this.mode.set('view');
    this.selectedUser.set(user);
    this.form.patchValue({
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      organizationId: user.organizationId,
      storeId: user.storeId ?? '',
      storeIds: user.storeIds ?? (user.storeId ? [user.storeId] : []),
      role: user.role,
      staffCategory: user.staffCategory ?? null,
      status: user.status,
      permissions: user.permissions.map(permission => permission.code),
    });
    this.isFormVisible.set(true);
  }

  closeForm(): void {
    this.isFormVisible.set(false);
    this.selectedUser.set(null);
    this.form.reset();
  }

  saveUser(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();
    const payload: CreateUserRequest = {
      firstName: values.firstName.trim(),
      lastName: values.lastName.trim(),
      email: values.email.trim(),
      organizationId: values.organizationId,
      storeId: values.storeId || undefined,
      storeIds: values.storeIds,
      role: values.role,
      staffCategory: values.role === Role.StoreStaff ? values.staffCategory ?? undefined : undefined,
      status: values.status,
      permissions: values.permissions,
    };

    const selectedUser = this.selectedUser();
    const request = this.mode() === 'edit' && selectedUser
      ? (() => {
          const updateRequest: UpdateUserRequest = { ...payload };
          updateRequest.id = selectedUser.id;
          return this.userMasterService.updateUser(selectedUser.id, updateRequest);
        })()
      : this.userMasterService.createUser(payload);

    this.isLoading.set(true);
    this.apiError.set(null);

    request.subscribe({
      next: response => {
        if (response.success) {
          this.closeForm();
          this.loadUsers();
          return;
        }

        this.apiError.set(response.message ?? 'Unable to save the user.');
      },
      error: error => {
        this.apiError.set(error?.message ?? 'User management API is unavailable in the current backend.');
      },
      complete: () => {
        this.isLoading.set(false);
      },
    });
  }

  toggleUserStatus(user: User): void {
    const nextStatus = user.status === UserStatus.Active ? UserStatus.Inactive : UserStatus.Active;

    this.isLoading.set(true);
    this.userMasterService.toggleUserStatus(user.id, nextStatus).subscribe({
      next: response => {
        if (response.success) {
          this.loadUsers();
          return;
        }
        this.apiError.set(response.message ?? 'Unable to update the user status.');
      },
      error: error => {
        this.apiError.set(error?.message ?? 'User status update is unavailable until the backend contract is added.');
      },
      complete: () => {
        this.isLoading.set(false);
      },
    });
  }

  onOrganizationFilterChange(value: string): void {
    this.organizationFilter.set(value);
    this.page.set(1);
  }

  onOrganizationSelected(value: string): void {
    this.form.patchValue({ storeId: '', storeIds: [] });
    this.loadStores(value);
  }

  toggleStore(storeId: string): void {
    const selected = this.form.controls.storeIds.value ?? [];
    const next = selected.includes(storeId) ? selected.filter(id => id !== storeId) : [...selected, storeId];
    this.form.patchValue({ storeIds: next, storeId: next[0] ?? '' });
  }

  isStoreSelected(storeId: string): boolean {
    return (this.form.controls.storeIds.value ?? []).includes(storeId);
  }

  onStatusFilterChange(value: string): void {
    this.statusFilter.set(value);
    this.page.set(1);
  }

  onRoleFilterChange(value: string): void {
    this.roleFilter.set(value);
    this.page.set(1);
  }

  togglePermission(permissionCode: string): void {
    const existing = this.form.get('permissions')?.value ?? [];
    const next = existing.includes(permissionCode)
      ? existing.filter(code => code !== permissionCode)
      : [...existing, permissionCode];

    this.form.patchValue({ permissions: next });
  }

  isPermissionSelected(permissionCode: string): boolean {
    return (this.form.get('permissions')?.value ?? []).includes(permissionCode);
  }

  getFieldError(controlName: string): string | undefined {
    const control = this.form.get(controlName);
    if (!control || !control.touched || !control.invalid) {
      return undefined;
    }

    if (control.hasError('required')) {
      return 'This field is required.';
    }

    if (control.hasError('email')) {
      return 'Enter a valid email address.';
    }

    if (control.hasError('minlength')) {
      return 'Value is too short.';
    }

    return 'This field is invalid.';
  }

  private loadOrganizations(): void {
    this.userMasterService.getOrganizations().subscribe({
      next: response => {
        if (response.success && response.data) {
          this.organizations.set(response.data);
        }
      },
      error: error => {
        this.apiError.set(error?.message ?? 'Organizations API is not available yet.');
      },
    });
  }

  private loadUsers(): void {
    this.isLoading.set(true);
    this.apiError.set(null);

    this.userMasterService.getUsers().subscribe({
      next: response => {
        if (response.success && response.data) {
          this.users.set(response.data.map(user => ({
            ...user,
            fullName: `${user.firstName} ${user.lastName}`.trim(),
          })));
          return;
        }

        this.apiError.set(response.message ?? 'Unable to load users.');
      },
      error: error => {
        this.apiError.set(error?.message ?? 'User master data is unavailable until the backend exposes its user API.');
      },
      complete: () => {
        this.isLoading.set(false);
      },
    });
  }

  private loadStores(organizationId: string): void {
    if (!organizationId) {
      this.stores.set([]);
      return;
    }

    this.userMasterService.getStores(organizationId).subscribe({
      next: response => {
        if (response.success && response.data) {
          this.stores.set(response.data);
        }
      },
      error: error => {
        this.apiError.set(error?.message ?? 'Stores API is not available yet.');
      },
    });
  }

  private updateStaffCategoryValidation(role: Role): void {
    const control = this.form.controls.staffCategory;
    if (role === Role.StoreStaff) control.addValidators(Validators.required);
    else control.removeValidators(Validators.required);
    control.updateValueAndValidity({ emitEvent: false });
  }

  private getOrganizationName(organizationId: string): string {
    const organization = this.organizations().find(item => item.id === organizationId);
    return organization?.name ?? '—';
  }

  private getStoreName(storeId?: string): string {
    if (!storeId) {
      return '—';
    }

    const store = this.stores().find(item => item.id === storeId);
    return store?.name ?? '—';
  }

  private getSortValue(user: User, sortBy: string): string {
    switch (sortBy) {
      case 'fullName':
        return `${user.firstName} ${user.lastName}`.toLowerCase();
      case 'email':
        return user.email.toLowerCase();
      case 'role':
        return user.role.toLowerCase();
      case 'status':
        return user.status.toLowerCase();
      default:
        return user.fullName.toLowerCase();
    }
  }
}
