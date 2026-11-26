import { areaOf, storeChoices } from './stores';
import { TIPS, tipForUrl } from '../components/help-tip/tips';
import { MENU } from '../../layout/shell/menu';

describe('store choices', () => {
  it('keeps the open stores in name order', () => {
    const store = (id: string, name: string, isActive = true) => ({ id, name, isActive }) as never;
    expect(storeChoices([store('2', 'Panvel'), store('1', 'Belapur'), store('3', 'Closed', false)]).map((s: any) => s.name)).toEqual(['Belapur', 'Panvel']);
    expect(storeChoices(null)).toEqual([]);
  });

  it('reads the area from an address', () => {
    expect(['Sector 20, Kharghar, Navi Mumbai 410209', 'Old Panvel Market Road, Panvel 410206', 'Sector 11, CBD Belapur, Navi Mumbai 400614', '12 Marine Drive', '', null].map(areaOf))
      .toEqual(['Kharghar', 'Panvel', 'CBD Belapur', 'Marine Drive', 'Other', 'Other']);
  });
});

describe('help in two languages', () => {
  it('every menu screen has English and Marathi help', () => {
    for (const item of MENU) {
      const key = tipForUrl(item.route!);
      expect(key, item.label).not.toBeNull();
      expect(TIPS[key!].en.length).toBeGreaterThan(10);
      expect(TIPS[key!].mr).toMatch(/[\u0900-\u097F]/);
    }
    expect(tipForUrl('/admin/dashboard')).toBe('dashboard');
    expect(tipForUrl('/admin/users?page=2')).toBe('admin/users');
    expect(tipForUrl('/login')).toBeNull();
  });
});
