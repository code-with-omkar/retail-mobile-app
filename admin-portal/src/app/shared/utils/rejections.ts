/** Why a store declines an order; the keys are the server's. */
export const REJECTION_REASONS = [
  { key: 'OutOfStock', name: 'Items out of stock' },
  { key: 'StoreClosed', name: 'Store closed' },
  { key: 'TooBusy', name: 'Too busy' },
  { key: 'AddressProblem', name: 'Address problem' },
  { key: 'Other', name: 'Other' },
];

export const rejectionName = (key: string | null | undefined): string => REJECTION_REASONS.find(r => r.key === key)?.name ?? 'Not recorded';
