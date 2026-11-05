import { t } from '../../core/i18n';
import { blankDocument, DocumentData, DocumentForm, Line, LineKind, LineScope } from '../../core/documents';
import { MenuCategory, PosBarcode, PosPlace, PosSettings, SaleMenu } from '../../core/pos';

/** A line of the order being taken: a product of the menu, its price and how many. */
export interface OrderLine {
  productId: string;
  name: string;
  price: number;
  quantity: number;
  /** The lot, for a product that tracks lots: two lots of a product are two lines. */
  lot?: string | null;
}

/** One more of a product: its line grows, or a new line is added at the end. */
export function addToOrder(lines: readonly OrderLine[], product: { productId: string; name: string | null; price: number | null; lot?: string | null }): OrderLine[] {
  const same = (l: OrderLine) => l.productId === product.productId && (l.lot ?? null) === (product.lot ?? null);
  return lines.some(same)
    ? lines.map(l => (same(l) ? { ...l, quantity: l.quantity + 1 } : l))
    : [...lines, { productId: product.productId, name: product.name ?? '', price: product.price ?? 0, quantity: 1, lot: product.lot ?? null }];
}

/** The product of what the scanner typed: the barcode as it is, without the spaces a scanner may add. */
export function scanned(barcodes: readonly PosBarcode[], typed: string): PosBarcode | null {
  const code = typed.trim();
  return code ? barcodes.find(b => b.barcode === code) ?? null : null;
}

/** A line's quantity changed by a step; at zero the line goes. */
export function changeQuantity(lines: readonly OrderLine[], productId: string, step: number, lot: string | null = null): OrderLine[] {
  return lines.map(l => (l.productId === productId && (l.lot ?? null) === lot ? { ...l, quantity: l.quantity + step } : l)).filter(l => l.quantity > 0);
}

/** An amount with its VAT (a rate in percent), to the cent: what the guest pays. */
export function withVat(amount: number, rate: number): number {
  return Math.round(amount * (100 + rate)) / 100;
}

/** The order's total with VAT, line by line to the cent as the bill adds it up. */
export function orderTotal(lines: readonly OrderLine[], rate: number | ((line: OrderLine) => number) = 0): number {
  const of = typeof rate === 'number' ? () => rate : rate;
  return lines.reduce((sum, l) => sum + Math.round(withVat(l.price * l.quantity, of(l)) * 100), 0) / 100;
}

/** What the guest gets back: received less due, never below zero; null while less than due is received. */
export function changeDue(received: number, due: number): number | null {
  const cents = Math.round(received * 100) - Math.round(due * 100);
  return cents < 0 ? null : cents / 100;
}

/** A key of the on-screen keypad applied to the amount typed: a digit, the decimal point (once, two decimals at most), back, clear. */
export function keypad(value: string, key: string): string {
  if (key === 'clear') {
    return '';
  }
  if (key === 'back') {
    return value.slice(0, -1);
  }
  if (key === '.') {
    return value.includes('.') ? value : (value || '0') + '.';
  }
  if (!/^\d$/.test(key) || /\.\d{2}$/.test(value)) {
    return value;
  }
  return value === '0' ? key : value + key;
}

/**
 * The menus open now as one list of categories: a category in several open menus shows once, its products once each, in the order the
 * menus give them.
 */
export function menuCategories(menus: readonly SaleMenu[]): MenuCategory[] {
  const byId = new Map<string, MenuCategory>();
  for (const category of menus.flatMap(m => m.categories)) {
    const seen = byId.get(category.categoryId);
    if (!seen) {
      byId.set(category.categoryId, { ...category, products: [...category.products] });
    } else {
      seen.products.push(...category.products.filter(p => !seen.products.some(q => q.productId === p.productId)));
    }
  }
  return [...byId.values()];
}

/** The property names a table's place on the plan is read from (the TABLE type's properties in the base pack). */
export const POSITION_X = 'Position X';
export const POSITION_Y = 'Position Y';
export const SEATS = 'Seats';

export function numberProperty(place: PosPlace, name: string): number | null {
  const value = place.properties.find(p => p.name === name)?.value;
  const n = typeof value === 'number' ? value : typeof value === 'string' && value.trim() !== '' ? Number(value) : NaN;
  return Number.isFinite(n) ? n : null;
}

export interface PlacedTable {
  place: PosPlace;
  x: number;
  y: number;
  seats: number | null;
}

/** The plan's grid step (px) for tables with no place of their own. */
export const GRID = 150;

/**
 * Where each table of a hall stands on the plan: its own position when it has one, otherwise the next free cell of a grid (row by row,
 * `columns` wide), so a hall nobody arranged yet still shows every table.
 */
