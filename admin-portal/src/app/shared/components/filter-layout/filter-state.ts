import { signal } from '@angular/core';

export interface FilterOption { value: string; label: string; count?: number; }

export interface FilterGroup {
  key: string;
  label: string;
  options: FilterOption[];
  /** One choice only (such as a sort order). Otherwise any number can be ticked. */
  single?: boolean;
  /** A single choice that always has a value, so it has no "All" (such as the store being viewed). */
  required?: boolean;
}

/** The ticked values of each group; a group with none ticked shows everything. */
export type Selection = Record<string, string[]>;

const PREFIX = 'qc-filters:';

/**
 * A screen's chosen filters, remembered in this browser. `defaults` is what "Clear all" returns to.
 */
export function filterState(screen: string, defaults: Selection = {}) {
  const selected = signal<Selection>({ ...defaults, ...read(screen) });
  const save = (next: Selection) => {
    selected.set(next);
    try { localStorage.setItem(PREFIX + screen, JSON.stringify(next)); } catch { /* a private window: remembered until reload */ }
  };
  return {
    selected: selected.asReadonly(),
    set: save,
    clear: () => save({ ...defaults }),
    /** The ticked values of one group. */
    of: (key: string): string[] => selected()[key] ?? [],
    /** Whether a row with this value passes the group. */
    allows: (key: string, value: string): boolean => { const ticked = selected()[key] ?? []; return !ticked.length || ticked.includes(value); },
  };
}

function read(screen: string): Selection {
  try {
    const found: unknown = JSON.parse(localStorage.getItem(PREFIX + screen) ?? '{}');
    if (!found || typeof found !== 'object' || Array.isArray(found)) return {};
    return Object.fromEntries(Object.entries(found).filter(([, v]) => Array.isArray(v) && v.every(x => typeof x === 'string'))) as Selection;
  } catch { return {}; }
}

/**
 * Filtering a list that is already here. `by` says what each group looks at in a row. Returns the rows that pass every group, and how many
 * rows each value would show under the other groups.
 */
export function facets<T>(rows: readonly T[], selected: Selection, by: Record<string, (row: T) => string>) {
  const passes = (row: T, skip?: string) => Object.entries(by).every(([key, of]) => key === skip || !selected[key]?.length || selected[key].includes(of(row)));
  return {
    shown: rows.filter(row => passes(row)),
    count: (key: string, value: string) => rows.filter(row => by[key](row) === value && passes(row, key)).length,
  };
}

/** Builds a value again only when one of the things it is built from changes, for screens that keep their choices in plain fields. */
export function cached<T>(from: () => unknown[], build: () => T): () => T {
  let last: unknown[] | null = null;
  let value: T;
  return () => {
    const now = from();
    if (!last || now.length !== last.length || now.some((one, i) => one !== last![i])) { last = now; value = build(); }
    return value;
  };
}
