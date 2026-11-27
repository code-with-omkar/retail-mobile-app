import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export const PAGE_SIZES = [10, 20, 50] as const;
export const DEFAULT_PAGE_SIZE = 20;

export function pageCount(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
}

/** The page numbers to offer: the first, the last and two either side of the current one; 0 stands for a gap. */
export function pageNumbers(page: number, pages: number): number[] {
  const wanted = new Set([1, pages]);
  for (let p = page - 2; p <= page + 2; p++) if (p >= 1 && p <= pages) wanted.add(p);
  const out: number[] = [];
  let last = 0;
  for (const p of [...wanted].sort((a, b) => a - b)) {
    if (p - last > 1) out.push(0);
    out.push(p);
    last = p;
  }
  return out;
}

/** "Showing 21-40 of 134", page numbers and the rows per page. */
@Component({
  selector: 'app-pagination',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <nav class="pager" aria-label="Pages" *ngIf="total > 0">
      <span class="range">Showing {{ from }}–{{ to }} of {{ total }}</span>
      <span class="pages">
        <button type="button" [disabled]="page <= 1" (click)="go(page - 1)" aria-label="Previous page">‹</button>
        <ng-container *ngFor="let p of numbers">
          <span class="gap" *ngIf="p === 0">…</span>
          <button type="button" *ngIf="p !== 0" [class.on]="p === page" [attr.aria-current]="p === page ? 'page' : null" (click)="go(p)">{{ p }}</button>
        </ng-container>
        <button type="button" [disabled]="page >= pages" (click)="go(page + 1)" aria-label="Next page">›</button>
      </span>
      <label class="size">Rows
        <select name="pageSize" [ngModel]="pageSize" (ngModelChange)="resize($event)">
          <option *ngFor="let s of sizes" [ngValue]="s">{{ s }}</option>
        </select>
      </label>
    </nav>
  `,
  styles: [`
    .pager{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;padding:12px 4px;font-size:12px;color:var(--muted)}
    .pages{display:flex;gap:4px;align-items:center}
    button{min-width:32px;height:32px;border:1px solid var(--line);border-radius:999px;background:#fff;color:var(--ink);font:700 12px inherit;cursor:pointer}
    button.on{background:var(--yellow);border-color:var(--yellow)}
    button:disabled{opacity:.4;cursor:default}
    .size{display:flex;gap:6px;align-items:center}
    select{border:1px solid var(--line);border-radius:10px;padding:5px 8px;background:#fff;color:var(--ink);font:inherit}
  `],
})
export class PaginationComponent {
  @Input() page = 1;
  @Input() pageSize: number = DEFAULT_PAGE_SIZE;
  @Input() total = 0;
  @Output() pageChange = new EventEmitter<number>();
  @Output() pageSizeChange = new EventEmitter<number>();

  readonly sizes = PAGE_SIZES;
  get pages() { return pageCount(this.total, this.pageSize); }
  get numbers() { return pageNumbers(this.page, this.pages); }
  get from() { return Math.min(this.total, (this.page - 1) * this.pageSize + 1); }
  get to() { return Math.min(this.total, this.page * this.pageSize); }

  go(page: number) { if (page >= 1 && page <= this.pages && page !== this.page) this.pageChange.emit(page); }
  resize(size: number) { this.pageSizeChange.emit(Number(size)); this.pageChange.emit(1); }
}
