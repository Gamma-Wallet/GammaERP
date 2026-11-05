import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DocumentApi, DocumentForm, HeldLot, Line, LineFill, LineKind, LineSerial } from '../../core/documents';
import { EntityApi } from '../../core/entities';
import { Button } from '../../ui/controls';
import { Dialogs } from '../../ui/dialog';
import { formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { SerialDialog, SerialDialogData } from './serial-dialog';

/** A line on the screen: the API's line, a key to track it, and the names its pickers show. */
export interface LineDraft extends Line {
  key: string;
  units?: SelectOption[];
}

/** The fields of a line a document type's fill can ask for (LineFill: 0 typed, 1 chosen, 2 not needed). */
export type LineField = 'element' | 'unit' | 'quantity' | 'unitPrice' | 'discount' | 'vatGroup';

/** Whether the kind's line uses the field: a kind without a fill uses every field. */
export function uses(fill: LineFill | null | undefined, field: LineField): boolean {
  return !fill || fill[field] !== 2;
}

/** The columns a grid shows: those any of the document type's kinds use. */
export function columnsOf(kinds: readonly LineKind[]): Set<LineField> {
  const fields: LineField[] = ['element', 'unit', 'quantity', 'unitPrice', 'discount', 'vatGroup'];
  return new Set(fields.filter(f => kinds.some(k => uses(k.fill, f))));
}

/** A line's amounts as the server works them out (LineAmounts.Of): value = (price − discount) × quantity, VAT = value × rate / 100. */
export function amountsOf(line: Line, rate: number): { value: number; vat: number; total: number } {
  const value = ((line.price ?? 0) - (line.discount ?? 0)) * (line.quantity ?? 0);
  const vat = (value * rate) / 100;
  return { value, vat, total: value + vat };
}

/** The document's totals over its lines: discount (per unit × quantity), net, VAT and total. */
export function totalsOf(lines: readonly Line[], rateOf: (vatGroupId: string | null | undefined) => number) {
  return lines.reduce(
    (sum, line) => {
      const a = amountsOf(line, rateOf(line.vatGroupId));
      return { discount: sum.discount + (line.discount ?? 0) * (line.quantity ?? 0), value: sum.value + a.value, vat: sum.vat + a.vat, total: sum.total + a.total };
    },
    { discount: 0, value: 0, vat: 0, total: 0 },
  );
}

let nextKey = 0;

/** A new line of a kind: its default quantity and discount, the first VAT group, the first configuration. */
export function newLine(kind: LineKind, form: DocumentForm, currencyId: string | null): LineDraft {
  return {
    key: `line-${++nextKey}`, typeId: kind.transactionTypeId, typeConfId: kind.scopes[0]?.confId ?? null, name: {},
    quantity: kind.fill?.defaultQuantity ?? 1, discount: kind.fill?.defaultDiscount ?? null, price: null,
    vatGroupId: uses(kind.fill, 'vatGroup') ? (form.vatGroups[0]?.id ?? null) : null, currencyId,
  };
}

/** A saved line, as the grid holds it. */
export function draftOf(line: Line): LineDraft {
  return { ...line, key: line.id ?? `line-${++nextKey}`, units: line.unitId ? [{ value: line.unitId, label: line.unitName ?? '' }] : [] };
}

/**
 * The lines of a document, typed like a spreadsheet: a column for each field the type's kinds use, a cell switched off where its kind does not
 * need it. Picking a product asks for its units and proposed price (the person may change it). Enter on the last line adds one.
 */
@Component({
  selector: 'app-line-grid',
  imports: [TranslatePipe, Button, CdkMenu, CdkMenuItem, CdkMenuTrigger, Icon, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wrap">
      <table [attr.aria-label]="'Lines' | t">
        <thead>
          <tr>
            <th class="no">#</th>
            @if (form().lineKinds.length > 1) { <th class="kind">{{ 'Kind' | t }}</th> }
            @if (cols().has('element')) { <th class="element">{{ 'Item / account' | t }}</th> }
            @if (form().paysOpenItems) { <th class="unit">{{ 'Invoice number' | t }}</th> }
            @if (placeSide()) { <th class="place">{{ 'Place' | t }}</th> }
            @if (cols().has('unit')) { <th class="unit">{{ 'Unit' | t }}</th> }
            @if (cols().has('quantity')) { <th class="num">{{ 'Quantity' | t }}</th> }
            @if (cols().has('unitPrice')) { <th class="num">{{ 'Price' | t }}</th> }
            @if (cols().has('discount')) { <th class="num">{{ 'Discount' | t }}</th> }
            @if (cols().has('vatGroup')) { <th class="vat">{{ 'VAT' | t }}</th> }
            <th class="num">{{ 'Net' | t }}</th>
            <th class="num">{{ 'VAT amount' | t }}</th>
            <th class="num">{{ 'Total' | t }}</th>
            @if (!readOnly()) { <th class="act"></th> }
          </tr>
        </thead>
        <tbody>
          @for (line of lines(); track line.key; let i = $index; let last = $last) {
            <tr [class.invalid]="rowError(i)">
              <td class="no figures">{{ i + 1 }}</td>
              @if (form().lineKinds.length > 1) {
                <td><ui-select [options]="kindOptions()" [value]="line.typeId" [disabled]="readOnly()" [ariaLabel]="'Kind' | t" (valueChange)="changeKind(i, $event)" /></td>
              }
              @if (cols().has('element')) {
                <td>
                  @if (uses(kindOf(line)?.fill, 'element')) {
                    <ui-select [options]="elementOptions(line)" [searchable]="true" [value]="line.elementId ?? null" [disabled]="readOnly()"
                      [placeholder]="scopeOf(line)?.name ?? 'Choose…'" [ariaLabel]="'Item' | t" (search)="search(line, $event)"
                      (valueChange)="pickElement(i, $event)" />
                    @if (line.tracksLot) {
                      <span class="lot">
                        <input class="control" [attr.list]="'lots-' + i" [attr.aria-label]="'Lot' | t" [placeholder]="'Lot' | t" [value]="line.lot ?? ''" [disabled]="readOnly()"
                          (focus)="loadLots(i)" (input)="set(i, { lot: $any($event.target).value.trim() || null })" />
                        <datalist [id]="'lots-' + i">
                          @for (l of heldLots()[line.elementId!] ?? []; track l.id) { <option [value]="l.lot">{{ l.quantity }}{{ l.expiryDate ? ' · ' + l.expiryDate : '' }}</option> }
                        </datalist>
                        <input class="control" type="date" [attr.aria-label]="'Expiry date' | t" [value]="line.lotExpiryDate ?? ''" [disabled]="readOnly()"
                          (input)="set(i, { lotExpiryDate: $any($event.target).value || null })" />
                      </span>
                    }
                    @if (line.tracksSerial) {
                      <button type="button" class="serials" [class.short]="(line.serials?.length ?? 0) !== line.quantity" (click)="openSerials(i)">{{ 'Serial numbers: {0} of {1}' | t: (line.serials?.length ?? 0) : (line.quantity ?? 0) }}</button>
                    }
                  }
                </td>
              }
              @if (form().paysOpenItems) {
                <td><input class="control" [attr.aria-label]="'Invoice number' | t" [value]="line.referenceNumber ?? ''" [disabled]="readOnly()"
                  (input)="set(i, { referenceNumber: $any($event.target).value.trim() || null })" (keydown.enter)="enter(last)" /></td>
              }
              @if (placeSide(); as side) {
                <td><ui-select [options]="placeOptions(line)" [searchable]="true" [value]="side === 'from' ? (line.fromId ?? null) : (line.toId ?? null)" [disabled]="readOnly()"
                  [placeholder]="documentPlace" [ariaLabel]="'Place' | t" (search)="searchPlaces($event)" (valueChange)="pickPlace(i, side, $event)" /></td>
              }
              @if (cols().has('unit')) {
                <td>
                  @if (uses(kindOf(line)?.fill, 'unit')) {
                    <ui-select [options]="line.units ?? []" [value]="line.unitId ?? null" [disabled]="readOnly()" [ariaLabel]="'Unit' | t" (valueChange)="pickUnit(i, $event)" />
                  }
                </td>
              }
              @if (cols().has('quantity')) {
                <td>@if (uses(kindOf(line)?.fill, 'quantity')) {
                  <input class="control number" inputmode="decimal" [attr.aria-label]="'Quantity' | t" [value]="line.quantity ?? ''" [disabled]="readOnly()"
                    [attr.aria-invalid]="cellError(i, 'quantity') ? 'true' : null" (input)="setNumber(i, 'quantity', $any($event.target).value)" (keydown.enter)="enter(last)" />
                }</td>
              }
              @if (cols().has('unitPrice')) {
                <td>@if (uses(kindOf(line)?.fill, 'unitPrice')) {
                  <input class="control number" inputmode="decimal" [attr.aria-label]="'Price' | t" [value]="line.price ?? ''" [disabled]="readOnly()"
                    [attr.aria-invalid]="cellError(i, 'price') ? 'true' : null" (input)="setNumber(i, 'price', $any($event.target).value)" (keydown.enter)="enter(last)" />
                }</td>
              }
              @if (cols().has('discount')) {
                <td>@if (uses(kindOf(line)?.fill, 'discount')) {
                  <input class="control number" inputmode="decimal" [attr.aria-label]="'Discount per unit' | t" [value]="line.discount ?? ''" [disabled]="readOnly()"
                    (input)="setNumber(i, 'discount', $any($event.target).value)" (keydown.enter)="enter(last)" />
                }</td>
              }
              @if (cols().has('vatGroup')) {
                <td>@if (uses(kindOf(line)?.fill, 'vatGroup')) {
                  <ui-select [options]="vatOptions()" [value]="line.vatGroupId ?? null" [disabled]="readOnly()" [ariaLabel]="'VAT group' | t" (valueChange)="set(i, { vatGroupId: $event })" />
                }</td>
              }
              <td class="num figures">{{ format(amounts(line).value) }}</td>
              <td class="num figures">{{ format(amounts(line).vat) }}</td>
              <td class="num figures strong">{{ format(amounts(line).total) }}</td>
              @if (!readOnly()) {
                <td class="act">
                  <button uiButton variant="quiet" size="small" [iconOnly]="true" [attr.aria-label]="'Remove line' | t" (click)="remove(i)"><ui-icon name="trash" [size]="14" /></button>
                </td>
              }
            </tr>
            @if (rowError(i); as error) {
              <tr class="error-row"><td [attr.colspan]="span()">{{ error }}</td></tr>
            }
          } @empty {
            <tr><td class="empty" [attr.colspan]="span()">{{ 'No lines yet.' | t }}</td></tr>
          }
        </tbody>
      </table>
    </div>
    @if (!readOnly() && form().lineKinds.length) {
      <div class="add">
        <!-- One kind of line: it is added straight away; several: chosen from a dropdown. -->
        @if (form().lineKinds.length === 1) {
          <button uiButton size="small" (click)="add(form().lineKinds[0])"><ui-icon name="plus" [size]="14" />{{ 'Add line' | t }}</button>
        } @else {
          <button uiButton size="small" [cdkMenuTriggerFor]="kinds"><ui-icon name="plus" [size]="14" />{{ 'Add line' | t }}<ui-icon name="chevron-down" [size]="14" /></button>
          <ng-template #kinds>
            <div class="menu-panel" cdkMenu>
              @for (kind of form().lineKinds; track kind.transactionTypeId) {
                <button class="menu-item" cdkMenuItem (cdkMenuItemTriggered)="add(kind)">{{ kind.name }}</button>
              }
            </div>
          </ng-template>
        }
      </div>
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-3); }
    .wrap { overflow-x: auto; border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
    table { width: 100%; border-collapse: collapse; font-size: var(--text-s); }
    th { height: var(--row-head-h); padding: 0 var(--space-2); background: var(--surface-2); color: var(--ink-2); font-size: var(--text-xs);
      font-weight: 500; letter-spacing: 0.01em; text-align: left; white-space: nowrap; border-bottom: 1px solid var(--line); }
    td { padding: 4px var(--space-2); border-bottom: 1px solid var(--line); vertical-align: middle; }
    tr:last-child td { border-bottom: 0; }
    .num { text-align: right; white-space: nowrap; }
    th.num { width: 110px; }
    .no { width: 36px; color: var(--muted); }
    .kind { width: 160px; } .element { min-width: 240px; } .place { min-width: 180px; } .unit { width: 120px; } .vat { width: 140px; } .act { width: 40px; }
    .control { height: 32px; }
    .control.number { min-width: 9ch; }
    .strong { font-weight: 600; }
    tr.invalid td { background: var(--critical-bg); }
    .error-row td { padding: 2px var(--space-2) var(--space-2) 44px; color: var(--critical); font-size: var(--text-xs); background: var(--critical-bg); }
    .empty { height: 64px; color: var(--muted); text-align: center; }
    .add { display: flex; flex-wrap: wrap; gap: var(--space-2); }
    .lot { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-1); padding-top: 2px; }
    .serials { padding: 2px 0 0; border: 0; background: none; color: var(--muted); font-size: var(--text-xs); text-decoration: underline; cursor: pointer; }
    .serials.short { color: var(--critical); }
  `,
})
export class LineGrid {
  private readonly api = inject(DocumentApi);
  private readonly entities = inject(EntityApi);
  private readonly dialogs = inject(Dialogs);
  protected readonly uses = uses;

  readonly form = input.required<DocumentForm>();
  readonly lines = input.required<LineDraft[]>();
  readonly readOnly = input(false);
  /** The API's refusals by field ("transactions[2].quantity" → message). */
  readonly errors = input<Record<string, string | undefined>>({});
  /** The document's partner (on whichever side), for the proposed price. */
  readonly partnerId = input<string | null>(null);
  readonly currencyId = input<string | null>(null);
  /** The document's day: a proposed price is worked out at that day's exchange rates. */
  readonly date = input<string | null>(null);
  /** The document's from side: what a line's serial numbers are picked from. */
  readonly fromId = input<string | null>(null);
  readonly linesChange = output<LineDraft[]>();

  /** The choices found per lookup scope, and the names of the elements already on lines. */
  private readonly found = signal<Record<string, SelectOption[]>>({});
  protected readonly cols = computed(() => columnsOf(this.form().lineKinds));
  protected readonly span = computed(() => 5 + this.cols().size + (this.form().lineKinds.length > 1 ? 1 : 0) + (this.placeSide() ? 1 : 0));
  /**
   * The side of a line that is a place (a cost centre): the from side when the document takes stock out of one, else the to side. A line's own
   * place overrides the document's; empty is the document's.
   */
  protected readonly placeSide = computed<'from' | 'to' | null>(() =>
    this.form().from?.table === 'tbl_cost_center' ? 'from' : this.form().to?.table === 'tbl_cost_center' ? 'to' : null);
  private readonly places = signal<SelectOption[]>([]);
  protected readonly documentPlace = t("The document's place");
  protected readonly kindOptions = computed<SelectOption[]>(() => this.form().lineKinds.map(k => ({ value: k.transactionTypeId, label: k.name })));
  protected readonly vatOptions = computed<SelectOption[]>(() => this.form().vatGroups.map(v => ({ value: v.id, label: v.name || `${v.rate} %`, hint: `${v.rate} %` })));
  private readonly rates = computed(() => new Map(this.form().vatGroups.map(v => [v.id, v.rate])));

  protected kindOf(line: Line): LineKind | undefined {
    return this.form().lineKinds.find(k => k.transactionTypeId === line.typeId);
  }

  protected scopeOf(line: Line) {
    const kind = this.kindOf(line);
    return kind?.scopes.find(s => s.confId === line.typeConfId) ?? kind?.scopes[0];
  }

  protected rateOf(vatGroupId: string | null | undefined): number {
    return (vatGroupId && this.rates().get(vatGroupId)) || 0;
  }

  protected amounts(line: Line) {
    return amountsOf(line, this.rateOf(line.vatGroupId));
  }

  protected format(value: number): string {
    return formatNumber(value);
  }

  protected elementOptions(line: LineDraft): SelectOption[] {
    const scope = this.scopeOf(line)?.scopeId;
    const options = (scope && this.found()[scope]) || [];
    // The line's own element stays shown even when the search has not found it.
    return line.elementId && !options.some(o => o.value === line.elementId)
      ? [{ value: line.elementId, label: line.elementName ?? '' }, ...options] : options;
  }

  protected cellError(i: number, field: string): string | undefined {
    return this.errors()[`transactions[${i}].${field}`];
  }

  protected rowError(i: number): string | null {
    const prefix = `transactions[${i}]`;
    const found = Object.entries(this.errors()).filter(([k, v]) => k.startsWith(prefix) && v).map(([, v]) => v);
    return found.length ? found.join(' ') : null;
  }

  private emit(lines: LineDraft[]): void {
    this.linesChange.emit(lines);
  }

  protected set(i: number, change: Partial<LineDraft>): void {
    this.emit(this.lines().map((l, n) => (n === i ? { ...l, ...change } : l)));
  }

  protected setNumber(i: number, field: 'quantity' | 'price' | 'discount', typed: string): void {
    const value = typed.trim() === '' ? null : Number(typed.replace(',', '.'));
    this.set(i, { [field]: value === null || Number.isNaN(value) ? null : value });
  }

  protected add(kind: LineKind): void {
    this.emit([...this.lines(), newLine(kind, this.form(), this.currencyId())]);
  }

  protected enter(last: boolean): void {
    const kind = this.form().lineKinds[0];
    if (last && kind && !this.readOnly()) {
      this.add(kind);
    }
  }

  protected remove(i: number): void {
    this.emit(this.lines().filter((_, n) => n !== i));
  }

  protected changeKind(i: number, typeId: string | null): void {
    const kind = this.form().lineKinds.find(k => k.transactionTypeId === typeId);
    if (kind) {
      this.set(i, { typeId: kind.transactionTypeId, typeConfId: kind.scopes[0]?.confId ?? null, elementId: null, elementName: null, units: [], unitId: null });
    }
  }

  /** Looks up the line's choices as the person types (the first page when nothing is typed). */
  search(line: Line, query: string): Promise<void> {
    const scope = this.scopeOf(line)?.scopeId;
    return scope ? this.searchScope(scope, query) : Promise.resolve();
  }

  private async searchScope(scope: string, query: string): Promise<void> {
    try {
      const page = await this.entities.lookup(scope, query, 50);
      this.found.update(all => ({ ...all, [scope]: page.items.map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined })) }));
    } catch {
      this.found.update(all => ({ ...all, [scope]: [] }));
    }
  }

  /** Loads the first choices of every kind's scope, so a picker opens with them, and the places a line may name. */
  preload(): Promise<void> {
    const scopes = new Set(this.form().lineKinds.flatMap(k => k.scopes.map(s => s.scopeId).filter((s): s is string => !!s)));
    return Promise.all([...[...scopes].map(scope => this.searchScope(scope, '')), this.searchPlaces('')]).then(() => undefined);
  }

  protected async searchPlaces(query: string): Promise<void> {
    const side = this.placeSide();
    const scope = side === 'from' ? this.form().from?.scopeId : side === 'to' ? this.form().to?.scopeId : null;
    if (!scope) {
      return;
    }
    const page = await this.entities.lookup(scope, query, 50).catch(() => null);
    this.places.set(page?.items.map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined })) ?? []);
  }

  protected placeOptions(line: Line): SelectOption[] {
    const side = this.placeSide();
    const [id, name] = side === 'from' ? [line.fromId, line.fromName] : [line.toId, line.toName];
    const options = this.places();
    return id && !options.some(o => o.value === id) ? [{ value: id, label: name ?? '' }, ...options] : options;
  }

  protected pickPlace(i: number, side: 'from' | 'to', id: string | null): void {
    const name = id ? (this.places().find(o => o.value === id)?.label ?? null) : null;
    this.set(i, side === 'from' ? { fromId: id, fromName: name } : { toId: id, toName: name });
    if (side === 'from') {
      this.heldLots.set({});
    }
  }

  protected async pickElement(i: number, elementId: string | null): Promise<void> {
    const line = this.lines()[i];
    const name = this.elementOptions(line).find(o => o.value === elementId)?.label ?? null;
    this.set(i, { elementId, elementName: name, serials: [], tracksSerial: false, lot: null, lotExpiryDate: null, tracksLot: false });
    if (elementId && this.scopeOf(line)?.table === 'tbl_product') {
      await this.propose(i, elementId, null);
    }
  }

  protected async pickUnit(i: number, unitId: string | null): Promise<void> {
    const line = this.lines()[i];
    this.set(i, { unitId, unitName: line.units?.find(u => u.value === unitId)?.label ?? null });
    if (line.elementId && unitId) {
      await this.propose(i, line.elementId, unitId);
    }
  }

  /** A line's serial numbers: typed when they come new from a partner, picked from what the from side holds otherwise. */
  /** The lots the from side holds, shown when the lot box is opened, earliest expiry first; a purchase types a new one. */
  protected readonly heldLots = signal<Record<string, HeldLot[]>>({});

  protected async loadLots(i: number): Promise<void> {
    const line = this.lines()[i];
    const holder = line?.fromId ?? this.fromId();
    if (!line?.elementId || !holder || this.form().from?.table === 'tbl_partner' || this.heldLots()[line.elementId]) {
      return;
    }
    const product = line.elementId;
    const lots = await this.api.heldLots(product, holder, line.unitId ?? null).catch(() => []);
    this.heldLots.update(held => ({ ...held, [product]: lots }));
  }

  protected async openSerials(i: number): Promise<void> {
    const line = this.lines()[i];
    if (!line?.elementId) {
      return;
    }
    const table = this.form().from?.table;
    const data: SerialDialogData = { product: line.elementName ?? '', productId: line.elementId, quantity: line.quantity ?? 0, serials: line.serials ?? [],
      mode: !table || table === 'tbl_partner' ? 'type' : 'pick', holderId: line.fromId ?? this.fromId(), readOnly: this.readOnly() };
    const serials = await firstValueFrom(this.dialogs.open<LineSerial[], SerialDialogData, SerialDialog>(SerialDialog, data).closed);
    if (serials) {
      this.set(i, { serials });
    }
  }

  /** The product's units and proposed price (B5): the price is the proposal's, which the person may change. */
  private async propose(i: number, productId: string, unitId: string | null): Promise<void> {
    try {
      const proposal = await this.api.lineProposal(this.form().typeId, productId, this.partnerId(), unitId, this.currencyId(), this.date());
      const current = this.lines()[i];
      if (!current || current.elementId !== productId) {
        return;
      }
      const units = proposal.units.map(u => ({ value: u.id, label: u.name, hint: u.code ?? undefined }));
      this.set(i, {
        units, unitId: proposal.unitId, unitName: units.find(u => u.value === proposal.unitId)?.label ?? null,
        ...(proposal.price !== null ? { price: proposal.price } : {}),
        ...(proposal.vatGroupId ? { vatGroupId: proposal.vatGroupId } : {}),
        tracksSerial: proposal.tracksSerial ?? false,
        tracksLot: proposal.tracksLot ?? false,
      });
    } catch {
      // No proposal: the person types the unit and price.
    }
  }
}
