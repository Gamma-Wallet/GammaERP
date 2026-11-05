import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { ChartSpec } from '../ui/chart-option';
import { Chart } from '../ui/chart';
import { Chip, Chips } from '../ui/chips';
import { Button, Checkbox, Field, Input } from '../ui/controls';
import { Dialogs } from '../ui/dialog';
import { formatNumber } from '../ui/format';
import { Icon } from '../ui/icon';
import { PropertyForm } from '../ui/property-form';
import { Property, PropertyKind, PropertyValues, toSaveJson } from '../ui/property-form.logic';
import { Select, SelectOption } from '../ui/select';
import { Kpi, Status, StatusKind } from '../ui/status';
import { CellTemplate, Column, Sort, Table, TreeTable } from '../ui/table';
import { Tab, Tabs } from '../ui/tabs';
import { Toasts } from '../ui/toast';

// Made-up sample data only.
interface SampleDocument {
  id: string;
  number: string;
  date: string;
  partner: string;
  amount: number;
  state: 'posted' | 'held' | 'reversed';
}

interface SampleAccount {
  id: string;
  code: string;
  name: string;
  debit: number | null;
  credit: number | null;
  children?: SampleAccount[];
}

const PARTNERS = ['North Bakery', 'Blue Harbour Cafe', 'Hill Street Market', 'Olive & Co', 'Riverside Hotel', 'Lantern Bar'];
const STATES: SampleDocument['state'][] = ['posted', 'posted', 'posted', 'held', 'reversed'];
const DOCUMENTS: SampleDocument[] = Array.from({ length: 64 }, (_, i) => ({
  id: `d${i + 1}`,
  number: `S-${String(1001 + i)}`,
  date: `2026-10-${String((i % 30) + 1).padStart(2, '0')}`,
  partner: PARTNERS[(i * 7) % PARTNERS.length],
  amount: Math.round(((i * 7919) % 50000) + 1250) / 100,
  state: STATES[i % STATES.length],
}));

const ACCOUNTS: SampleAccount[] = [
  { id: 'a1', code: '1', name: 'Assets', debit: 18450, credit: 6200, children: [
    { id: 'a11', code: '10', name: 'Cash and bank', debit: 12450, credit: 5200, children: [
      { id: 'a111', code: '100', name: 'Cash desk', debit: 4450, credit: 3200 },
      { id: 'a112', code: '101', name: 'Bank current account', debit: 8000, credit: 2000 },
    ] },
    { id: 'a12', code: '12', name: 'Stock', debit: 6000, credit: 1000 },
  ] },
  { id: 'a2', code: '2', name: 'Liabilities', debit: 1500, credit: 9800, children: [
    { id: 'a21', code: '20', name: 'Suppliers', debit: 1500, credit: 7300 },
    { id: 'a22', code: '24', name: 'VAT payable', debit: null, credit: 2500 },
  ] },
  { id: 'a7', code: '7', name: 'Income', debit: null, credit: 3950 },
];

