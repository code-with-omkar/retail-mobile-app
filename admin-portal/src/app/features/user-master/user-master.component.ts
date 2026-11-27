import { AuthService } from '../../core/services/auth.service';
import { FilterLayoutComponent } from '../../shared/components/filter-layout/filter-layout.component';
import { FilterGroup, Selection, filterState } from '../../shared/components/filter-layout/filter-state';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonComponent } from '../../shared/components/button/button.component';
import { InputComponent } from '../../shared/components/input/input.component';
import { SelectComponent, SelectOption } from '../../shared/components/select/select.component';
import { CellDirective, DataColumn, DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { DEFAULT_PAGE_SIZE, PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { SidePanelComponent } from '../../shared/components/side-panel/side-panel.component';
import { Accent, accentFor, initialsOf, roleAccent } from '../../shared/utils/palette';
import { permissionGroup } from '../../shared/utils/palette';
import { Organization, Permission, Role, StaffCategory, Store, User, UserStatus } from '../../core/models/domain.model';
import { UserMasterService, CreateUserRequest, UpdateUserRequest } from '../../core/services/user-master.service';

@Component({
  selector: 'app-user-master',
  standalone: true,
  imports: [CommonModule,
    ReactiveFormsModule,
    ButtonComponent,
    InputComponent,
    SelectComponent,
    DataTableComponent,
    CellDirective,
    PaginationComponent,
    SidePanelComponent, FilterLayoutComponent],
  templateUrl: './user-master.component.html',
  styleUrls: ['./user-master.component.css'],
})
export class UserMasterComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly userMasterService = inject(UserMasterService);

  private readonly auth = inject(AuthService, { optional: true });
  /** A temporary password just made, shown until the panel is closed. */
  readonly temporary = signal<{ name: string; email: string; password: string } | null>(null);
  readonly copied = signal(false);

  /** Staff and delivery partners who are active; never customers, and never yourself (use Change password). */
  canGivePassword(user: User): boolean { return user.role !== Role.Customer && user.status === UserStatus.Active && !!user.email && user.id !== this.auth?.getUserId?.(); }

  givePassword(user: User): void {
    if (!window.confirm(`Give ${user.firstName} ${user.lastName} a new temporary password? Their current password stops working.`)) return;
    this.makePassword(user);
  }

  private makePassword(user: User): void {
    this.apiError.set(null);
    this.userMasterService.setTemporaryPassword(user.id).subscribe({
      next: response => {
        if (!response.data) { this.apiError.set(response.message ?? 'Could not make a temporary password.'); return; }
        this.copied.set(false);
        this.changedId.set(user.id);
        this.temporary.set({ name: `${user.firstName} ${user.lastName}`.trim(), email: user.email, password: response.data.temporaryPassword });
      },
      error: error => this.apiError.set(error?.error?.message ?? error?.message ?? 'Could not make a temporary password.'),
    });
  }

  copyPassword(): void {
    const made = this.temporary();
    if (made) navigator.clipboard?.writeText(made.password).then(() => this.copied.set(true), () => {});
  }

  readonly users = signal<User[]>([]);
  readonly organizations = signal<Organization[]>([]);
  readonly stores = signal<Store[]>([]);
  readonly isLoading = signal(false);
  readonly apiError = signal<string | null>(null);
  readonly selectedUser = signal<User | null>(null);
  readonly isFormVisible = signal(false);
  readonly mode = signal<'create' | 'edit' | 'view'>('create');
  readonly searchTerm = signal('');
  readonly filters = filterState('users', { sort: ['name'] });
  private readonly roleCounts = signal<Record<string, number> | null>(null);
  private readonly statusCounts = signal<{ active: number; inactive: number } | null>(null);
  readonly organizationFilter = signal('all');
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  readonly total = signal(0);
  readonly allCount = signal(0);
  readonly activeCount = signal(0);
  readonly roleCount = signal(0);
  /** The user just opened or saved, marked in the list. */
  readonly changedId = signal<string | null>(null);
  private asked = 0;

  readonly columns: DataColumn[] = [
    { key: 'user', label: 'User' }, { key: 'org', label: 'Organization and store' }, { key: 'role', label: 'Role' },
    { key: 'status', label: 'Status' }, { key: 'actions', label: 'Actions' },
  ];
  readonly sortOptions: SelectOption[] = [
    { value: 'name', label: 'Name A to Z' }, { value: 'name-desc', label: 'Name Z to A' }, { value: 'email', label: 'Email' }, { value: 'newest', label: 'Newest first' },
  ];

  readonly permissionCatalog = this.userMasterService.permissions;
  readonly roleDefinitions = signal<{ id: string; code: string; name: string; description?: string; isActive: boolean; permissions: Permission[] }[]>([]);
  readonly effectiveRolePermissions = computed(() => this.roleDefinitions().find(role => role.code === this.form.controls.role.value)?.permissions ?? []);

  readonly roleOptions = computed<SelectOption[]>(() => {
    const options: SelectOption[] = this.roleDefinitions().map(role => ({ value: role.code, label: role.name }));
    // An organization administrator has no role code of their own; keep their role selectable so saving does not change it.
    return this.selectedUser()?.role === Role.Admin ? [...options, { value: Role.Admin, label: 'Administrator' }] : options;
  });
  readonly groups = computed<FilterGroup[]>(() => {
    const roles = this.roleCounts(), statuses = this.statusCounts();
    return [
      { key: 'role', label: 'Role', options: this.roleDefinitions().map(role => ({ value: role.code, label: role.name, count: roles ? roles[role.code] ?? 0 : undefined })) },
      { key: 'status', label: 'Status', options: [
        { value: UserStatus.Active, label: 'Active', count: statuses?.active },
        { value: UserStatus.Inactive, label: 'Inactive', count: statuses?.inactive },
      ] },
      { key: 'sort', label: 'Sort by', single: true, required: true, options: this.sortOptions },
    ];
  });
  onFilters(next: Selection): void { this.filters.set(next); this.filter(); }
  clearFilters(): void { this.filters.clear(); this.searchTerm.set(''); this.filter(); }
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

  goTo(page: number): void {
    this.page.set(page);
    this.changedId.set(null);
    this.loadUsers();
  }

  resize(size: number): void {
    this.pageSize.set(size);
  }

  /** A new search, filter or order starts at the first page. */
  filter(): void {
    this.goTo(1);
  }

  isActive(user: User): boolean {
    return user.status === UserStatus.Active;
  }

  initials(user: User): string {
    return initialsOf(`${user.firstName} ${user.lastName}`);
  }

  avatarAccent(user: User): Accent {
    return accentFor(user.email || user.id);
  }

  roleAccent(role: string): Accent {
    return roleAccent(role);
  }

  roleLabel(code: string): string {
    return this.roleDefinitions().find(role => role.code === code)?.name ?? code;
  }

  permAccent(permission: Permission): Accent {
    return accentFor(permissionGroup(permission.code));
  }

  openCreateForm(): void {
    this.mode.set('create');
    this.selectedUser.set(null);
    this.form.reset({
      firstName: '',
      lastName: '',
      email: '',
      // the only organization there is, when there is only one
      organizationId: this.organizations().length === 1 ? this.organizations()[0].id : '',
      storeId: '',
      storeIds: [],
      role: Role.Customer,
      staffCategory: null,
      status: UserStatus.Active,
      permissions: [],
    });
    if (this.form.controls.organizationId.value) this.loadStores(this.form.controls.organizationId.value);
    this.apiError.set(null);
    this.isFormVisible.set(true);
  }

  openEditForm(user: User): void {
    this.mode.set('edit');
    this.selectedUser.set(user);
    this.changedId.set(user.id);
    this.apiError.set(null);
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
    this.changedId.set(user.id);
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
      // Only store staff have the list of stores; anyone else is assigned to the primary store alone.
      storeIds: values.role === Role.StoreStaff ? values.storeIds : values.storeId ? [values.storeId] : [],
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
    const isNew = !(this.mode() === 'edit' && selectedUser);

    request.subscribe({
      next: response => {
        if (response.success) {
          this.changedId.set(response.data?.id ?? selectedUser?.id ?? null);
          this.closeForm();
          this.loadUsers();
          // A new member of staff cannot sign in without a password, so one is made at once.
          if (isNew && values.role !== Role.Customer && values.status === UserStatus.Active && response.data?.id) {
            this.makePassword({ ...response.data, firstName: payload.firstName, lastName: payload.lastName, email: payload.email });
          }
          return;
        }

        this.apiError.set(response.message ?? 'Unable to save the user.');
      },
      error: error => {
        this.apiError.set(error?.message ?? 'User management API is unavailable in the current backend.');
        this.isLoading.set(false);
      },
      complete: () => {
        this.isLoading.set(false);
      },
    });
  }

  toggleUserStatus(user: User): void {
    const nextStatus = user.status === UserStatus.Active ? UserStatus.Inactive : UserStatus.Active;

    this.isLoading.set(true);
    this.changedId.set(user.id);
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
        this.isLoading.set(false);
      },
      complete: () => {
        this.isLoading.set(false);
      },
    });
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
          // with one organization its stores are read at once, so the list can name each person's store
          if (response.data.length === 1) this.loadStores(response.data[0].id);
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
    const request = ++this.asked;

    this.userMasterService.getUsersPage({ search: this.searchTerm().trim(), roles: this.filters.of('role'), statuses: this.filters.of('status'), sort: this.filters.of('sort')[0] ?? 'name', page: this.page(), pageSize: this.pageSize() }).subscribe({
      next: response => {
        if (request !== this.asked) return; // an older answer arriving late
        this.isLoading.set(false);
        if (response.success && response.data) {
          this.users.set(response.data.items.map(user => ({ ...user, fullName: `${user.firstName} ${user.lastName}`.trim() })));
          this.total.set(response.data.totalCount);
          this.allCount.set(response.data.allCount);
          this.activeCount.set(response.data.activeCount);
          this.roleCount.set(response.data.roleCount);
          this.roleCounts.set(response.data.roleCounts ?? null);
          this.statusCounts.set(response.data.roleCounts ? { active: response.data.matchingActive ?? 0, inactive: response.data.matchingInactive ?? 0 } : null);
          return;
        }

        this.apiError.set(response.message ?? 'Unable to load users.');
      },
      error: error => {
        if (request !== this.asked) return;
        this.apiError.set(error?.message ?? 'Unable to load users.');
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

  getOrganizationName(organizationId: string): string {
    const organization = this.organizations().find(item => item.id === organizationId);
    return organization?.name ?? '—';
  }

  getStoreName(storeId?: string): string {
    if (!storeId) {
      return '—';
    }

    const store = this.stores().find(item => item.id === storeId);
    return store?.name ?? '—';
  }

}
