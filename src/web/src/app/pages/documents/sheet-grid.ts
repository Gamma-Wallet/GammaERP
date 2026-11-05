import { TranslatePipe } from '../../core/t.pipe';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DocumentApi, DocumentData, DocumentForm } from '../../core/documents';
import { ElementType, EntityApi, errorsOf } from '../../core/entities';
import { Button, Input } from '../../ui/controls';
import { formatNumber, textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { blankRow, journalOf, locationSide, rowOf, SheetRow, sheetTotals, withAmount } from './sheet-logic';

/**
 * The one grid of a sheet: each row is a journal under the sheet with its one transaction: which journal type, which
 * kind of line, what, where, debit or credit. What each column offers comes from the journal type's form.
 */
@Component({
  selector: 'app-sheet-grid',
  imports: [TranslatePipe, Button, Icon, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <div class="wrap">
      <table>
        <thead>
          <tr><th class="n">#</th><th class="type">{{ 'Document type' | t }}</th><th class="kind">{{ 'Kind' | t }}</th><th>{{ 'What' | t }}</th><th class="where">{{ 'Where' | t }}</th><th class="money num">{{ 'Debit' | t }}</th><th class="money num">{{ 'Credit' | t }}</th><th class="text">{{ 'Description' | t }}</th>
            @if (!readOnly()) { <th class="n"></th> }</tr>
        </thead>
        <tbody>
          @for (r of rows(); track $index; let i = $index) {
            <tr>
              <td class="n figures">{{ i + 1 }}</td>
              <td><ui-select [options]="typeOptions()" [value]="r.typeId" [disabled]="readOnly()" [ariaLabel]="'Document type of row ' + (i + 1)" (valueChange)="chooseType(i, $event)" /></td>
              <td><ui-select [options]="kindOptions(r)" [value]="kindOf(r)" [disabled]="readOnly()" [ariaLabel]="'Kind of row ' + (i + 1)" (valueChange)="chooseKind(i, $event)" /></td>
              <td><ui-select [options]="elementOptions(r)" [searchable]="true" [value]="r.elementId" [disabled]="readOnly() || !r.confId" [ariaLabel]="'What of row ' + (i + 1)"
                (search)="findElement(r, $event)" (valueChange)="chooseElement(i, $event)" /></td>
              <td><ui-select [options]="locationOptions(r)" [searchable]="true" [value]="r.locationId" [disabled]="readOnly() || !hasLocation(r)" [ariaLabel]="'Where of row ' + (i + 1)"
                (search)="findLocation(r, $event)" (valueChange)="chooseLocation(i, $event)" /></td>
              <td><input uiInput class="num" inputmode="decimal" [attr.aria-label]="'Debit of row ' + (i + 1)" [disabled]="readOnly()" [value]="r.debit ?? ''" (change)="amount(i, 'debit', $any($event.target).value)" /></td>
              <td><input uiInput class="num" inputmode="decimal" [attr.aria-label]="'Credit of row ' + (i + 1)" [disabled]="readOnly()" [value]="r.credit ?? ''" (change)="amount(i, 'credit', $any($event.target).value)" /></td>
              <td><input uiInput [attr.aria-label]="'Description of row ' + (i + 1)" [disabled]="readOnly()" [value]="r.description" (change)="set(i, { description: $any($event.target).value })" /></td>
              @if (!readOnly()) {
                <td class="n"><button uiButton variant="quiet" size="small" [iconOnly]="true" [attr.aria-label]="'Remove row ' + (i + 1)" (click)="remove(i)"><ui-icon name="trash" [size]="14" /></button></td>
              }
            </tr>
          } @empty {
            <tr><td class="empty" [attr.colspan]="readOnly() ? 8 : 9">{{ 'No rows yet.' | t }}</td></tr>
          }
        </tbody>
      </table>
    </div>
    <div class="foot">
      @if (!readOnly()) { <button uiButton size="small" [disabled]="!types().length" (click)="add()"><ui-icon name="plus" [size]="14" />{{ 'Add row' | t }}</button> }
      <span class="spacer"></span>
      <span>{{ 'Debit' | t }} <strong class="figures">{{ n(totals().debit) }}</strong></span>
      <span>{{ 'Credit' | t }} <strong class="figures">{{ n(totals().credit) }}</strong></span>
      <span [class.off]="totals().difference !== 0">{{ 'Difference' | t }} <strong class="figures">{{ n(totals().difference) }}</strong></span>
    </div>
  `,
  styles: `
    :host { display: grid; gap: var(--space-3); }
    .message { display: flex; align-items: center; gap: var(--space-2); margin: 0; padding: var(--space-2) var(--space-3); border-radius: var(--radius); background: var(--critical-bg); color: var(--critical); }
    .wrap { overflow-x: auto; border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); }
    table { width: 100%; min-width: 1240px; table-layout: fixed; border-collapse: collapse; }
    th.type { width: 180px; } th.kind { width: 150px; } th.where { width: 180px; } th.money { width: 130px; } th.text { width: 200px; }
    th { padding: var(--space-2); text-align: left; font-weight: 500; color: var(--muted); background: var(--surface-2); white-space: nowrap; }
    td { padding: var(--space-1); border-top: 1px solid var(--line); }
    .n { width: 36px; text-align: center; color: var(--muted); }
    .num { text-align: right; }
    td input { width: 100%; }
    .empty { padding: var(--space-5); text-align: center; color: var(--muted); }
    .foot { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-4); padding: var(--space-3); border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); }
    .spacer { flex: 1; }
    .off { color: var(--critical); }
  `,
})
export class SheetGrid {
  private readonly api = inject(DocumentApi);
  private readonly entities = inject(EntityApi);

  /** The journal types under the sheet's type. */
  readonly types = input<ElementType[]>([]);
  readonly journals = input<DocumentData[]>([]);
  readonly categoryId = input.required<string>();
  readonly readOnly = input(false);
  readonly changed = output<number>();

  protected readonly n = formatNumber;
  protected readonly message = signal<string | null>(null);
  protected readonly rows = signal<SheetRow[]>([]);
  protected readonly totals = computed(() => sheetTotals(this.rows()));
  private readonly forms = signal<Record<string, DocumentForm>>({});
  private readonly found = signal<Record<string, SelectOption[]>>({});
  protected readonly typeOptions = computed<SelectOption[]>(() => this.types().map(t => ({ value: t.id!, label: textIn(t.name) || t.code || '', hint: t.code ?? undefined })));

  constructor() {
    effect(() => {
      const journals = this.journals();
      untracked(() => {
        this.rows.set(journals.map(rowOf));
        void Promise.all(journals.map(j => this.formOf(j.typeId)));
      });
    });
  }

  /** The journals to save, one per row. */
  toSave(): DocumentData[] {
    return this.rows().map(r => journalOf(r, this.forms()[r.typeId], this.categoryId()));
  }

  private async formOf(typeId: string): Promise<DocumentForm | null> {
    const known = this.forms()[typeId];
    if (known) {
      return known;
    }
    try {
      const form = await this.api.form(typeId);
      this.forms.update(all => ({ ...all, [typeId]: form }));
      const side = locationSide(form);
      if (side) {
        await this.search(side.scope.scopeId, '');
      }
      return form;
    } catch (error) {
      this.message.set(errorsOf(error).message);
      return null;
    }
  }

  private async search(scope: string, query: string): Promise<void> {
    try {
      const page = await this.entities.lookup(scope, query, 50);
      this.found.update(all => ({ ...all, [scope]: page.items.map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined })) }));
    } catch {
      this.found.update(all => ({ ...all, [scope]: [] }));
    }
  }

  /** What is offered, with what the row already holds so a saved row shows its name. */
  private offered(scope: string | null | undefined, id: string | null, name: string | null): SelectOption[] {
    const options = scope ? this.found()[scope] ?? [] : [];
    return id && !options.some(o => o.value === id) ? [{ value: id, label: name ?? id }, ...options] : options;
  }

  private scopeOf(row: SheetRow) {
    return this.forms()[row.typeId]?.lineKinds.flatMap(k => k.scopes).find(s => s.confId === row.confId);
  }

  protected kindOf(row: SheetRow): string | null {
    return row.confId;
  }

  protected kindOptions(row: SheetRow): SelectOption[] {
    return (this.forms()[row.typeId]?.lineKinds ?? []).flatMap(k => k.scopes.map(s => ({ value: s.confId, label: k.scopes.length > 1 ? `${k.name} · ${s.name}` : k.name })));
  }

  protected elementOptions(row: SheetRow): SelectOption[] {
    return this.offered(this.scopeOf(row)?.scopeId, row.elementId, row.elementName);
  }

  protected locationOptions(row: SheetRow): SelectOption[] {
    return this.offered(locationSide(this.forms()[row.typeId])?.scope.scopeId, row.locationId, row.locationName);
  }

  protected hasLocation(row: SheetRow): boolean {
    return !!locationSide(this.forms()[row.typeId]);
  }

  protected findElement(row: SheetRow, query: string): void {
    const scope = this.scopeOf(row)?.scopeId;
    if (scope) {
      void this.search(scope, query);
    }
  }

  protected findLocation(row: SheetRow, query: string): void {
    const side = locationSide(this.forms()[row.typeId]);
    if (side) {
      void this.search(side.scope.scopeId, query);
    }
  }

  /** Where only one place can be chosen (a journal's company), it is that one. */
  private withOnlyPlace(row: SheetRow): SheetRow {
    const options = this.locationOptions(row);
    return !row.locationId && options.length === 1 ? { ...row, locationId: String(options[0].value), locationName: options[0].label } : row;
  }

  private update(rows: SheetRow[]): void {
    this.rows.set(rows);
    this.changed.emit(rows.length);
  }

  protected set(i: number, change: Partial<SheetRow>): void {
    this.update(this.rows().map((r, n) => (n === i ? { ...r, ...change } : r)));
  }

  protected amount(i: number, field: 'debit' | 'credit', typed: string): void {
    this.update(this.rows().map((r, n) => (n === i ? withAmount(r, field, typed) : r)));
  }

  protected async add(): Promise<void> {
    const last = this.rows().at(-1);
    const typeId = last?.typeId ?? this.types()[0]?.id;
    if (typeId) {
      await this.formOf(typeId);
      this.update([...this.rows(), this.withOnlyPlace(blankRow(typeId, last))]);
    }
  }

  protected remove(i: number): void {
    this.update(this.rows().filter((_, n) => n !== i));
  }

  protected async chooseType(i: number, typeId: string | null): Promise<void> {
    if (typeId) {
      await this.formOf(typeId);
      this.update(this.rows().map((r, n) => (n === i ? this.withOnlyPlace({ ...r, typeId, lineTypeId: null, confId: null, elementId: null, elementName: null }) : r)));
    }
  }

  protected chooseKind(i: number, confId: string | null): void {
    const row = this.rows()[i];
    const kind = this.forms()[row.typeId]?.lineKinds.find(k => k.scopes.some(s => s.confId === confId));
    this.set(i, { confId, lineTypeId: kind?.transactionTypeId ?? null, elementId: null, elementName: null });
    const scope = kind?.scopes.find(s => s.confId === confId)?.scopeId;
    if (scope && !this.found()[scope]) {
      void this.search(scope, '');
    }
  }

  protected chooseElement(i: number, id: string | null): void {
    const row = this.rows()[i];
    this.set(i, { elementId: id, elementName: this.elementOptions(row).find(o => o.value === id)?.label ?? null });
  }

  protected chooseLocation(i: number, id: string | null): void {
    const row = this.rows()[i];
    this.set(i, { locationId: id, locationName: this.locationOptions(row).find(o => o.value === id)?.label ?? null });
  }
}
