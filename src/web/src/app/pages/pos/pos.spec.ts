import { describe, expect, it } from 'vitest';
import { DocumentForm } from '../../core/documents';
import { PosPlace, PosSettings } from '../../core/pos';
import { addToOrder, scanned, billOf, cashMoveOf, changeDue, changeQuantity, hallsOf, keypad, layoutTables, menuCategories, orderTotal, scopesOf } from './pos-logic';

const place = (id: string, change: Partial<PosPlace> = {}): PosPlace => ({
  id, parentId: null, code: id, name: id, holdsPlaces: false, properties: [], openBills: 0, open: 0, waiters: [], ...change,
});

const settings: PosSettings = {
  pointOfSaleId: 'bar', billTypeId: 'psh', billType: 'Point-of-sale receipt', moneyInTypeId: 'map', moneyInType: null, moneyOutTypeId: 'mpp', moneyOutType: null,
  cashDeskId: 'cash', cashDesk: 'Arka', walkInClientId: 'walk-in', walkInClient: 'Klient', paymentMethodId: 'cash-method', paymentMethod: 'Cash', missing: [],
};

const form = (change: Partial<DocumentForm>): DocumentForm => ({
  typeId: 't', code: null, name: 'Point-of-sale payment', neverPosted: false, allowsPartialReversal: false, isReversal: false, from: null, to: null,
  paymentMethods: [], vatGroups: [{ id: 'vat0', code: null, name: '0%', rate: 0 }, { id: 'vat20', code: null, name: '20%', rate: 20 }], currencies: [],
  lineKinds: [{ transactionTypeId: 'desk-line', code: null, name: 'Cash desk', scopes: [{ confId: 'c', scopeId: 's', table: 'tbl_cost_center', name: 'Cash desks', stockDirection: 0 }],
    fill: null, from: null, to: null }], ...change,
});

