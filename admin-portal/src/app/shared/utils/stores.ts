import { Store } from '../../core/models/domain.model';

/** The open stores in name order. The server sends only the caller's own, so the first one is a safe default for anyone. */
export function storeChoices(stores: Store[] | null | undefined): Store[] {
  return (stores ?? []).filter(s => s.isActive).sort((a, b) => a.name.localeCompare(b.name));
}

/** The locality written in an address: "Sector 20, Kharghar, Navi Mumbai 410209" gives "Kharghar", "12 Marine Drive" gives "Marine Drive". */
export function areaOf(address: string | null | undefined): string {
  const parts = (address ?? '').split(',').map(p => p.trim()).filter(Boolean);
  if (!parts.length) return 'Other';
  const part = parts.length >= 3 ? parts[parts.length - 2] : parts[parts.length - 1];
  return part.replace(/\b\d{6}\b/, '').replace(/^\d+[A-Za-z]?\s+/, '').trim() || 'Other';
}
