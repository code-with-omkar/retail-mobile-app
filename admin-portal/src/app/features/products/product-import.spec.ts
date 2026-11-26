import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { csvLine, parseCsv } from '../../shared/utils/csv';
import { readImport, templateCsv } from './product-import';
import { BulkUploadComponent } from './bulk-upload.component';
import { ApiService } from '../../core/services/api.service';

const categories = [{ id: 'c1', name: 'Groceries' }];
const head = 'sku,name,category,unit,price,mrp,description,name_marathi';

describe('CSV', () => {
  it('reads quoted cells with commas, quotes and line breaks, and skips empty lines', () => {
    expect(parseCsv('\uFEFFa,b\r\n"x, y","say ""hi""\nbye"\r\n\r\n')).toEqual([['a', 'b'], ['x, y', 'say "hi"\nbye']]);
  });

  it('writes cells safely, including ones a spreadsheet would run as a formula', () => {
    expect(csvLine(['a,b', 'q"q', '=1+1', 5])).toBe('"a,b","q""q",\'=1+1,5');
  });
});

describe('product upload file', () => {
  it('the template reads back as two good rows', () => {
    const rows = readImport(templateCsv(categories), categories);
    expect(rows.map(r => [r.sku, r.categoryId, r.price, r.mrp, r.problems])).toEqual([['SAMPLE-001', 'c1', 89, 99, []], ['SAMPLE-002', 'c1', 22, null, []]]);
  });

  it('says what is wrong with each row', () => {
    const rows = readImport([head, 'A,Rice,groceries,1 kg,50,40,,', 'a,,Toys,,0,,,'].join('\n'), categories);
    expect(rows[0].problems).toEqual(['MRP must be empty or at least the price']);
    expect(rows[1].problems).toEqual(['SKU appears twice in the file', 'Name is missing', 'No category called "Toys"', 'Unit is missing', 'Price must be a number from 1 to 1,00,000']);
  });

  it('refuses a file without the columns or without products', () => {
    expect(() => readImport('name,price\nRice,5', categories)).toThrowError(/Missing: sku, category, unit/);
    expect(() => readImport(head, categories)).toThrowError(/no products/);
    expect(() => readImport('', categories)).toThrowError(/empty/);
  });
});

describe('BulkUploadComponent', () => {
  it('creates the good rows one by one, saves the Marathi name, and reports each refusal', () => {
    const post = vi.fn((_: string, body: { sku: string }) => (body.sku === 'B' ? throwError(() => ({ message: 'SKU already used' })) : of({ success: true, data: { id: 'p1' } })));
    const put = vi.fn(() => of({ success: true }));
    TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: { post, put } }] });
    const f = TestBed.createComponent(BulkUploadComponent);
    f.componentRef.setInput('categories', categories);
    f.detectChanges();
    const c = f.componentInstance;
    const done: number[] = [];
    c.imported.subscribe(n => done.push(n));

    c.load([head, 'A,Rice,Groceries,1 kg,50,,,तांदूळ', 'B,Salt,Groceries,1 kg,20,,,', 'C,Bad,Toys,1 kg,20,,,'].join('\n'));
    c.importRows();

    expect(post).toHaveBeenCalledTimes(2);
    expect(put).toHaveBeenCalledWith('/admin/catalog/products/p1/translations/mr', { name: 'तांदूळ', description: null });
    expect(c.results().map(r => r.message)).toEqual(['Created', 'SKU already used']);
    expect(done).toEqual([1]);
    expect(c.finished()).toBe(true);
  });
});
