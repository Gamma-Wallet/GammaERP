import { t } from '../../core/i18n';
// The configuration screens' words: model and column names, and the meanings of coded columns (display only; the founder, 2026-10-31:
// named choices kept in the screen text, the engine reads the numbers).
import { SelectOption } from '../../ui/select';

/** The configuration models' names on the screens. */
export const MODEL_NAMES: Record<string, string> = {
  'vat-groups': t('VAT groups'), 'payment-methods': t('Payment methods'), 'payment-method-cost-centers': t('Payment methods by cost centre'),
  'payment-method-document-types': t('Payment methods per document type'), 'document-type-from': t('Document "from" sides'), 'document-type-to': t('Document "to" sides'),
  'document-type-transaction-types': t('Transaction types of document types'), 'document-type-vat-groups': t('VAT groups of document types'),
  'transaction-fills': t('How lines are filled'), 'transaction-type-scopes': t('Transaction scopes'), 'transaction-type-from': t('Line "from" sides'),
  'transaction-type-to': t('Line "to" sides'), scenarios: t('Posting scenarios'), 'scenario-lines': t('Scenario lines'), templates: t('Print templates'),
  'sale-prices': t('Sale prices'), 'sale-price-types': t('Sale prices by partner'), 'point-of-sale-settings': t('Point-of-sale settings'), 'group-purchase-settings': t('Purchases from the group'), 'pos-shows': t('Shows'), 'depreciation-books': t('Depreciation books'), 'depreciation-methods': t('Depreciation methods'), 'depreciation-scenarios': t('Depreciation scenarios'), 'depreciation-scenario-lines': t('Depreciation scenario lines'), 'balance-sheet-lines': t('Balance sheet lines'), 'profit-loss-lines': t('Profit and loss lines'), 'document-type-flows': t('Document flows'), 'sale-menus': t('Sale menus'), 'sale-menu-categories': t('Sale menu categories'),
  'unit-conversions': t('Unit conversions'), 'product-recipes': t('Recipes'), 'vat-return-boxes': t('VAT return boxes'), 'vat-return-box-rules': t('VAT return box rules'),
  menus: t('Menu'), 'menu-access': t('Menu access'), 'dashboard-items': t('Dashboard items'), 'migration-items': t('Migration steps'), 'closed-periods': t('Closed periods'), 'opportunity-stages': t('Opportunity stages'), 'approval-limits': t('Approval limits'), 'production-routes': t('Production routes'), 'repair-stages': t('Repair stages'), 'chart-mappings': t('Account mapping'), 'bank-statement-formats': t('Bank statement formats'), 'user-roles': t('Roles'),
};

const WORDS: Record<string, string> = { vat: 'VAT', lcu: 'LCU', pos: 'POS', id: '' };

/** Names that differ in one model: a price's stored Value LCU is right only on the day it was set (the founder, 2026-11-04). */
const MODEL_LABELS: Record<string, Record<string, string>> = {
  'sale-prices': { valueLcu: t('Value LCU when set') },
  'sale-price-types': { valueLcu: t('Value LCU when set') },
};

/** A column's name for people: "vatGroupId" → "VAT group", "valueLcu" → "Value LCU". */
export function columnLabel(name: string, model?: string): string {
  if (model && MODEL_LABELS[model]?.[name]) {
    return MODEL_LABELS[model][name];
  }
  if (name === 'idSup') {
    return t('Under');
  }
  const words = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').split(' ').map(w => w.toLowerCase());
  const shown = words.map(w => (w in WORDS ? WORDS[w] : w)).filter(Boolean);
  const text = shown.join(' ');
  return t(text.charAt(0).toUpperCase() + text.slice(1));
}

const FILL: SelectOption<number | null>[] = [
  { value: null, label: '—' }, { value: 0, label: t('Typed') }, { value: 1, label: t('Chosen from the list') }, { value: 2, label: t('Not needed') },
];

/** Coded columns: model → column → its choices. A column not named here is shown as its number. */
export const CODED: Record<string, Record<string, SelectOption<number | null>[]>> = {
  'transaction-fills': Object.fromEntries(
    ['transactionType', 'vatGroup', 'element', 'unit', 'location', 'unitPriceWithoutVat', 'discount', 'quantity', 'priceWithoutVat', 'vat'].map(c => [c, FILL]),
  ),
  'document-type-from': { costLayers: costLayers() },
  'document-type-to': { costLayers: costLayers() },
  'transaction-type-scopes': { stockDirection: [{ value: 0, label: t('No stock') }, { value: 1, label: t('Moves stock (from down, to up)') },
    { value: 2, label: t('Out of the from side only (used, taken apart)') }, { value: 3, label: t('Into the to side only (made, recovered)') },
    { value: 4, label: t('Waste: out of the from side, an expense') }, { value: 5, label: t('Value only: no stock, its cost share to an expense') }] },
  'vat-return-boxes': { amount: [{ value: 1, label: t('Taxable value') }, { value: 2, label: t('VAT') }] },
};

function costLayers(): SelectOption<number | null>[] {
  return [
    { value: 0, label: t('None') }, { value: 1, label: t('Take (oldest first)') }, { value: 2, label: t('Make (a purchase)') }, { value: 3, label: t('Receive (a transfer)') },
    { value: 4, label: t('Put back (a reversal)') }, { value: 5, label: t('Take off (a reversal)') }, { value: 6, label: t('Make from used') },
    { value: 7, label: t('Receive converted') },
  ];
}

/** The text a coded value shows; the number itself when it has no name. */
export function codedText(model: string, column: string, value: unknown): string | null {
  const options = CODED[model]?.[column];
  if (!options || value === null || value === undefined) {
    return null;
  }
  return options.find(o => o.value === value)?.label ?? String(value);
}