const PROPERTIES: Property[] = [
  { id: 'p1', name: { en: 'Brand' }, required: true, kind: PropertyKind.Text, sortOrder: 1, choices: [] },
  { id: 'p2', name: { en: 'Weight (kg)' }, description: { en: 'With a point for decimals, e.g. 0.75' }, required: false,
    kind: PropertyKind.Number, sortOrder: 2, choices: [] },
  { id: 'p3', name: { en: 'Best before' }, required: false, kind: PropertyKind.Date, sortOrder: 3, choices: [] },
  { id: 'p4', name: { en: 'Delivered at' }, required: false, kind: PropertyKind.DateTime, sortOrder: 4, choices: [] },
  { id: 'p5', name: { en: 'Packaging' }, required: true, kind: PropertyKind.List, sortOrder: 5,
    choices: [{ id: 'c1', name: { en: 'Bottle' } }, { id: 'c2', name: { en: 'Can' } }, { id: 'c3', name: { en: 'Keg' } }] },
  { id: 'p6', name: { en: 'Allergens' }, required: false, kind: PropertyKind.MultiList, sortOrder: 6,
    choices: [{ id: 'c4', name: { en: 'Gluten' } }, { id: 'c5', name: { en: 'Milk' } }, { id: 'c6', name: { en: 'Nuts' } }] },
  { id: 'p7', name: { en: 'Default supplier' }, required: false, kind: PropertyKind.Lookup, sortOrder: 7, elementsId: 'partners', choices: [] },
  { id: 'p8', name: { en: 'Warehouse location' }, required: false, kind: PropertyKind.Location, sortOrder: 8, choices: [] },
  { id: 'p9', name: { en: 'Barcodes' }, required: false, kind: PropertyKind.MultiText, sortOrder: 9, choices: [] },
  { id: 'p10', name: { en: 'Photo' }, required: false, kind: PropertyKind.Photo, sortOrder: 10, choices: [] },
  { id: 'p11', name: { en: 'Sold as' }, description: { en: 'Each choice shows its own fields.' }, required: false,
    kind: PropertyKind.ActivatingList, sortOrder: 11, choices: [{ id: 'c7', name: { en: 'Draught' } }, { id: 'c8', name: { en: 'Cocktail ingredient' } }] },
  { id: 'p12', name: { en: 'Glass size (l)' }, required: true, kind: PropertyKind.Number, sortOrder: 12, shownForChoiceId: 'c7', choices: [] },
  { id: 'p13', name: { en: 'Measure per cocktail (cl)' }, required: true, kind: PropertyKind.Number, sortOrder: 13, shownForChoiceId: 'c8', choices: [] },
];

const STATE_KIND: Record<SampleDocument['state'], StatusKind> = { posted: 'good', held: 'warning', reversed: 'neutral' };
const STATE_NAME: Record<SampleDocument['state'], string> = { posted: 'Posted', held: 'On hold', reversed: 'Reversed' };

