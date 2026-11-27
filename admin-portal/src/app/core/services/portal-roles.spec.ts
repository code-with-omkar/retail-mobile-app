import { portalRefusal } from './auth.service';
import { greetingFor } from '../../layout/header/header.component';
import { Role } from '../models/domain.model';

describe('who may sign in to the portal', () => {
  it('lets administrators and store staff in', () => {
    for (const role of [Role.ApplicationAdmin, Role.Admin, Role.StoreStaff]) expect(portalRefusal(role)).toBeNull();
  });

  it('sends delivery partners and customers to their own apps', () => {
    expect(portalRefusal(Role.DeliveryPartner)).toContain('delivery app');
    expect(portalRefusal(Role.Customer)).toContain('QuickCart app');
    expect(portalRefusal(undefined)).toContain('cannot use');
  });
});

describe('the greeting', () => {
  it('follows the time of day', () => {
    const at = (hour: number) => greetingFor(new Date(2026, 10, 25, hour, 5));
    expect([at(0), at(11), at(12), at(16), at(17), at(22)]).toEqual(['Good morning', 'Good morning', 'Good afternoon', 'Good afternoon', 'Good evening', 'Good evening']);
  });
});
