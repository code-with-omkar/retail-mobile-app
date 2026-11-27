import { LoaderComponent } from '../loader/loader.component';
import { Component, ContentChildren, Directive, HostBinding, Input, QueryList, TemplateRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface DataColumn { key: string; label: string; width?: string; }

/** The look of one column's cells: `<ng-template appCell="status" let-row>…</ng-template>`. Without one the row's value is printed. */
@Directive({ selector: '[appCell]', standalone: true })
export class CellDirective {
  @Input('appCell') key = '';
  readonly template = inject<TemplateRef<unknown>>(TemplateRef);
}

/**
 * The list used by every screen: a header that stays while the rows scroll, a running row number across pages, and the row being worked on
 * marked. Rows are tracked by id, so refreshing the data does not rebuild them or move the scroll position.
 */
@Component({
  selector: 'app-data-table',
  standalone: true,
  imports: [CommonModule, LoaderComponent],
  template: `
    <div class="frame">
      <div class="scroll" [style.maxHeight]="maxHeight">
        <table [style.minWidth]="minWidth">
          <thead>
            <tr><th class="index">#</th><th *ngFor="let c of columns" [style.width]="c.width">{{ c.label }}</th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let row of rows; let i = index; trackBy: trackRow" [class.active]="activeId !== null && row.id === activeId">
              <td class="index">{{ startIndex + i + 1 }}</td>
              <td *ngFor="let c of columns">
                <ng-container *ngIf="cellFor(c.key) as cell; else plain"><ng-container *ngTemplateOutlet="cell; context: { $implicit: row }"></ng-container></ng-container>
                <ng-template #plain>{{ row[c.key] }}</ng-template>
              </td>
            </tr>
          </tbody>
        </table>
        <app-loader *ngIf="loading && !rows.length"></app-loader>
        <p class="state" *ngIf="!loading && !rows.length">{{ emptyText }}</p>
      </div>
    </div>
  `,
  styles: [`
    :host{display:block}
    :host.fill{flex:1;min-height:0;display:flex;flex-direction:column}
    :host.fill .frame{flex:1;min-height:0;display:flex;flex-direction:column}
    :host.fill .scroll{flex:1;min-height:0;max-height:none !important}
    .frame{border:1px solid var(--line);border-radius:18px;background:#fff;overflow:hidden}
    .scroll{display:flex;flex-direction:column;overflow:auto;scrollbar-width:thin;scrollbar-color:#B9B2D9 transparent;overscroll-behavior:contain}
    table{flex-shrink:0;width:100%;border-collapse:collapse;font-size:12px;color:var(--ink)}
    th{position:sticky;top:0;z-index:1;background:var(--card);text-align:left;font-size:10px;font-weight:800;text-transform:uppercase;letter-spacing:.4px;color:var(--muted);padding:11px 12px;white-space:nowrap}
    td{padding:11px 12px;border-top:1px solid var(--line);vertical-align:middle}
    .index{width:44px;color:var(--muted);text-align:right}
    tr.active td{background:#FFF6C2}
    tr.active td:first-child{box-shadow:inset 4px 0 0 var(--yellow)}
    .state{text-align:center;color:var(--muted);padding:28px 12px;margin:0;font-size:12px}
  `],
})
export class DataTableComponent {
  @Input() columns: DataColumn[] = [];
  @Input() rows: any[] = [];
  @Input() loading = false;
  @Input() emptyText = 'Nothing to show.';
  /** The id of the row being edited or just changed. */
  @Input() activeId: string | null = null;
  /** How many rows come before this page, so numbering carries on. */
  @Input() startIndex = 0;
  @Input() maxHeight = '62vh';
  /** A wide list keeps this width and scrolls sideways on a narrower screen. */
  @Input() minWidth: string | null = null;
  /** Take the rest of a column layout instead of stopping at maxHeight: the page then has one scrollbar, the list's. */
  @Input() @HostBinding('class.fill') fill = false;

  @ContentChildren(CellDirective) cells?: QueryList<CellDirective>;

  trackRow = (index: number, row: any) => row?.id ?? index;
  cellFor(key: string) { return this.cells?.find(c => c.key === key)?.template ?? null; }
}