/** Every component of the kit on one page, with made-up data: the reference for building the screens (phase 2). */
@Component({
  selector: 'app-kit',
  imports: [Button, CellTemplate, Chart, Checkbox, Chips, Field, Icon, Input, Kpi, PropertyForm, Select, Status, Table, Tabs, TreeTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './kit.html',
  styleUrl: './kit.scss',
})
export class Kit {
  private readonly dialogs = inject(Dialogs);
  private readonly toasts = inject(Toasts);
  protected readonly formatNumber = formatNumber;

  // Controls
  protected readonly partnerOptions: SelectOption[] = PARTNERS.map((p, i) => ({ value: `p${i}`, label: p, hint: `P-${100 + i}` }));
  protected readonly manyOptions: SelectOption[] = Array.from({ length: 24 }, (_, i) => ({ value: `u${i}`, label: `Product ${i + 1}`, hint: `${(i + 1) * 3} pcs` }));
  protected readonly partner = signal<string | null>(null);
  protected readonly products = signal<string[]>([]);
  protected readonly remember = signal(true);
  protected readonly note = signal('');

  // Chips + table
  protected readonly stateChips = computed<Chip<SampleDocument['state']>[]>(() =>
    (['posted', 'held', 'reversed'] as const).map(s => ({ value: s, label: STATE_NAME[s], count: DOCUMENTS.filter(d => d.state === s).length })));
  protected readonly states = signal<SampleDocument['state'][]>([]);
  protected readonly sort = signal<Sort | null>({ key: 'date', direction: 'desc' });
  protected readonly page = signal(1);
  protected readonly selected = signal<string[]>([]);
  protected readonly activeId = signal<string | null>(null);
  protected readonly documents = computed(() => DOCUMENTS.filter(d => !this.states().length || this.states().includes(d.state)));
  protected readonly total = computed(() => formatNumber(this.documents().reduce((sum, d) => sum + d.amount, 0)));
  protected readonly columns: Column<SampleDocument>[] = [
    { key: 'number', label: 'Number', sortable: true, width: '110px' },
    { key: 'date', label: 'Date', format: 'date', sortable: true, width: '120px' },
    { key: 'partner', label: 'Partner', sortable: true },
    { key: 'state', label: 'State', width: '130px' },
    { key: 'amount', label: 'Amount', format: 'number', sortable: true, width: '140px' },
  ];

  // Tree table
  protected readonly accounts = ACCOUNTS;
  protected readonly expanded = signal<string[]>(['a1', 'a11']);
  protected readonly accountColumns: Column<SampleAccount>[] = [
    { key: 'code', label: 'Account' },
    { key: 'name', label: 'Name' },
    { key: 'debit', label: 'Debit', format: 'number', width: '140px' },
    { key: 'credit', label: 'Credit', format: 'number', width: '140px' },
  ];
  private readonly tree = viewChild.required(TreeTable);

  // Tabs
  protected readonly tabs: Tab[] = [{ id: 'lines', label: 'Lines', count: 3 }, { id: 'posting', label: 'Posting' }, { id: 'history', label: 'History' }];
  protected readonly tab = signal<string | null>('lines');

  // Charts
  private readonly months = ['May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct'];
  protected readonly salesChart: ChartSpec = {
    kind: 'line', categories: this.months, decimals: 0, unit: 'EUR',
    series: [{ name: 'This year', values: [8200, 9100, 12400, 13800, 10100, 9650] }, { name: 'Last year', values: [7600, 8300, 10900, 12100, 9800, 8900] }],
  };
  protected readonly costChart: ChartSpec = {
    kind: 'stacked-bar', categories: this.months, decimals: 0, unit: 'EUR',
    series: [{ name: 'Goods', values: [4100, 4500, 6200, 6900, 5000, 4700] }, { name: 'Staff', values: [2200, 2200, 2600, 2600, 2300, 2200] },
      { name: 'Rent and utilities', values: [900, 950, 1100, 1150, 980, 920] }],
  };
  protected readonly mixChart: ChartSpec = {
    kind: 'donut', categories: ['Drinks', 'Food', 'Coffee', 'Desserts'], decimals: 0, unit: 'EUR',
    series: [{ name: 'October', values: [4300, 3100, 1500, 750] }],
  };
  protected readonly visitsChart: ChartSpec = {
    kind: 'bar', categories: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'], series: [{ name: 'Bills', values: [42, 38, 45, 51, 88, 112, 74] }],
  };

  // Property form
  protected readonly properties = PROPERTIES;
  protected readonly values = signal<PropertyValues>({ p1: { en: 'Harbour Lager' }, p5: 'c1' });
  protected readonly lookups: Record<string, SelectOption[]> = { partners: this.partnerOptions };
  protected readonly saved = signal<string | null>(null);
  private readonly form = viewChild.required(PropertyForm);

  protected kindOf(row: unknown): StatusKind {
    return STATE_KIND[(row as SampleDocument).state];
  }

  protected stateOf(row: unknown): string {
    return STATE_NAME[(row as SampleDocument).state];
  }

  protected expandAll(open: boolean): void {
    this.tree().expandAll(open);
  }

  protected async reverse(): Promise<void> {
    const yes = await this.dialogs.confirm({
      title: 'Reverse invoice S-1004?',
      message: 'A reversal document is created and posted; the invoice stays as it is, marked reversed.',
      confirmLabel: 'Reverse',
      danger: true,
    });
    if (yes) {
      this.toasts.good('Invoice S-1004 reversed.');
    } else {
      this.toasts.info('Nothing was changed.');
    }
  }

  protected toast(kind: 'good' | 'info' | 'warning' | 'critical'): void {
    const messages = { good: 'Invoice S-1012 posted.', info: 'The list was refreshed.', warning: 'Two lines have no VAT group.',
      critical: 'The API did not answer. Nothing was saved.' };
    this.toasts.show(kind, messages[kind]);
  }

  protected save(): void {
    if (this.form().validate()) {
      this.saved.set(toSaveJson(this.form().toSave()));
      this.toasts.good('The values are valid.');
    } else {
      this.saved.set(null);
      this.toasts.warning('Some fields need attention.');
    }
  }
}
