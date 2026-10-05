import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';

@Component({ selector: 'app-profile', standalone: true, imports: [CommonModule], template: `<section class="profile"><span class="eyebrow">ACCOUNT</span><h2>Profile</h2><div class="panel"><strong>{{ auth.userContext()?.displayName }}</strong><span>{{ auth.userContext()?.email || 'Email unavailable' }}</span><span>Role: {{ auth.userContext()?.role }}</span><span>User ID: {{ auth.userContext()?.userId }}</span><span>Organization: {{ auth.userContext()?.organizationId }}</span></div></section>`, styles: [`.profile{max-width:600px}.eyebrow{color:#899a94;font-size:10px;font-weight:700;letter-spacing:1.5px}.panel{display:grid;gap:10px;margin-top:18px;background:#fff;border:1px solid #e5ebe6;border-radius:8px;padding:22px}.panel span{color:#586963;font-size:12px}`] })
export class ProfileComponent { readonly auth=inject(AuthService); }
