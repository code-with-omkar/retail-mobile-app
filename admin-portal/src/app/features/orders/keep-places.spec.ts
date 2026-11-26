import { keepPlaces } from './orders.component';

const row = (id: string, status = 'Pending') => ({ id, status });

describe('orders keep their place', () => {
  it('the first list is shown as the server sent it', () => {
    expect(keepPlaces([], [row('a'), row('b')]).map(o => o.id)).toEqual(['a', 'b']);
  });

  it('an accepted order stays in its row although the server now sorts it lower, with its new status', () => {
    const next = keepPlaces([row('a'), row('b'), row('c')], [row('b'), row('c'), row('a', 'Accepted')]);

    expect(next.map(o => o.id)).toEqual(['a', 'b', 'c']);
    expect(next[0].status).toBe('Accepted');
  });

  it('a new order appears at the top and one that is gone disappears', () => {
    expect(keepPlaces([row('a'), row('b')], [row('n'), row('b')]).map(o => o.id)).toEqual(['n', 'b']);
  });
});
