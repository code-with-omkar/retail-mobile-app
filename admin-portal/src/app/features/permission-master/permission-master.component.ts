import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { Permission } from '../../core/models/domain.model';
import { UserMasterService } from '../../core/services/user-master.service';

@Component({
  selector: 'app-permission-master',
  standalone: true,
  imports: [CommonModule],
  template: `
    <main class="page"><header><span class="eyebrow">ACCESS CONTROL</span><h1>Permission Master</h1><p>Server-defined capabilities available to organization roles.</p></header><section class="grid"><article *ngFor="let permission of permissions()"><span class="code">{{ permission.code }}</span><h2>{{ permission.name }}</h2><p>{{ permission.description }}</p><span class="state">{{ permission.isActive ? 'Active' : 'Inactive' }}</span></article></section><p class="error" *ngIf="error()">{{ error() }}</p></main>
  `,
  styles: [`
    :host { display:block; color:#25322d; } .page { padding:32px; max-width:1120px; margin:auto; } .eyebrow { color:#b45f32; font-size:11px; letter-spacing:2px; font-weight:700; } h1 { margin:8px 0; font:700 38px Georgia,serif; } header p { color:#708078; } .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(230px,1fr)); gap:14px; margin-top:30px; } article { background:#fffdf8; border:1px solid #dce5df; padding:22px; min-height:150px; } .code { color:#b45f32; font:600 12px monospace; } h2 { font:700 21px Georgia,serif; margin:18px 0 8px; } article p { color:#708078; line-height:1.5; } .state { color:#2f7655; font-size:12px; font-weight:700; } .error { color:#ad3c32; }
  `],
})
export class PermissionMasterComponent implements OnInit {
  private readonly service = inject(UserMasterService);
  readonly permissions = signal<Permission[]>([]);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.service.getPermissions().subscribe({ next: response => this.permissions.set(response.data ?? []), error: error => this.error.set(error.message) });
  }
}