describe('point of sale', () => {
  it('takes an order: a tap adds one, the steps change it, at zero the line goes, the total adds up to the cent', () => {
    let order = addToOrder([], { productId: 'beer', name: 'Birrë', price: 1.5 });
    order = addToOrder(order, { productId: 'beer', name: 'Birrë', price: 1.5 });
    order = addToOrder(order, { productId: 'coffee', name: 'Kafe', price: 0.7 });
    expect(order.map(l => [l.productId, l.quantity])).toEqual([['beer', 2], ['coffee', 1]]);
    expect(orderTotal(order)).toBe(3.7);
    expect(orderTotal(order, 20)).toBe(4.44);
    expect(changeQuantity(order, 'coffee', -1).map(l => l.productId)).toEqual(['beer']);
  });

  it('shows the change only once enough is received, and the keypad keeps two decimals', () => {
    expect(changeDue(10, 3.7)).toBe(6.3);
    expect(changeDue(3, 3.7)).toBeNull();
    expect(['1', '2', '.', '5', '0', '9'].reduce(keypad, '')).toBe('12.50');
    expect(keypad('', '.')).toBe('0.');
    expect(keypad('0', '7')).toBe('7');
    expect(keypad('12', 'back')).toBe('1');
  });

  it('merges the open menus into one list of categories', () => {
    const drinks = { categoryId: 'drinks', name: 'Drinks', products: [{ productId: 'beer', code: null, name: 'Birrë', categoryId: 'drinks', price: 1.5, currencyId: null }] };
    const more = { ...drinks, products: [...drinks.products, { productId: 'wine', code: null, name: 'Verë', categoryId: 'drinks', price: 3, currencyId: null }] };
    const menus = [{ id: 'm1', code: null, name: null, startsAt: null, endsAt: null, categories: [drinks] }, { id: 'm2', code: null, name: null, startsAt: null, endsAt: null, categories: [more] }];
    expect(menuCategories(menus).map(c => c.products.map(p => p.productId))).toEqual([['beer', 'wine']]);
  });

  it('places tables by their position, the others on a grid, and groups them by hall', () => {
    const placed = layoutTables([place('t1', { properties: [{ id: 'x', name: 'Position X', value: 300 }, { id: 'y', name: 'Position Y', value: '40' }] }), place('t2'), place('t3')], 2);
    expect(placed.map(t => [t.x, t.y])).toEqual([[300, 40], [16, 16], [166, 16]]);
    const halls = hallsOf([place('hall', { holdsPlaces: true }), place('t1', { parentId: 'hall' }), place('counter')]);
    expect(halls.map(h => [h.name, h.tables.map(t => t.id)])).toEqual([['hall', ['t1']], ['Other places', ['counter']]]);
  });

  it('writes the bill to the walk-in client at the table, each product on the line kind whose scope holds it', () => {
    const order = [{ productId: 'beer', name: 'Birrë', price: 1.5, quantity: 2 }, { productId: 'delivery', name: 'Delivery', price: 1, quantity: 1 }];
    const terms = new Map([['beer', { lineTypeId: 'item', typeConfId: 'goods', unitId: 'cope', vatGroupId: 'vat20' }],
      ['delivery', { lineTypeId: 'service-item', typeConfId: 'services', unitId: 'cope', vatGroupId: 'vat20' }]]);
    const bill = billOf(order, { typeId: 'psh', categoryId: 'pos' }, settings, { id: 'bar', name: 'Bar' }, 't1', terms);
    expect(bill).toMatchObject({ typeId: 'psh', categoryIds: ['pos'], toId: 'walk-in', tableId: 't1', paymentMethodId: 'cash-method',
      transactions: [{ typeId: 'item', typeConfId: 'goods', elementId: 'beer', unitId: 'cope', quantity: 2, price: 1.5, fromId: 'bar' },
        { typeId: 'service-item', typeConfId: 'services', elementId: 'delivery' }] });
    expect(billOf(order, { typeId: 'psh', categoryId: 'pos' }, settings, { id: 'bar', name: 'Bar' }, 't1', new Map())).toHaveProperty('problem');
    const kind = (code: string) => ({ transactionTypeId: code, code, name: code, scopes: [{ confId: code, scopeId: code, table: 'tbl_product', name: code, stockDirection: 0 }],
      fill: null, from: null, to: null });
    expect(scopesOf(form({ lineKinds: [kind('service-item'), kind('item')] }), 'tbl_product').map(s => s.scope.confId)).toEqual(['service-item', 'item']);
  });

  it('records money out to the person the type names, one cash-desk line at no VAT', () => {
    const out = cashMoveOf({ typeId: 'mpp', categoryId: 'pos', form: form({ to: { scopeId: 'staff', table: 'tbl_employee', name: 'Staff' } }) },
      { id: 'arben', name: 'Arben' }, { id: 'cash', name: 'Arka' }, 12.5, ' Ice ');
    expect(out).toMatchObject({ toId: 'arben', fromId: null, description: { en: 'Ice' }, transactions: [{ typeId: 'desk-line', typeConfId: 'c', elementId: 'cash', price: 12.5, vatGroupId: 'vat0' }] });
    const moneyIn = cashMoveOf({ typeId: 'map', categoryId: 'pos', form: form({ from: { scopeId: 'p', table: 'tbl_partner', name: 'Clients' } }) },
      { id: 'walk-in', name: 'Klient' }, { id: 'cash', name: 'Arka' }, 20, '');
    expect(moneyIn).toMatchObject({ fromId: 'walk-in', toId: null, description: null });
    expect(cashMoveOf({ typeId: 'x', categoryId: 'c', form: form({ lineKinds: [] }) }, { id: 'a', name: 'A' }, { id: 'cash', name: null }, 1, '')).toHaveProperty('problem');
  });
});

describe('scanning at a shop', () => {
  const oil = { barcode: '5901234123457', productId: 'oil', code: 'OIL', name: 'Oil', price: 5, currencyId: null };

  it('finds the product of what the scanner typed, spaces aside, and nothing for an empty scan or an unknown code', () => {
    expect(scanned([oil], ' 5901234123457\n')).toBe(oil);
    expect(scanned([oil], '123')).toBeNull();
    expect(scanned([oil], '   ')).toBeNull();
  });

  it('a second scan of a product makes its line grow', () => {
    expect(addToOrder(addToOrder([], oil), oil)).toEqual([{ productId: 'oil', name: 'Oil', price: 5, quantity: 2 }]);
  });
});
