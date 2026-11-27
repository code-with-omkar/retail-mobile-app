/** Why a store declines an order; the keys are the server's. */
export const REJECTION_REASONS = [
  { key: 'OutOfStock', name: 'Items out of stock' },
  { key: 'StoreClosed', name: 'Store closed' },
  { key: 'TooBusy', name: 'Too busy' },
  { key: 'AddressProblem', name: 'Address problem' },
  { key: 'Other', name: 'Other' },
];

export const rejectionName = (key: string | null | undefined): string => REJECTION_REASONS.find(r => r.key === key)?.name ?? 'Not recorded';

/** Why a store cancels an order it accepted; the keys are the server's. */
export const CANCEL_REASONS = [
  { key: 'CustomerRequest', name: 'Customer asked' },
  { key: 'DeliveryFailed', name: 'Delivery failed' },
  { key: 'OutOfStock', name: 'Items out of stock' },
  { key: 'StoreProblem', name: 'Store problem' },
  { key: 'Other', name: 'Other' },
];

const FAILURES: Record<string, string> = { CustomerUnavailable: 'Customer not available', WrongAddress: 'Wrong address', CustomerRefused: 'Customer refused it', VehicleProblem: 'Vehicle problem', Other: 'Other' };

export const cancelName = (key: string | null | undefined): string => CANCEL_REASONS.find(r => r.key === key)?.name ?? '';
export const failureName = (key: string | null | undefined): string => FAILURES[key ?? ''] ?? 'Not recorded';
