/** Electric Yellow accent pairs (docs/DESIGN_SYSTEM.md): `solid` for headers/chips, `tint` for card surfaces. Text on both is ink #141118. */
export interface Accent {
  solid: string;
  tint: string;
  name: string;
}

export const ACCENTS: Accent[] = [
  { name: 'yellow', solid: '#FFD400', tint: '#FFF1A8' },
  { name: 'green', solid: '#02F34C', tint: '#B9FFCF' },
  { name: 'lavender', solid: '#B9A8FF', tint: '#EAE4FF' },
  { name: 'pink', solid: '#FB1A8E', tint: '#FFC6E2' },
];

/** Even/odd card rule: 1st yellow, 2nd green, 3rd lavender, 4th pink, then repeat. */
export function cycleAccent(index: number): Accent {
  return ACCENTS[((index % ACCENTS.length) + ACCENTS.length) % ACCENTS.length];
}

const ROLE_ACCENTS: Record<string, Accent> = {
  admin: ACCENTS[3],
  applicationadmin: ACCENTS[2],
  storestaff: ACCENTS[0],
  customer: ACCENTS[1],
};

/** Stable accent for any string key (group name, id, ...). */
export function accentFor(key: string | undefined | null): Accent {
  const text = key ?? '';
  let hash = 0;
  for (let i = 0; i < text.length; i++) hash = (hash * 31 + text.charCodeAt(i)) >>> 0;
  return ACCENTS[hash % ACCENTS.length];
}

export function roleAccent(role: string | undefined | null): Accent {
  const key = (role ?? '').replace(/[\s_-]/g, '').toLowerCase();
  return ROLE_ACCENTS[key] ?? accentFor(key);
}

/** Resource prefix of a permission code, e.g. "orders:read" -> "orders". */
export function permissionGroup(code: string | undefined | null): string {
  const value = (code ?? '').trim();
  const split = value.split(/[:._]/)[0];
  return split || 'general';
}

export function initialsOf(name: string | undefined | null): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (!parts.length) return '?';
  return parts.slice(0, 2).map(part => part[0]).join('').toUpperCase();
}
