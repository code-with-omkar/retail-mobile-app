import { actionIcon, decorate, statusIcon } from './decorate';

describe('list icons and card tools', () => {
  it('knows the icon of each action and status', () => {
    expect([actionIcon('Accept'), actionIcon('Cancel order'), actionIcon('Switch off'), actionIcon('Pay ₹10'), actionIcon('Add →'), actionIcon('← Remove')]).toEqual(['check', 'ban', 'power', 'rupee', 'plus', 'minus']);
    expect(actionIcon('Something else')).toBeNull();
    expect(statusIcon('Delivered')).toEqual(['circleCheck', 'done']);
    expect(statusIcon('Cancelled (refunded)')).toEqual(['circleCross', 'bad']);
    expect(statusIcon('Store Staff')).toBeNull();
  });

  it('marks list buttons and statuses, keeping the words as the tooltip, and follows a change of words', () => {
    const root = document.createElement('div');
    root.innerHTML = '<app-data-table><table><tbody><tr><td><span class="badge">Active</span><span class="chip">Customer</span><button class="act">Switch off</button></td></tr></tbody></table></app-data-table>';
    decorate(root);
    const button = root.querySelector<HTMLElement>('button.act')!;
    expect([button.dataset['ic'], button.title, button.textContent]).toEqual(['power', 'Switch off', 'Switch off']);
    expect(root.querySelector<HTMLElement>('.badge')!.title).toBe('Active');
    expect(root.querySelector<HTMLElement>('.chip')!.dataset['ic']).toBeUndefined();
    button.textContent = 'Switch on';
    decorate(root);
    expect(button.title).toBe('Switch on');
  });

  it('a card can be minimised and maximised, one at a time, and gets its tools once', () => {
    const root = document.createElement('div');
    root.innerHTML = '<section class="card"><h3>Rules</h3><p>Body</p></section>';
    decorate(root);
    decorate(root);
    const card = root.querySelector<HTMLElement>('.card')!;
    const [min, max] = Array.from(card.querySelectorAll<HTMLButtonElement>('.card-tool'));
    expect(card.querySelectorAll('.card-tools')).toHaveLength(1);
    min.click();
    expect(card.classList).toContain('card-min');
    expect(min.title).toBe('Show card');
    max.click();
    expect(card.classList).toContain('card-max');
    expect(card.classList).not.toContain('card-min');
    expect(min.title).toBe('Minimize card');
    max.click();
    expect(card.classList).not.toContain('card-max');
  });
});