export function layoutTables(tables: readonly PosPlace[], columns = 6): PlacedTable[] {
  let cell = 0;
  return tables.map(place => {
    const x = numberProperty(place, POSITION_X);
    const y = numberProperty(place, POSITION_Y);
    const seats = numberProperty(place, SEATS);
    if (x !== null && y !== null) {
      return { place, x, y, seats };
    }
    const at = cell++;
    return { place, x: (at % columns) * GRID + 16, y: Math.floor(at / columns) * GRID + 16, seats };
  });
}

/** The halls of the plan (places that hold places) and the tables in each; tables right under the point of sale come under "All". */
export function hallsOf(places: readonly PosPlace[]): { id: string | null; name: string; tables: PosPlace[] }[] {
  const halls = places.filter(p => p.holdsPlaces);
  const tables = places.filter(p => !p.holdsPlaces);
  const loose = tables.filter(t => !halls.some(h => h.id === t.parentId));
  return [
    ...halls.map(h => ({ id: h.id as string | null, name: h.name ?? h.code ?? '', tables: tables.filter(t => t.parentId === h.id) })),
    ...(loose.length ? [{ id: null, name: halls.length ? t('Other places') : t('Tables'), tables: loose }] : []),
  ];
}

/** What a missing setting is called on screen. */
export const SETTING_NAMES: Readonly<Record<string, string | undefined>> = {
  billTypeId: 'the bill document type',
  moneyInTypeId: 'the money-in document type',
  moneyOutTypeId: 'the money-out document type',
  cashDeskId: 'the cash desk',
  walkInClientId: 'the walk-in client',
};

/** The line kind of a document type whose lines are of a register (its table), e.g. the products of a bill, the cash desk of a payment. */
export function lineKindOf(form: DocumentForm, table: string): LineKind | null {
  return form.lineKinds.find(k => k.scopes.some(s => s.table === table)) ?? null;
}

/** The (line kind, scope) pairs a document type's lines can carry a register's elements on, e.g. goods and services for products. */
export function scopesOf(form: DocumentForm, table: string): { kind: LineKind; scope: LineScope }[] {
  return form.lineKinds.flatMap(kind => kind.scopes.filter(s => s.table === table).map(scope => ({ kind, scope })));
}

/**
 * How a product goes on a bill line: its line kind and scope (the one whose elements hold the product: goods move stock, services do not;
 * the posting scenario is chosen by it), its unit and VAT group from the line proposal (B5).
 */
export interface ProductTerms {
  lineTypeId: string;
  typeConfId: string;
  unitId: string | null;
  vatGroupId: string | null;
}

/**
 * The bill of an order (a document of the point of sale's bill type): to the walk-in client, at the table, paid by the default method, a
 * line per product leaving the point of sale at the menu's price. A problem, naming the product, when one has no unit to sell in.
 */
export function billOf(order: readonly OrderLine[], bill: { typeId: string; categoryId: string }, settings: PosSettings,
  pointOfSale: { id: string; name: string | null }, tableId: string | null, terms: ReadonlyMap<string, ProductTerms>): DocumentData | { problem: string } {
  const missing = order.find(l => !terms.get(l.productId)?.unitId);
  if (missing) {
    return { problem: `${missing.name} has no unit to sell in: give it one in the products register.` };
  }
  return {
    ...blankDocument(bill.typeId, bill.categoryId), toId: settings.walkInClientId, toName: settings.walkInClient, tableId,
    paymentMethodId: settings.paymentMethodId,
    transactions: order.map((l): Line => {
      const t = terms.get(l.productId)!;
      return { typeId: t.lineTypeId, typeConfId: t.typeConfId, elementId: l.productId, elementName: l.name, unitId: t.unitId, vatGroupId: t.vatGroupId,
        quantity: l.quantity, price: l.price, fromId: pointOfSale.id, fromName: pointOfSale.name, lot: l.lot ?? null };
    }),
  };
}

/**
 * Money in or out of the cash desk outside a bill: a document of the money type, the person on the side the type names (who brings the
 * money or takes it), one cash-desk line of the amount at no VAT, the reason as its description.
 */
export function cashMoveOf(type: { typeId: string; categoryId: string; form: DocumentForm }, party: { id: string; name: string }, cashDesk: { id: string; name: string | null },
  amount: number, reason: string): DocumentData | { problem: string } {
  const kind = lineKindOf(type.form, 'tbl_cost_center');
  if (!kind) {
    return { problem: `${type.form.name} has no cash-desk line.` };
  }
  const zero = type.form.vatGroups.find(v => v.rate === 0)?.id ?? null;
  const scope = kind.scopes.find(s => s.table === 'tbl_cost_center')?.confId ?? null;
  const side = type.form.from ? { fromId: party.id, fromName: party.name } : { toId: party.id, toName: party.name };
  return {
    ...blankDocument(type.typeId, type.categoryId), ...side, description: reason.trim() ? { en: reason.trim() } : null,
    transactions: [{ typeId: kind.transactionTypeId, typeConfId: scope, elementId: cashDesk.id, elementName: cashDesk.name, quantity: 1, price: amount, vatGroupId: zero }],
  };
}
