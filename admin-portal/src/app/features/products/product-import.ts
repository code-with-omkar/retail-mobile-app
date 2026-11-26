import { csvLine, parseCsv } from '../../shared/utils/csv';

export const COLUMNS = ['sku', 'name', 'category', 'unit', 'price', 'mrp', 'description', 'name_marathi'] as const;
export const MAX_ROWS = 500;

export interface ImportCategory { id: string; name: string; }

export interface ImportRow {
  line: number;
  sku: string; name: string; category: string; unit: string; description: string; nameMarathi: string;
  categoryId: string; price: number; mrp: number | null;
  /** Why the row cannot be created; empty when it can. */
  problems: string[];
}

/** The file an administrator downloads, fills in and uploads. The first mark lets Excel read Marathi. */
export function templateCsv(categories: ImportCategory[]): string {
  const category = categories[0]?.name ?? 'Groceries';
  return '﻿' + [
    csvLine([...COLUMNS]),
    csvLine(['SAMPLE-001', 'Sample rice', category, '1 kg', 89, 99, 'Replace these two sample rows with your products', 'तांदूळ']),
    csvLine(['SAMPLE-002', 'Sample salt', category, '500 g', 22, '', 'MRP and Marathi name are optional', '']),
  ].join('\r\n') + '\r\n';
}

/** Rows read from an uploaded file, each checked the way the server will check it. Throws when the file itself cannot be used. */
export function readImport(text: string, categories: ImportCategory[]): ImportRow[] {
  const [header, ...lines] = parseCsv(text);
  if (!header) throw new Error('The file is empty.');
  const names = header.map(h => h.trim().toLowerCase());
  const missing = ['sku', 'name', 'category', 'unit', 'price'].filter(c => !names.includes(c));
  if (missing.length) throw new Error('The first line must name the columns. Missing: ' + missing.join(', ') + '.');
  if (!lines.length) throw new Error('The file has no products under the column names.');
  if (lines.length > MAX_ROWS) throw new Error(`Upload at most ${MAX_ROWS} products at a time.`);

  const seen = new Set<string>();
  return lines.map((cells, index) => {
    const value = (column: string) => (cells[names.indexOf(column)] ?? '').trim();
    const sku = value('sku'), name = value('name'), category = value('category'), unit = value('unit');
    const priceText = value('price'), mrpText = value('mrp');
    const price = Number(priceText), mrp = mrpText === '' ? null : Number(mrpText);
    const found = categories.find(c => c.name.toLowerCase() === category.toLowerCase());
    const problems: string[] = [];
    if (!sku) problems.push('SKU is missing');
    else if (sku.length > 64) problems.push('SKU is longer than 64');
    else if (seen.has(sku.toLowerCase())) problems.push('SKU appears twice in the file');
    seen.add(sku.toLowerCase());
    if (!name) problems.push('Name is missing');
    else if (name.length > 160) problems.push('Name is longer than 160');
    if (!found) problems.push(category ? `No category called "${category}"` : 'Category is missing');
    if (!unit) problems.push('Unit is missing');
    if (priceText === '' || !(price > 0) || price > 100000) problems.push('Price must be a number from 1 to 1,00,000');
    if (mrp !== null && (!(mrp > 0) || mrp < price)) problems.push('MRP must be empty or at least the price');
    return { line: index + 2, sku, name, category, unit, description: value('description'), nameMarathi: value('name_marathi'), categoryId: found?.id ?? '', price, mrp, problems };
  });
}
