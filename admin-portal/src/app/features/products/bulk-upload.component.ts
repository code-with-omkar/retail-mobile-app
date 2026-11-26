import { Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Observable, catchError, concatMap, from, map, of, switchMap, tap } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { FileUploadComponent } from '../../shared/components/file-upload/file-upload.component';
import { ImportCategory, ImportRow, MAX_ROWS, readImport, templateCsv } from './product-import';

export interface ImportResult { line: number; name: string; ok: boolean; message: string; }

/**
 * Many products at once: download the template, fill it in, upload it, check the rows, then create them. Rows with a problem are left out
 * and shown with the reason; what the server refuses (such as an SKU already in use) is reported row by row. Pictures are added afterwards.
 */
@Component({
  selector: 'app-bulk-upload',
  standalone: true,
  imports: [CommonModule, FileUploadComponent],
  template: `
    <div class="step"><span class="num">1</span><div><strong>Download the template</strong><p>Fill one product per line. Save it from Excel as <b>CSV UTF-8</b>. Category names must match the ones in the portal.</p><button type="button" class="act" (click)="download()">Download template</button></div></div>
    <div class="step"><span class="num">2</span><div class="grow"><strong>Upload the filled file</strong>
      <app-file-upload label="" [types]="[]" accept=".csv,text/csv" [maxBytes]="1048576" [busy]="running()" [removable]="false" (fileSelected)="read($event)"></app-file-upload>
      <small>Up to {{ max }} products in one file.</small>
    </div></div>
    <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>

    <ng-container *ngIf="rows().length">
      <div class="step"><span class="num">3</span><div><strong>Check and create</strong><p>{{ good().length }} ready · {{ rows().length - good().length }} with a problem</p></div></div>
      <div class="preview">
        <table>
          <thead><tr><th>Line</th><th>SKU</th><th>Name</th><th>Price</th><th>Result</th></tr></thead>
          <tbody>
            <tr *ngFor="let r of rows()" [class.bad]="r.problems.length || resultFor(r)?.ok === false" [class.done]="resultFor(r)?.ok">
              <td>{{ r.line }}</td><td>{{ r.sku }}</td><td>{{ r.name }}</td><td>{{ r.price || '' }}</td>
              <td>{{ resultFor(r)?.message ?? (r.problems.join('; ') || 'Ready') }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="progress" role="status" *ngIf="running() || finished()">{{ results().length }} of {{ total() }} done · {{ createdCount() }} created</p>
      <button type="button" class="primary" [disabled]="running() || finished() || !good().length" (click)="importRows()">{{ running() ? 'Creating…' : 'Create ' + good().length + ' product' + (good().length === 1 ? '' : 's') }}</button>
    </ng-container>
  `,
  styles: [`
    :host{display:grid;gap:14px;color:var(--ink);font-size:12px}
    .step{display:flex;gap:10px}
    .step>div{display:grid;gap:6px;justify-items:start}
    .step .grow{flex:1;justify-items:stretch}
    .step p{margin:0;color:var(--muted)}
    .num{width:24px;height:24px;border-radius:50%;background:var(--yellow);display:grid;place-items:center;font-weight:800;flex-shrink:0}
    small{color:var(--muted)}
    .act,.primary{border:1px solid #D5CEF5;border-radius:999px;padding:8px 16px;font:800 11px 'Plus Jakarta Sans',sans-serif;background:var(--soft);color:var(--ink);cursor:pointer}
    .primary{background:var(--yellow);border-color:var(--yellow);justify-self:start}
    .primary:disabled{opacity:.45;cursor:default}
    .preview{max-height:260px;overflow:auto;border:1px solid var(--line);border-radius:14px}
    table{width:100%;border-collapse:collapse;font-size:11px}
    th{position:sticky;top:0;background:var(--card);text-align:left;padding:8px;font-size:10px;text-transform:uppercase}
    td{border-top:1px solid var(--line);padding:7px 8px;vertical-align:top}
    tr.bad td:last-child{color:var(--danger);font-weight:700}
    tr.done td:last-child{color:var(--success);font-weight:700}
    .error{color:var(--danger);font-weight:700;margin:0}
    .progress{margin:0;font-weight:700}
  `],
})
export class BulkUploadComponent {
  private api = inject(ApiService);
  @Input() categories: ImportCategory[] = [];
  /** Sent once the rows have been tried, with how many were created. */
  @Output() imported = new EventEmitter<number>();

  readonly max = MAX_ROWS;
  readonly rows = signal<ImportRow[]>([]);
  readonly results = signal<ImportResult[]>([]);
  readonly error = signal<string | null>(null);
  readonly running = signal(false);
  readonly finished = signal(false);
  readonly good = computed(() => this.rows().filter(r => !r.problems.length));
  readonly total = computed(() => this.good().length);
  readonly createdCount = computed(() => this.results().filter(r => r.ok).length);

  resultFor(row: ImportRow) { return this.results().find(r => r.line === row.line); }

  download() {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([templateCsv(this.categories)], { type: 'text/csv;charset=utf-8' }));
    link.download = 'products-template.csv';
    link.click();
    URL.revokeObjectURL(link.href);
  }

  async read(file: File) { this.load(await file.text()); }

  load(text: string) {
    this.error.set(null); this.results.set([]); this.finished.set(false);
    try { this.rows.set(readImport(text, this.categories)); }
    catch (e) { this.rows.set([]); this.error.set((e as Error).message); }
  }

  /** One product after another, so the server is not flooded and each row gets its own answer. */
  importRows() {
    if (this.running() || !this.good().length) return;
    this.running.set(true); this.results.set([]);
    from(this.good()).pipe(concatMap(row => this.create(row))).subscribe({
      next: result => this.results.set([...this.results(), result]),
      complete: () => { this.running.set(false); this.finished.set(true); this.imported.emit(this.createdCount()); },
    });
  }

  private create(row: ImportRow): Observable<ImportResult> {
    const done = (ok: boolean, message: string): ImportResult => ({ line: row.line, name: row.name, ok, message });
    return this.api.post<{ id: string }>('/admin/catalog/products', { sku: row.sku, name: row.name, description: row.description, categoryId: row.categoryId, unitOfMeasure: row.unit, price: row.price, mrp: row.mrp }).pipe(
      switchMap(r => {
        if (!row.nameMarathi || !r.data?.id) return of(done(true, 'Created'));
        return this.api.put('/admin/catalog/products/' + r.data.id + '/translations/mr', { name: row.nameMarathi, description: null }).pipe(
          map(() => done(true, 'Created')),
          catchError(() => of(done(true, 'Created; the Marathi name was not saved'))));
      }),
      catchError(e => of(done(false, e?.error?.message ?? e?.message ?? 'Not created'))),
    );
  }
}
