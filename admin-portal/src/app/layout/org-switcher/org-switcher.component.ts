import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Role } from '../../core/models/domain.model';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { StorageService } from '../../core/services/storage.service';

interface OrganizationChoice { id: string; name: string; code?: string | null; }
interface OrganizationList { homeOrganizationId: string; currentOrganizationId: string; organizations: OrganizationChoice[]; }

/**
 * Lets a platform administrator look at another organization. The choice is sent with every request (X-Organization-Id);
 * the server shows that organization's data and refuses changes there, so a banner says so. Nobody else sees this.
 */
@Component({
  selector: 'app-org-switcher',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="org" *ngIf="visible() || error()">
      <label *ngIf="visible()">Organization
        <select [ngModel]="current()" (ngModelChange)="choose($event)" name="organization" [disabled]="busy()">
          <option *ngFor="let o of organizations()" [value]="o.id">{{ o.name }}{{ o.id === home() ? ' (yours)' : '' }}</option>
        </select>
      </label>
      <span class="readonly" *ngIf="isOther()" role="status">Read-only: you are looking at {{ currentName() }}. Switch back to your own organization to make changes.</span>
      <span class="error" *ngIf="error()" role="alert">{{ error() }}</span>
    </div>
  `,
  styles: [`
    .org { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-bottom: 14px; color:var(--ink); font-size: 13px; font-weight: 700; }
    select { margin-left: 8px; padding: 6px 10px; border-radius: 10px; border: 0; font-weight: 700; }
    .readonly { background: #FFD400; color: #141118; padding: 6px 12px; border-radius: 12px; }
    .error { background: #FFE1E1; color: #7A0010; padding: 6px 12px; border-radius: 12px; }
  `],
})
export class OrgSwitcherComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly storage = inject(StorageService);

  readonly organizations = signal<OrganizationChoice[]>([]);
  readonly home = signal<string | null>(null);
  readonly current = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly visible = computed(() => this.organizations().length > 1);
  readonly isOther = computed(() => !!this.current() && this.current() !== this.home());
  readonly currentName = computed(() => this.organizations().find(o => o.id === this.current())?.name ?? '');

  ngOnInit(): void {
    if (!this.auth.hasRole(Role.ApplicationAdmin)) return;
    this.api.get<OrganizationList>('/admin/organizations').subscribe({
      next: r => {
        this.organizations.set(r.data?.organizations ?? []);
        this.home.set(r.data?.homeOrganizationId ?? null);
        this.current.set(r.data?.currentOrganizationId ?? null);
      },
      error: e => {
        // A remembered choice that is no longer allowed (or closed) is dropped so the portal is usable again.
        this.storage.removeOrganization();
        this.error.set(e?.message ?? 'Could not load the organizations.');
      },
    });
  }

  choose(id: string): void {
    if (!id || id === this.current()) return;
    if (id === this.home()) this.storage.removeOrganization(); else this.storage.setOrganization(id);
    this.current.set(id);
    this.reload();
  }

  /** Everything on screen belongs to the previous organization, so the portal starts over. Replaced in tests. */
  reload(): void {
    if (typeof window !== 'undefined') window.location.reload();
  }
}
