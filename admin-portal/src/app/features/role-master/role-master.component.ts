import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonComponent } from '../../shared/components/button/button.component';
import { Permission } from '../../core/models/domain.model';
import { RoleDefinition, UserMasterService } from '../../core/services/user-master.service';
import { Accent, accentFor, cycleAccent, initialsOf, permissionGroup } from '../../shared/utils/palette';

interface PermissionGroup {
  name: string;
  accent: Accent;
  items: Permission[];
  enabled: number;
}

@Component({
  selector: 'app-role-master',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent],
  template: `
    <main class="page">
      <header class="heading">
        <span class="eyebrow">ACCESS CONTROL</span>
        <h1>Role Master</h1>
        <p>Define the permissions granted to each operational role.</p>
      </header>

      <section class="workspace">
        <aside class="roles" aria-label="Roles">
          <button
            type="button"
            *ngFor="let role of roles()"
            class="role-card"
            [class.selected]="role.id === selectedRole()?.id"
            [style.background]="accentOf(role).tint"
            [style.--accent]="accentOf(role).solid"
            (click)="selectRole(role)"
          >
            <span class="badge" [style.background]="accentOf(role).solid">{{ initials(role.name) }}</span>
            <span class="meta">
              <strong>{{ role.name }}</strong>
              <small>{{ role.code }}</small>
            </span>
            <span class="count">{{ role.permissions.length }}</span>
          </button>
        </aside>

        <div class="detail" *ngIf="selectedRole() as role; else empty">
          <div class="band" [style.background]="accentOf(role).solid">
            <div class="band-text">
              <span class="band-label">ROLE</span>
              <h2>{{ role.name }}</h2>
              <p>{{ role.description || 'No description provided.' }}</p>
            </div>
            <div class="band-actions">
              <span class="dirty" *ngIf="isDirty()">● Unsaved changes</span>
              <app-button label="Save permissions" (onClick)="save()" [disabled]="isSaving() || !isDirty()" [loading]="isSaving()" />
            </div>
          </div>

          <div class="body">
            <div class="summary">
              <div class="progress">
                <div class="progress-top"><strong>{{ selectedPermissionIds().length }} of {{ permissions().length }} permissions enabled</strong><span>{{ percent() }}%</span></div>
                <div class="track"><div class="fill" [style.width.%]="percent()" [style.background]="accentOf(role).solid"></div></div>
              </div>
              <div class="search">
                <span aria-hidden="true">⌕</span>
                <input type="text" placeholder="Filter permissions" [ngModel]="query()" (ngModelChange)="query.set($event)" />
              </div>
            </div>

            <section class="group" *ngFor="let group of groups()">
              <header [style.background]="group.accent.solid">
                <h3>{{ group.name }}</h3>
                <span class="group-count">{{ group.enabled }}/{{ group.items.length }}</span>
                <span class="spacer"></span>
                <button type="button" (click)="setGroup(group, true)">Select all</button>
                <button type="button" (click)="setGroup(group, false)">Clear</button>
              </header>
              <label class="permission" *ngFor="let permission of group.items" [style.background]="isSelected(permission.id) ? group.accent.tint : '#EAE4FF'">
                <span class="info">
                  <strong>{{ permission.name }}</strong>
                  <small><code>{{ permission.code }}</code> · {{ permission.description }}</small>
                </span>
                <input type="checkbox" role="switch" [checked]="isSelected(permission.id)" (change)="toggle(permission.id)" [style.--on]="group.accent.solid">
              </label>
            </section>

            <p class="none" *ngIf="!groups().length">No permissions match "{{ query() }}".</p>
            <p class="error" *ngIf="error()">{{ error() }}</p>
          </div>
        </div>
        <ng-template #empty><div class="empty">Select a role to manage its permissions.</div></ng-template>
      </section>
    </main>
  `,
  styles: [`
    :host { display: block; color: #141118; font-family: 'Inter', sans-serif; }
    .page { max-width: 1180px; margin: auto; }
    .eyebrow { display: inline-block; background: #FB1A8E; color: #141118; font-size: 10px; font-weight: 800; letter-spacing: 1.5px; padding: 4px 10px; border-radius: 999px; }
    h1 { margin: 10px 0 6px; font: 800 32px 'Space Grotesk', sans-serif; letter-spacing: -1px; color: #fff; }
    .heading p { margin: 0; color: #E9E4FF; font-size: 12px; }

    .workspace { display: grid; grid-template-columns: 280px 1fr; gap: 18px; margin-top: 26px; align-items: start; }

    .roles { display: grid; gap: 10px; position: sticky; top: 0; }
    .role-card { display: flex; align-items: center; gap: 12px; width: 100%; text-align: left; border: 3px solid transparent; border-radius: 24px; padding: 12px 14px; cursor: pointer; color: #141118; font-family: inherit; transition: transform .15s, border-color .15s, box-shadow .15s; }
    .role-card:hover { transform: translateX(3px); }
    .role-card.selected { border-color: var(--accent); box-shadow: 0 12px 32px rgba(20,17,24,.25); transform: translateX(6px); }
    .badge { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 16px; font: 800 13px 'Space Grotesk', sans-serif; flex-shrink: 0; }
    .meta { flex: 1; min-width: 0; display: grid; gap: 2px; }
    .meta strong { font-weight: 800; font-size: 13px; }
    small { color: #5E5A66; font-size: 11px; }
    .count { background: #EAE4FF; border-radius: 999px; padding: 2px 10px; font-size: 11px; font-weight: 800; }

    .detail { background: #EAE4FF; border-radius: 24px; overflow: hidden; }
    .band { display: flex; justify-content: space-between; gap: 16px; align-items: flex-start; padding: 22px 26px; }
    .band-label { font-size: 10px; font-weight: 800; letter-spacing: 1.5px; opacity: .7; }
    .band h2 { margin: 4px 0; font: 800 26px 'Space Grotesk', sans-serif; letter-spacing: -.8px; }
    .band p { margin: 0; font-size: 12px; opacity: .8; max-width: 52ch; }
    .band-actions { display: flex; align-items: center; gap: 12px; flex-shrink: 0; }
    .dirty { background: #FFF1A8; border-radius: 999px; padding: 4px 10px; font-size: 10px; font-weight: 800; }

    .body { padding: 22px 26px 26px; display: grid; gap: 16px; }
    .summary { display: flex; gap: 18px; align-items: center; flex-wrap: wrap; }
    .progress { flex: 1; min-width: 240px; display: grid; gap: 8px; }
    .progress-top { display: flex; justify-content: space-between; font-size: 12px; }
    .track { height: 10px; background: rgba(20,17,24,.12); border-radius: 999px; overflow: hidden; }
    .fill { height: 100%; border-radius: 999px; transition: width .25s ease; }
    .search { display: flex; align-items: center; gap: 8px; background: #EAE4FF; border: 2px solid #FFD400; border-radius: 14px; padding: 0 14px; color: #141118; min-width: 220px; }
    .search input { flex: 1; min-width: 0; background: transparent; border: 0; color: #141118; padding: 10px 0; font: 12px 'Inter', sans-serif; }
    .search input:focus { outline: none; }
    .search:focus-within { box-shadow: 0 0 0 3px rgba(255,212,0,.3); }

    .group { display: grid; gap: 8px; }
    .group header { display: flex; align-items: center; gap: 10px; padding: 8px 8px 8px 16px; border-radius: 999px; }
    .group h3 { margin: 0; font: 800 13px 'Space Grotesk', sans-serif; text-transform: capitalize; }
    .group-count { background: #EAE4FF; border-radius: 999px; padding: 1px 9px; font-size: 11px; font-weight: 800; }
    .spacer { flex: 1; }
    .group header button { border: 0; background: #EAE4FF; color: #141118; border-radius: 999px; padding: 5px 12px; font: 800 10px 'Inter', sans-serif; cursor: pointer; }
    .group header button:hover { background: #FFF1A8; }

    .permission { display: flex; align-items: center; justify-content: space-between; gap: 14px; padding: 12px 16px; border-radius: 20px; cursor: pointer; transition: background .2s; }
    .info { display: grid; gap: 3px; min-width: 0; }
    .info strong { font-size: 12px; font-weight: 800; }
    code { font: 600 11px ui-monospace, Consolas, monospace; color: #141118; }

    input[type=checkbox] { appearance: none; flex-shrink: 0; width: 42px; height: 24px; border-radius: 999px; background: #B9A8FF; position: relative; cursor: pointer; transition: background .2s; margin: 0; }
    input[type=checkbox]::after { content: ''; position: absolute; top: 3px; left: 3px; width: 18px; height: 18px; border-radius: 50%; background: #EAE4FF; transition: transform .2s; }
    input[type=checkbox]:checked { background: var(--on, #02F34C); }
    input[type=checkbox]:checked::after { transform: translateX(18px); background: #fff; }
    input[type=checkbox]:focus-visible { outline: 3px solid #141118; outline-offset: 2px; }

    .none, .empty { color: #5E5A66; text-align: center; padding: 24px; margin: 0; }
    .empty { background: #EAE4FF; border-radius: 24px; padding: 48px; }
    .error { background: #FFC6E2; border-radius: 16px; padding: 10px 14px; font-size: 12px; font-weight: 700; margin: 0; }

    @media (max-width: 860px) {
      .workspace { grid-template-columns: 1fr; }
      .roles { position: static; grid-auto-flow: column; grid-auto-columns: minmax(220px, 1fr); overflow-x: auto; padding-bottom: 6px; }
      .role-card.selected, .role-card:hover { transform: none; }
      .band { flex-direction: column; }
    }
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
  readonly query = signal('');

  readonly percent = computed(() => {
    const total = this.permissions().length;
    return total ? Math.round((this.selectedPermissionIds().length / total) * 100) : 0;
  });

  readonly isDirty = computed(() => {
    const role = this.selectedRole();
    if (!role) return false;
    const saved = new Set(role.permissions.map(permission => permission.id));
    const current = this.selectedPermissionIds();
    return saved.size !== current.length || current.some(id => !saved.has(id));
  });

  readonly groups = computed<PermissionGroup[]>(() => {
    const needle = this.query().trim().toLowerCase();
    const selected = new Set(this.selectedPermissionIds());
    const byGroup = new Map<string, Permission[]>();
    for (const permission of this.permissions()) {
      if (needle && !`${permission.name} ${permission.code} ${permission.description}`.toLowerCase().includes(needle)) continue;
      const name = permissionGroup(permission.code);
      byGroup.set(name, [...(byGroup.get(name) ?? []), permission]);
    }
    return [...byGroup.entries()].map(([name, items]) => ({
      name,
      items,
      accent: accentFor(name),
      enabled: items.filter(item => selected.has(item.id)).length,
    }));
  });

  ngOnInit(): void {
    this.service.getRoles().subscribe({ next: response => { this.roles.set(response.data ?? []); this.selectRole(this.roles()[0]); }, error: error => this.error.set(error.message) });
    this.service.getPermissions().subscribe({ next: response => this.permissions.set(response.data ?? []), error: error => this.error.set(error.message) });
  }

  accentOf(role: RoleDefinition): Accent { return cycleAccent(this.roles().findIndex(item => item.id === role.id)); }
  initials(name: string): string { return initialsOf(name); }

  selectRole(role: RoleDefinition | undefined): void {
    if (!role) return;
    this.selectedRole.set(role);
    this.selectedPermissionIds.set(role.permissions.map(permission => permission.id));
    this.error.set(null);
  }

  isSelected(permissionId: string): boolean { return this.selectedPermissionIds().includes(permissionId); }

  toggle(permissionId: string): void {
    const current = this.selectedPermissionIds();
    this.selectedPermissionIds.set(this.isSelected(permissionId) ? current.filter(id => id !== permissionId) : [...current, permissionId]);
  }

  setGroup(group: PermissionGroup, enabled: boolean): void {
    const ids = new Set(group.items.map(item => item.id));
    const rest = this.selectedPermissionIds().filter(id => !ids.has(id));
    this.selectedPermissionIds.set(enabled ? [...rest, ...ids] : rest);
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
