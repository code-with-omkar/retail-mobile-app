import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';
import { initialsOf } from '../../shared/utils/palette';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule],
  template: `
    <section class="profile">
      <header class="hero">
        <span class="avatar">{{ initials() }}</span>
        <div>
          <span class="eyebrow">ACCOUNT</span>
          <h2>{{ auth.userContext()?.displayName || 'Profile' }}</h2>
          <span class="role">{{ auth.userContext()?.role || 'User' }}</span>
        </div>
      </header>

      <div class="grid">
        <article><span class="label">Email</span><strong>{{ auth.userContext()?.email || 'Email unavailable' }}</strong></article>
        <article><span class="label">Role</span><strong>{{ auth.userContext()?.role || '—' }}</strong></article>
        <article><span class="label">Organization</span><strong>{{ auth.userContext()?.organizationId || '—' }}</strong></article>
        <article><span class="label">User ID</span><strong>{{ auth.userContext()?.userId || '—' }}</strong></article>
      </div>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#141118}
    .profile{max-width:760px;margin:auto;display:grid;gap:22px}
    .hero{display:flex;align-items:center;gap:18px}
    .avatar{display:grid;place-items:center;width:84px;height:84px;border-radius:50%;background:#FFD400;font:800 28px 'Space Grotesk',sans-serif;box-shadow:0 12px 32px rgba(20,17,24,.18);flex-shrink:0}
    .eyebrow{display:inline-block;background:#B9A8FF;font-size:10px;font-weight:800;letter-spacing:1.5px;padding:4px 10px;border-radius:999px}
    h2{margin:8px 0;font:800 32px 'Space Grotesk',sans-serif;letter-spacing:-1px;color:#fff}
    .role{display:inline-block;background:#FB1A8E;border-radius:999px;padding:3px 12px;font-size:11px;font-weight:800}
    .grid{display:grid;grid-template-columns:repeat(2,1fr);gap:12px}
    article{border-radius:24px;padding:18px 20px;display:grid;gap:10px;min-width:0}
    article:nth-child(4n+1){background:#FFF1A8}
    article:nth-child(4n+2){background:#B9FFCF}
    article:nth-child(4n+3){background:#EAE4FF}
    article:nth-child(4n+4){background:#FFC6E2}
    .label{justify-self:start;border-radius:999px;padding:3px 12px;font-size:10px;font-weight:800;letter-spacing:1px;text-transform:uppercase}
    article:nth-child(4n+1) .label{background:#FFD400}
    article:nth-child(4n+2) .label{background:#02F34C}
    article:nth-child(4n+3) .label{background:#B9A8FF}
    article:nth-child(4n+4) .label{background:#FB1A8E}
    article strong{font-size:14px;font-weight:800;word-break:break-all}
    @media(max-width:560px){.grid{grid-template-columns:1fr}.hero{flex-direction:column;align-items:flex-start}}
  `],
})
export class ProfileComponent {
  readonly auth = inject(AuthService);
  initials(): string { return initialsOf(this.auth.userContext()?.displayName); }
}
