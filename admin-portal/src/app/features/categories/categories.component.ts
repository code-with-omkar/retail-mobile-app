import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';

export interface AdminCategory { id: string; name: string; parentCategoryId: string | null; isActive: boolean; productCount: number; }

/** A top-level category with the sub-categories under it. */
export interface CategoryGroup { category: AdminCategory; subs: AdminCategory[]; }

/** Top-level categories (A to Z), each with its sub-categories (A to Z). A sub-category whose parent is missing is shown as top level. */
export function groupCategories(all: AdminCategory[]): CategoryGroup[] {
  const byName = (a: AdminCategory, b: AdminCategory) => a.name.localeCompare(b.name);
  const ids = new Set(all.map(c => c.id));
  const tops = all.filter(c => !c.parentCategoryId || !ids.has(c.parentCategoryId)).sort(byName);
  return tops.map(category => ({ category, subs: all.filter(c => c.parentCategoryId === category.id).sort(byName) }));
}

/**
 * The category tree. An administrator adds categories and puts one under another (a sub-category), which the customer app shows as
 * chips above the products of the top-level category. The tree is two levels deep.
 */
@Component({
  selector: 'app-categories',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="page">
      <h2>Categories</h2>
      <p class="muted">Put a category under another to make it a sub-category. Customers see sub-categories as chips above the products of the main category. Only categories that have products in the shop they are browsing are shown.</p>
      <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
      <p *ngIf="notice()" class="notice" role="status">{{ notice() }}</p>

      <form class="card" (ngSubmit)="save()" #f="ngForm">
        <h3>{{ editing() ? 'Edit category' : 'New category' }}</h3>
        <div class="grid">
          <label>Name<input name="name" [(ngModel)]="name" maxlength="60" required /></label>
          <label>Under
            <select name="parent" [(ngModel)]="parentId">
              <option value="">Nothing (a main category)</option>
              <option *ngFor="let p of parentChoices()" [value]="p.id">{{ p.name }}</option>
            </select>
          </label>
          <label class="check"><input type="checkbox" name="active" [(ngModel)]="isActive" /> Shown in the app</label>
        </div>
        <div class="buttons">
          <button class="primary" type="submit" [disabled]="busy() || f.invalid || !name.trim()">{{ busy() ? 'Saving…' : (editing() ? 'Save changes' : 'Add category') }}</button>
          <button type="button" class="ghost" *ngIf="editing()" (click)="reset()">Cancel</button>
        </div>
      </form>

      <p *ngIf="loading()">Loading categories…</p>
      <p *ngIf="!loading() && groups().length === 0">No categories yet.</p>
      <div class="table" *ngIf="groups().length">
        <div class="row head"><span>Category</span><span>Products</span><span>Status</span><span>Action</span></div>
        <ng-container *ngFor="let g of groups()">
          <div class="row">
            <span class="name">{{ g.category.name }}<small *ngIf="g.subs.length">{{ g.subs.length }} sub-categor{{ g.subs.length === 1 ? 'y' : 'ies' }}</small></span>
            <span>{{ g.category.productCount }}</span>
            <span class="badge" [class.off]="!g.category.isActive">{{ g.category.isActive ? 'On' : 'Off' }}</span>
            <span class="actions"><button class="act" [disabled]="busy()" (click)="edit(g.category)">Edit</button></span>
          </div>
          <div class="row sub" *ngFor="let s of g.subs">
            <span class="name">↳ {{ s.name }}</span>
            <span>{{ s.productCount }}</span>
            <span class="badge" [class.off]="!s.isActive">{{ s.isActive ? 'On' : 'Off' }}</span>
            <span class="actions"><button class="act" [disabled]="busy()" (click)="edit(s)">Edit</button></span>
          </div>
        </ng-container>
      </div>
    </section>
  `,
  styles: [`
    :host{display:block;font-family:'Inter',sans-serif;color:#fff}
    h2{font-family:'Space Grotesk',sans-serif;font-size:28px;letter-spacing:-1px;margin:0 0 4px;color:#fff}
    .muted{color:#E9E4FF;font-size:12px;margin:0 0 20px}
    .page{max-width:900px;display:grid;gap:16px}
    .card{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:20px;display:grid;gap:12px;color:var(--on-card,#141118)}
    h3{margin:0;font:800 16px 'Space Grotesk',sans-serif}
    .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px}
    label{display:grid;gap:4px;font-size:11px;font-weight:700}
    label.check{display:flex;gap:8px;align-items:center}
    input,select{border:1px solid rgba(20,17,24,.2);border-radius:12px;padding:8px 10px;font:inherit;background:#fff;color:#141118}
    input[type=checkbox]{width:auto}
    .buttons{display:flex;gap:10px}
    .primary{border:0;border-radius:999px;background:#FFD400;color:#141118;padding:10px 20px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .primary:disabled{opacity:.5;cursor:default}
    .ghost{border:1px solid rgba(20,17,24,.35);border-radius:999px;background:transparent;padding:10px 20px;font:800 12px 'Inter',sans-serif;cursor:pointer}
    .table{background:var(--card-bg,#EAE4FF);border-radius:24px;padding:8px 20px;color:var(--on-card,#141118)}
    .row{display:grid;grid-template-columns:2fr .8fr .8fr 1fr;gap:12px;align-items:center;padding:12px 0;border-top:1px solid rgba(20,17,24,.1);font-size:12px}
    .row.head{border-top:0;font-size:10px;font-weight:700;text-transform:uppercase}
    .row.sub{padding-left:18px;font-size:12px}
    .name{font-weight:800}
    .row.sub .name{font-weight:600}
    .row small{display:block;color:var(--on-card-muted,#5E5A66);font-size:10px;font-weight:400}
    .badge{justify-self:start;border-radius:999px;padding:3px 10px;font-size:10px;font-weight:800;background:#14A44D;color:#fff}
    .badge.off{background:#ECECF0;color:#141118}
    .actions{display:flex;gap:6px}
    .act{border:0;border-radius:999px;padding:6px 12px;font:800 11px 'Inter',sans-serif;cursor:pointer;background:#141118;color:#fff}
    .act:disabled{opacity:.5;cursor:default}
    .error{color:#FFB3D9;font-weight:700}
    .notice{color:#B9F6CA;font-weight:700}
  `],
})
export class CategoriesComponent implements OnInit {
  private api = inject(ApiService);

  readonly items = signal<AdminCategory[]>([]);
  readonly groups = computed(() => groupCategories(this.items()));
  readonly editing = signal<AdminCategory | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  name = '';
  parentId = '';
  isActive = true;

  /** Only a main category can be a parent, never the category being edited, and a category that has sub-categories cannot go under another. */
  parentChoices(): AdminCategory[] {
    const all = this.items();
    const own = this.editing();
    if (own && all.some(c => c.parentCategoryId === own.id)) return [];
    return all.filter(c => !c.parentCategoryId && c.id !== own?.id).sort((a, b) => a.name.localeCompare(b.name));
  }

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.api.get<AdminCategory[]>('/admin/catalog/categories').subscribe({
      next: r => { this.items.set(r.data ?? []); this.loading.set(false); },
      error: e => { this.error.set(e?.message ?? 'Could not load categories.'); this.loading.set(false); },
    });
  }

  reset() { this.editing.set(null); this.name = ''; this.parentId = ''; this.isActive = true; }

  edit(c: AdminCategory) {
    this.editing.set(c);
    this.name = c.name; this.parentId = c.parentCategoryId ?? ''; this.isActive = c.isActive;
    this.error.set(null); this.notice.set(null);
  }

  save() {
    if (this.busy() || !this.name.trim()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const editing = this.editing();
    const body = { name: this.name.trim(), parentCategoryId: this.parentId || null, isActive: this.isActive };
    const call = editing ? this.api.put<AdminCategory>('/admin/catalog/categories/' + editing.id, body) : this.api.post<AdminCategory>('/admin/catalog/categories', body);
    call.subscribe({
      next: () => { this.busy.set(false); this.notice.set(editing ? 'Category saved.' : 'Category added.'); this.reset(); this.load(); },
      error: e => { this.busy.set(false); this.error.set(e?.message ?? 'Could not save the category.'); },
    });
  }
}
