import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Permission } from '../../core/models/domain.model';
import { UserMasterService } from '../../core/services/user-master.service';
import { Accent, accentFor, permissionGroup } from '../../shared/utils/palette';

interface GroupChip { name: string; count: number; accent: Accent; }

@Component({
  selector: 'app-permission-master',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <main class="page">
      <header class="heading">
        <span class="eyebrow">ACCESS CONTROL</span>
        <h1>Permission Master</h1>
        <p>Server-defined capabilities available to organization roles.</p>
      </header>

      <div class="stats">
        <article class="stat yellow"><span class="icon">⊙</span><div><p>Permissions</p><strong>{{ permissions().length }}</strong></div></article>
        <article class="stat green"><span class="icon">✓</span><div><p>Active</p><strong>{{ activeCount() }}</strong></div></article>
        <article class="stat lavender"><span class="icon">⏻</span><div><p>Inactive</p><strong>{{ permissions().length - activeCount() }}</strong></div></article>
        <article class="stat pink"><span class="icon">▦</span><div><p>Groups</p><strong>{{ chips().length }}</strong></div></article>
      </div>

      <div class="controls">
        <div class="search">
          <span aria-hidden="true">⌕</span>
          <input type="text" placeholder="Search by name, code or description" [ngModel]="query()" (ngModelChange)="query.set($event)" />
        </div>
        <div class="chips" role="tablist" aria-label="Filter by group">
          <button type="button" class="chip all" [class.active]="group() === 'all'" (click)="group.set('all')">All <b>{{ permissions().length }}</b></button>
          <button type="button" class="chip" *ngFor="let chip of chips()" [class.active]="group() === chip.name" [style.--solid]="chip.accent.solid" [style.--tint]="chip.accent.tint" (click)="group.set(chip.name)">
            {{ chip.name }} <b>{{ chip.count }}</b>
          </button>
        </div>
      </div>

      <section class="grid">
        <article class="card" *ngFor="let permission of filtered()">
          <header>
            <span class="group">{{ groupOf(permission) }}</span>
            <span class="state" [class.on]="permission.isActive" [class.off]="!permission.isActive">{{ permission.isActive ? 'Active' : 'Inactive' }}</span>
          </header>
          <div class="content">
            <code>{{ permission.code }}</code>
            <h2>{{ permission.name }}</h2>
            <p>{{ permission.description }}</p>
          </div>
        </article>
      </section>

      <p class="none" *ngIf="!filtered().length && !error()">No permissions match your filters.</p>
      <p class="error" *ngIf="error()">{{ error() }}</p>
    </main>
  `,
  styles: [`
    :host { display: block; color: #141118; font-family: 'Inter', sans-serif; }
    .page { max-width: 1180px; margin: auto; display: grid; gap: 18px; }
    .eyebrow { display: inline-block; background: #B9A8FF; color: #141118; font-size: 10px; font-weight: 800; letter-spacing: 1.5px; padding: 4px 10px; border-radius: 999px; }
    h1 { margin: 10px 0 6px; font: 800 32px 'Space Grotesk', sans-serif; letter-spacing: -1px; color: #fff; }
    .heading p { margin: 0; color: #E9E4FF; font-size: 12px; }

    .stats { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; }
    .stat { border-radius: 24px; padding: 16px 18px; display: flex; gap: 12px; align-items: center; }
    .stat.yellow { background: #FFF1A8; } .stat.green { background: #B9FFCF; } .stat.pink { background: #FFC6E2; } .stat.lavender { background: #EAE4FF; }
    .stat p { margin: 0 0 4px; font-size: 11px; font-weight: 600; color: rgba(20,17,24,.7); }
    .stat strong { font: 800 24px 'Space Grotesk', sans-serif; letter-spacing: -.8px; }
    .icon { display: grid; place-items: center; width: 36px; height: 36px; border-radius: 14px; font-weight: 800; }
    .yellow .icon { background: #FFD400; } .green .icon { background: #02F34C; } .pink .icon { background: #FB1A8E; } .lavender .icon { background: #B9A8FF; }

    .controls { display: grid; gap: 12px; }
    .search { display: flex; align-items: center; gap: 8px; background: #EAE4FF; border: 2px solid #FFD400; border-radius: 14px; padding: 0 14px; color: #141118; max-width: 460px; }
    .search input { flex: 1; min-width: 0; background: transparent; border: 0; color: #141118; padding: 12px 0; font: 12px 'Inter', sans-serif; }
    .search input::placeholder { color: #5E5A66; }
    .search input:focus { outline: none; }
    .search:focus-within { box-shadow: 0 0 0 3px rgba(255,212,0,.3); }

    .chips { display: flex; flex-wrap: wrap; gap: 8px; }
    .chip { border: 0; border-radius: 999px; padding: 7px 14px; font: 800 11px 'Inter', sans-serif; text-transform: capitalize; cursor: pointer; background: var(--tint, #EAE4FF); color: #141118; transition: transform .15s, background .15s; }
    .chip b { background: #EAE4FF; border-radius: 999px; padding: 0 7px; margin-left: 4px; font-size: 10px; }
    .chip:hover { transform: translateY(-1px); }
    .chip.active { background: var(--solid, #FFD400); box-shadow: 0 8px 20px rgba(20,17,24,.22); }
    .chip.all.active { background: #FFD400; }

    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 12px; }
    .card { border-radius: 24px; overflow: hidden; transition: transform .15s, box-shadow .15s; }
    
    .grid .card:nth-child(4n+1){background:#FFF1A8}
    .grid .card:nth-child(4n+2){background:#B9FFCF}
    .grid .card:nth-child(4n+3){background:#EAE4FF}
    .grid .card:nth-child(4n+4){background:#FFC6E2}
    .grid .card:nth-child(4n+1) header{background:#FFD400}
    .grid .card:nth-child(4n+2) header{background:#02F34C}
    .grid .card:nth-child(4n+3) header{background:#B9A8FF}
    .grid .card:nth-child(4n+4) header{background:#FB1A8E}
    .card:hover { transform: translateY(-3px); box-shadow: 0 12px 32px rgba(20,17,24,.25); }
    .card header { display: flex; justify-content: space-between; align-items: center; padding: 10px 16px; }
    .group { font: 800 11px 'Inter', sans-serif; letter-spacing: 1px; text-transform: uppercase; }
    .state { border-radius: 999px; padding: 2px 10px; font-size: 10px; font-weight: 800; }
    .state.on { background: #B9FFCF; } .state.off { background: #FFC6E2; }
    .content { padding: 16px 18px 20px; display: grid; gap: 8px; }
    code { justify-self: start; background: #EAE4FF; border-radius: 8px; padding: 2px 8px; font: 700 11px ui-monospace, Consolas, monospace; }
    h2 { margin: 4px 0 0; font: 800 17px 'Space Grotesk', sans-serif; letter-spacing: -.4px; }
    .content p { margin: 0; color: #5E5A66; font-size: 12px; line-height: 1.5; }

    .none { color: #E9E4FF; text-align: center; padding: 28px; margin: 0; }
    .error { background: #FFC6E2; border-radius: 16px; padding: 10px 14px; font-size: 12px; font-weight: 700; margin: 0; }

    @media (max-width: 860px) { .stats { grid-template-columns: repeat(2, 1fr); } }
    @media (max-width: 520px) { .stats { grid-template-columns: 1fr; } }
  `],
})
export class PermissionMasterComponent implements OnInit {
  private readonly service = inject(UserMasterService);
  readonly permissions = signal<Permission[]>([]);
  readonly error = signal<string | null>(null);
  readonly query = signal('');
  readonly group = signal('all');

  readonly activeCount = computed(() => this.permissions().filter(permission => permission.isActive).length);

  readonly chips = computed<GroupChip[]>(() => {
    const counts = new Map<string, number>();
    for (const permission of this.permissions()) {
      const name = permissionGroup(permission.code);
      counts.set(name, (counts.get(name) ?? 0) + 1);
    }
    return [...counts.entries()].map(([name, count]) => ({ name, count, accent: accentFor(name) }));
  });

  readonly filtered = computed(() => {
    const needle = this.query().trim().toLowerCase();
    const group = this.group();
    return this.permissions().filter(permission =>
      (group === 'all' || permissionGroup(permission.code) === group) &&
      (!needle || `${permission.name} ${permission.code} ${permission.description}`.toLowerCase().includes(needle)));
  });

  ngOnInit(): void {
    this.service.getPermissions().subscribe({ next: response => this.permissions.set(response.data ?? []), error: error => this.error.set(error.message) });
  }

  groupOf(permission: Permission): string { return permissionGroup(permission.code); }
  accentOf(permission: Permission): Accent { return accentFor(permissionGroup(permission.code)); }
}
