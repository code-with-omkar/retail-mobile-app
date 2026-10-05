import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonComponent } from '../../shared/components/button/button.component';
import { Permission } from '../../core/models/domain.model';
import { RoleDefinition, UserMasterService } from '../../core/services/user-master.service';

@Component({
  selector: 'app-role-master',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent],
  template: `
    <main class="page">
      <header class="heading"><div><span class="eyebrow">ACCESS CONTROL</span><h1>Role Master</h1><p>Define the permissions granted to each operational role.</p></div></header>
      <section class="workspace">
        <aside class="roles"><button *ngFor="let role of roles()" [class.selected]="role.id === selectedRole()?.id" (click)="selectRole(role)">{{ role.name }}<small>{{ role.code }}</small></button></aside>
        <div class="permissions" *ngIf="selectedRole() as role; else empty">
          <div class="panel-heading"><div><h2>{{ role.name }}</h2><p>{{ role.description }}</p></div><app-button label="Save permissions" (onClick)="save()" [disabled]="isSaving()" /></div>
          <label class="permission" *ngFor="let permission of permissions()"><input type="checkbox" [checked]="isSelected(permission.id)" (change)="toggle(permission.id)"><span><strong>{{ permission.name }}</strong><small>{{ permission.code }} · {{ permission.description }}</small></span></label>
          <p class="error" *ngIf="error()">{{ error() }}</p>
        </div>
        <ng-template #empty><div class="empty">Select a role to manage its permissions.</div></ng-template>
      </section>
    </main>
  `,
  styles: [`
    :host { display:block; color:#25322d; } .page { padding:32px; max-width:1120px; margin:auto; } .eyebrow { color:#b45f32; font-size:11px; letter-spacing:2px; font-weight:700; } h1 { margin:8px 0; font:700 38px Georgia,serif; } p { color:#708078; } .workspace { display:grid; grid-template-columns:240px 1fr; gap:18px; margin-top:28px; } .roles,.permissions { border:1px solid #dce5df; background:#fffdf8; } .roles { padding:10px; } .roles button { display:block; width:100%; text-align:left; border:0; background:transparent; padding:15px; color:#50615a; cursor:pointer; } .roles button.selected { background:#e8f0e8; color:#1c493b; } small { display:block; margin-top:5px; color:#88978f; font-size:12px; } .permissions { padding:24px; } .panel-heading { display:flex; justify-content:space-between; gap:16px; align-items:start; border-bottom:1px solid #e5ebe6; padding-bottom:18px; } h2 { margin:0; font:700 24px Georgia,serif; } .permission { display:flex; gap:12px; padding:17px 0; border-bottom:1px solid #edf1ee; cursor:pointer; } input { accent-color:#b45f32; width:18px; height:18px; } .error { color:#ad3c32; } .empty { padding:48px; color:#708078; } @media (max-width:700px) { .page { padding:20px; } .workspace { grid-template-columns:1fr; } .panel-heading { flex-direction:column; } }
  `],
})
export class RoleMasterComponent implements OnInit {
  private readonly service = inject(UserMasterService);
  readonly roles = signal<RoleDefinition[]>([]);
  readonly permissions = signal<Permission[]>([]);
  readonly selectedRole = signal<RoleDefinition | null>(null);
  readonly selectedPermissionIds = signal<string[]>([]);
  readonly isSaving = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.service.getRoles().subscribe({ next: response => { this.roles.set(response.data ?? []); this.selectRole(this.roles()[0]); }, error: error => this.error.set(error.message) });
    this.service.getPermissions().subscribe({ next: response => this.permissions.set(response.data ?? []), error: error => this.error.set(error.message) });
  }

  selectRole(role: RoleDefinition | undefined): void {
    if (!role) return;
    this.selectedRole.set(role);
    this.selectedPermissionIds.set(role.permissions.map(permission => permission.id));
  }

  isSelected(permissionId: string): boolean { return this.selectedPermissionIds().includes(permissionId); }

  toggle(permissionId: string): void {
    const current = this.selectedPermissionIds();
    this.selectedPermissionIds.set(this.isSelected(permissionId) ? current.filter(id => id !== permissionId) : [...current, permissionId]);
  }

  save(): void {
    const role = this.selectedRole();
    if (!role) return;
    this.isSaving.set(true);
    this.service.updateRolePermissions(role.id, this.selectedPermissionIds()).subscribe({
      next: response => { if (response.data) { this.roles.update(roles => roles.map(item => item.id === role.id ? response.data! : item)); this.selectedRole.set(response.data); } this.isSaving.set(false); },
      error: error => { this.error.set(error.message); this.isSaving.set(false); },
    });
  }
}