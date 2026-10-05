import { Component, Input, Output, EventEmitter, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LoadingComponent } from '../loading/loading.component';
import { EmptyStateComponent } from '../empty-state/empty-state.component';

export interface TableColumn<T> {
  key: keyof T;
  label: string;
  sortable?: boolean;
  width?: string;
  formatter?: (value: any, row: T) => string;
}

export interface TableState {
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
  page: number;
  pageSize: number;
}

export interface PaginationMeta {
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

@Component({
  selector: 'app-table',
  standalone: true,
  imports: [CommonModule, LoadingComponent, EmptyStateComponent],
  template: `
    <div class="table-container">
      <app-loading *ngIf="isLoading" [message]="'Loading data...'"></app-loading>

      <table *ngIf="!isLoading && rows.length > 0" class="data-table">
        <thead>
          <tr>
            <th *ngIf="selectable" class="checkbox-col">
              <input
                type="checkbox"
                [checked]="allRowsSelected()"
                (change)="toggleAllRows($event)"
                aria-label="Select all rows"
              />
            </th>
            <th
              *ngFor="let col of columns"
              [style.width]="col.width"
              [class.sortable]="col.sortable"
              (click)="col.sortable ? onSort(col) : null"
            >
              <div class="th-content">
                <span>{{ col.label }}</span>
                <span
                  *ngIf="col.sortable && state().sortBy === col.key"
                  class="sort-indicator"
                >
                  {{ state().sortOrder === 'asc' ? '▲' : '▼' }}
                </span>
              </div>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let row of rows; let i = index" [class.selected]="isRowSelected(row)">
            <td *ngIf="selectable" class="checkbox-col">
              <input
                type="checkbox"
                [checked]="isRowSelected(row)"
                (change)="toggleRowSelection(row, $event)"
                [attr.aria-label]="'Select row ' + (i + 1)"
              />
            </td>
            <td *ngFor="let col of columns" [style.width]="col.width">
              {{ col.formatter ? col.formatter(row[col.key], row) : row[col.key] }}
            </td>
          </tr>
        </tbody>
      </table>

      <app-empty-state
        *ngIf="!isLoading && rows.length === 0"
        [title]="emptyTitle"
        [message]="emptyMessage"
      ></app-empty-state>

      <div *ngIf="pagination && rows.length > 0" class="pagination">
        <button
          [disabled]="state().page === 1"
          (click)="goToPage(state().page - 1)"
        >
          ← Previous
        </button>
        <span class="page-info">
          Page {{ state().page }} of {{ pagination.totalPages }}
        </span>
        <button
          [disabled]="state().page === pagination.totalPages"
          (click)="goToPage(state().page + 1)"
        >
          Next →
        </button>
      </div>
    </div>
  `,
  styles: [`
    .table-container {
      width: 100%;
      overflow-x: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
      background: #fff;
    }

    thead {
      background: #f6f8f6;
      border-bottom: 1px solid #e3eae5;
    }

    th {
      padding: 12px 16px;
      text-align: left;
      font-size: 10px;
      font-weight: 700;
      color: #82908b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    th.sortable {
      cursor: pointer;
      user-select: none;
    }

    th.sortable:hover {
      background: #eff2ef;
    }

    .th-content {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .sort-indicator {
      font-size: 8px;
      color: #6c9d1e;
    }

    td {
      padding: 12px 16px;
      border-bottom: 1px solid #eff2ef;
      font-size: 11px;
      color: #172523;
    }

    tr:hover {
      background: #f9faf8;
    }

    tr.selected {
      background: #e6f4c7;
    }

    .checkbox-col {
      width: 40px;
      padding: 12px;
      text-align: center;
    }

    input[type="checkbox"] {
      cursor: pointer;
      accent-color: #6c9d1e;
    }

    .pagination {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 16px;
      padding: 20px;
      border-top: 1px solid #e3eae5;
      background: #f6f8f6;
    }

    .pagination button {
      border: 1px solid #e2e8e3;
      background: #fff;
      color: #172523;
      padding: 8px 12px;
      border-radius: 6px;
      font-size: 11px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .pagination button:hover:not(:disabled) {
      background: #eff2ef;
    }

    .pagination button:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .page-info {
      font-size: 11px;
      color: #82908b;
    }
  `],
})
export class TableComponent<T> implements OnInit {
  @Input() columns: TableColumn<T>[] = [];
  @Input() rows: T[] = [];
  @Input() isLoading = false;
  @Input() pagination?: PaginationMeta;
  @Input() selectable = false;
  @Input() emptyTitle = 'No data';
  @Input() emptyMessage = 'No records to display';

  @Output() stateChange = new EventEmitter<TableState>();
  @Output() selectionChange = new EventEmitter<T[]>();
  @Output() pageChange = new EventEmitter<number>();
  @Output() sortChange = new EventEmitter<{ sortBy: string; sortOrder: 'asc' | 'desc' }>();

  state = signal<TableState>({
    page: 1,
    pageSize: 10,
  });

  private selectedRows = signal<Set<any>>(new Set());

  ngOnInit(): void {
    this.emitStateChange();
  }

  onSort(column: TableColumn<T>): void {
    const currentSort = this.state().sortBy;
    const currentOrder = this.state().sortOrder;

    let newOrder: 'asc' | 'desc' = 'asc';
    if (currentSort === String(column.key) && currentOrder === 'asc') {
      newOrder = 'desc';
    }

    this.state.update(s => ({
      ...s,
      sortBy: String(column.key),
      sortOrder: newOrder,
      page: 1,
    }));

    this.sortChange.emit({
      sortBy: String(column.key),
      sortOrder: newOrder,
    });

    this.emitStateChange();
  }

  goToPage(page: number): void {
    this.state.update(s => ({ ...s, page }));
    this.pageChange.emit(page);
    this.emitStateChange();
  }

  toggleRowSelection(row: T, event: Event): void {
    const checkbox = event.target as HTMLInputElement;
    const selected = this.selectedRows();
    if (checkbox.checked) {
      selected.add(row);
    } else {
      selected.delete(row);
    }
    this.selectedRows.set(new Set(selected));
    this.selectionChange.emit(Array.from(selected));
  }

  toggleAllRows(event: Event): void {
    const checkbox = event.target as HTMLInputElement;
    const selected = new Set<T>();
    if (checkbox.checked) {
      this.rows.forEach(row => selected.add(row));
    }
    this.selectedRows.set(selected);
    this.selectionChange.emit(Array.from(selected));
  }

  allRowsSelected(): boolean {
    const selected = this.selectedRows().size;
    return selected > 0 && selected === this.rows.length;
  }

  isRowSelected(row: T): boolean {
    return this.selectedRows().has(row);
  }

  private emitStateChange(): void {
    this.stateChange.emit(this.state());
  }
}
