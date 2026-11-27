import { RouterLink } from '@angular/router';
import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';
import { ApiService } from '../../core/services/api.service';
import { Role } from '../../core/models/domain.model';
import { initialsOf } from '../../shared/utils/palette';
import { iconUrl } from '../../layout/shell/decorate';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <section class="profile page">
      <div><h2>My profile</h2><p class="muted">Your account in this portal.</p></div>

      <section class="slab who">
        <span class="photo">{{ initials() }}</span>
        <div class="name">
          <h3>{{ auth.userContext()?.displayName || 'Profile' }}</h3>
          <div class="tags">
            <span class="tag"><i [style.--ic]="icon('shield')" aria-hidden="true"></i>{{ auth.userContext()?.role || 'User' }}</span>
            <span class="tag on"><i [style.--ic]="icon('circleCheck')" aria-hidden="true"></i>Signed in</span>
          </div>
        </div>
      </section>

      <div class="boxes">
        <div class="box" *ngFor="let d of details(); trackBy: byLabel" [attr.data-kind]="d.kind">
          <span class="chip" aria-hidden="true"><i [style.--ic]="icon(d.kind)"></i></span>
          <div><span class="label">{{ d.label }}</span><strong>{{ d.value }}</strong></div>
        </div>
      </div>

      <section class="slab security">
        <span class="chip" aria-hidden="true"><i [style.--ic]="icon('key')"></i></span>
        <div><h4>Password</h4><p>Change the password you sign in with.</p></div>
        <a class="act" routerLink="/change-password">Change password</a>
      </section>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Plus Jakarta Sans',sans-serif;color:var(--ink)}
    .profile{display:flex;flex-direction:column;gap:14px;width:100%;max-width:1100px}
    h2{margin:0 0 4px;font:800 28px 'Bricolage Grotesque',sans-serif;letter-spacing:-1px;color:var(--ink)}
    .muted{margin:0;font-size:12px;color:var(--muted)}
    .slab,.box{background:#fff;color:#141118;border-radius:24px}
    .who{display:flex;align-items:center;gap:20px;padding:24px}
    .photo{display:grid;place-items:center;width:88px;height:88px;border-radius:50%;background:#FFD400;color:#141118;font:800 30px 'Bricolage Grotesque',sans-serif;box-shadow:0 0 0 6px #FFF3BF;flex-shrink:0;margin:6px}
    h3{margin:0 0 10px;font:800 26px 'Bricolage Grotesque',sans-serif;letter-spacing:-.6px;color:#141118}
    .tags{display:flex;gap:8px;flex-wrap:wrap}
    .tag{display:inline-flex;align-items:center;gap:6px;border-radius:999px;padding:5px 12px;font-size:11px;font-weight:800;background:#FFE7D3;color:#9A3C07}
    .tag.on{background:#DDF6E6;color:#15803D}
    i{display:inline-block;width:20px;height:20px;background:currentColor;-webkit-mask:var(--ic) center / contain no-repeat;mask:var(--ic) center / contain no-repeat;flex-shrink:0}
    .tag i{width:13px;height:13px}
    .boxes{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}
    .box{display:flex;align-items:center;gap:14px;padding:18px 20px;min-width:0}
    .box>div{display:grid;gap:4px;min-width:0}
    .chip{display:grid;place-items:center;width:44px;height:44px;border-radius:14px;flex-shrink:0;background:#FFE7D3;color:#C2410C}
    [data-kind=mail] .chip{background:#E3EEFF;color:#1D4ED8}
    [data-kind=shield] .chip{background:#EFE8FF;color:#6D28D9}
    [data-kind=box] .chip{background:#DBF5FA;color:#0E7490}
    [data-kind=eye] .chip{background:#FFF3D6;color:#B45309}
    .label{font-size:10px;font-weight:800;letter-spacing:.8px;text-transform:uppercase;color:#5E5A66}
    strong{font-size:14px;font-weight:800;word-break:break-all}
    .security{display:flex;align-items:center;gap:14px;padding:18px 20px;flex-wrap:wrap}
    .security>div{flex:1;min-width:180px}
    h4{margin:0 0 3px;font:800 15px 'Bricolage Grotesque',sans-serif}
    .security p{margin:0;font-size:12px;color:#5E5A66}
    @media(max-width:700px){.boxes{grid-template-columns:1fr}.who{flex-direction:column;align-items:flex-start}}
  `],
})
export class ProfileComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  /** The organization's name. Only administrators may read the list, so the box is left out for everyone else. */
  readonly organization = signal<string | null>(null);

  ngOnInit() {
    const u = this.auth.userContext();
    if (!u || (u.role !== Role.ApplicationAdmin && u.role !== Role.Admin)) return;
    this.api.get<{ id: string; name: string }[]>('/organizations').subscribe({
      next: r => this.organization.set(r.data?.find(o => o.id === u.organizationId)?.name ?? null),
      error: () => this.organization.set(null),
    });
  }
  readonly icon = iconUrl;
  readonly byLabel = (_: number, d: { label: string }) => d.label;
  details() {
    const u = this.auth.userContext();
    return [
      { kind: 'mail', label: 'Email', value: u?.email || 'Email unavailable' },
      { kind: 'shield', label: 'Role', value: u?.role || '—' },
      ...(this.organization() ? [{ kind: 'box', label: 'Organization', value: this.organization()! }] : []),
      { kind: 'eye', label: 'User ID', value: u?.userId || '—' },
    ];
  }
  initials(): string { return initialsOf(this.auth.userContext()?.displayName); }
}
