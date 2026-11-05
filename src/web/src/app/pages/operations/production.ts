import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DocumentApi, DocumentData } from '../../core/documents';
import { ElementType, EntityApi, errorsOf } from '../../core/entities';
import { DisassemblyLine, OperationsApi, OrderToProduce, ProductionLine } from '../../core/operations';
import { Button, Field, Input } from '../../ui/controls';
import { formatDate, formatNumber, textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Tab, Tabs } from '../../ui/tabs';
import { Toasts } from '../../ui/toast';
import { registerOptions, today } from './register-options';

/** Whether a line's place lacks what it needs (held is known and below the quantity, which already counts the waste). */
export function short(line: ProductionLine): boolean {
  return line.held !== null && line.held < line.quantity;
}

/**
 * Production: a product made from its recipe at a place (the ingredients worked out, then adjusted before producing), and the
 * customers' orders of a type still to produce, each made in one step; and a product taken apart again by its recipe read backwards (what is not
 * recovered is waste). Each opens the document made.
 */
@Component({
  selector: 'app-production',
  imports: [TranslatePipe, Button, Field, Icon, Input, Select, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Orders to produce' | t }}</h1></div>
    <ui-tabs [tabs]="tabs" [(active)]="tab" [ariaLabel]="'Production' | t" />
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @switch (tab()) {
      @case ('produce') {
        <section class="form">
          <ui-field [label]="'Product' | t" [required]="true"><ui-select [options]="products()" [searchable]="true" [value]="product()" (valueChange)="product.set($event)" /></ui-field>
          <ui-field [label]="'Unit' | t" [required]="true"><ui-select [options]="units()" [searchable]="true" [value]="unit()" (valueChange)="unit.set($event)" /></ui-field>
          <ui-field [label]="'Quantity' | t" [required]="true"><input uiInput [numeric]="true" inputmode="decimal" [value]="quantity()" (input)="quantity.set($any($event.target).value)" /></ui-field>
          <ui-field [label]="'Made at' | t" [required]="true"><ui-select [options]="places()" [searchable]="true" [value]="place()" (valueChange)="place.set($event)" /></ui-field>
          <ui-field [label]="'Date' | t"><input uiInput type="date" [value]="date()" (input)="date.set($any($event.target).value)" /></ui-field>
          @if (produceTypes().length > 1) {
            <ui-field [label]="'Document type' | t" [required]="true"><ui-select [options]="produceTypes()" [value]="produceType()" (valueChange)="produceType.set($event)" /></ui-field>
          }
          <div class="actions"><button uiButton [disabled]="!canCalculate()" (click)="calculate()">{{ 'Work out ingredients' | t }}</button></div>
        </section>
        @if (lines(); as list) {
          <table [attr.aria-label]="'Ingredients' | t">
            <thead><tr><th>{{ 'Ingredient' | t }}</th><th class="num">{{ 'Quantity' | t }}</th><th class="num">{{ 'Waste' | t }}</th><th class="num">{{ 'Held here' | t }}</th><th></th></tr></thead>
            <tbody>
              @for (line of list; track line.productId; let i = $index) {
                <tr [class.short]="short(line)">
                  <td>{{ line.name }} @if (short(line)) { <span class="flag">{{ 'short' | t }}</span> }</td>
                  <td class="num"><input class="control number" inputmode="decimal" [attr.aria-label]="'Quantity of ' + line.name" [value]="line.quantity" (input)="setLine(i, 'quantity', $any($event.target).value)" /></td>
                  <td class="num"><input class="control number" inputmode="decimal" [attr.aria-label]="'Waste of ' + line.name" [value]="line.wasteQuantity" (input)="setLine(i, 'wasteQuantity', $any($event.target).value)" /></td>
                  <td class="num figures">{{ line.held === null ? '—' : format(line.held) }}</td>
                  <td><button uiButton variant="quiet" size="small" [iconOnly]="true" [attr.aria-label]="'Remove ingredient' | t" (click)="removeLine(i)"><ui-icon name="trash" [size]="14" /></button></td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="muted">{{ 'The product has no recipe: add the ingredients used below.' | t }}</td></tr>
              }
            </tbody>
          </table>
          <section class="form">
            <ui-field [label]="'Ingredient' | t"><ui-select [options]="products()" [searchable]="true" [value]="newProduct()" (valueChange)="newProduct.set($event)" /></ui-field>
            <ui-field [label]="'Unit' | t"><ui-select [options]="units()" [searchable]="true" [value]="newUnit()" (valueChange)="newUnit.set($event)" /></ui-field>
            <ui-field [label]="'Quantity' | t"><input uiInput [numeric]="true" inputmode="decimal" [value]="newQuantity()" (input)="newQuantity.set($any($event.target).value)" /></ui-field>
            <div class="actions"><button uiButton [disabled]="!newProduct() || !newUnit() || !(+newQuantity() > 0)" (click)="addLine()"><ui-icon name="plus" [size]="14" />{{ 'Add ingredient' | t }}</button></div>
          </section>
          <div><button uiButton variant="primary" [disabled]="busy() || list.length === 0" (click)="produce()">{{ 'Produce' | t }}</button></div>
        }
      }
      @case ('disassemble') {
        <section class="form">
          <ui-field [label]="'Product' | t" [required]="true"><ui-select [options]="products()" [searchable]="true" [value]="product()" (valueChange)="product.set($event)" /></ui-field>
          <ui-field [label]="'Unit' | t" [required]="true"><ui-select [options]="units()" [searchable]="true" [value]="unit()" (valueChange)="unit.set($event)" /></ui-field>
          <ui-field [label]="'Quantity' | t" [required]="true"><input uiInput [numeric]="true" inputmode="decimal" [value]="quantity()" (input)="quantity.set($any($event.target).value)" /></ui-field>
          <ui-field [label]="'Taken apart at' | t" [required]="true"><ui-select [options]="places()" [searchable]="true" [value]="place()" (valueChange)="place.set($event)" /></ui-field>
          <ui-field [label]="'Date' | t"><input uiInput type="date" [value]="date()" (input)="date.set($any($event.target).value)" /></ui-field>
          @if (disassemblyTypes().length > 1) {
            <ui-field [label]="'Document type' | t" [required]="true"><ui-select [options]="disassemblyTypes()" [value]="disassemblyType()" (valueChange)="disassemblyType.set($event)" /></ui-field>
          }
          <div class="actions"><button uiButton [disabled]="!canCalculate()" (click)="calculateParts()">{{ 'Work out what comes back' | t }}</button></div>
        </section>
        @if (productHeld() !== null) { <p class="muted">{{ 'Held here of the product: {0}' | t: format(productHeld()!) }}</p> }
        @if (parts(); as list) {
          <table [attr.aria-label]="'What comes back' | t">
            <thead><tr><th>{{ 'Ingredient' | t }}</th><th class="num">{{ 'Comes back' | t }}</th><th class="num">{{ 'Lost (waste)' | t }}</th><th class="num">{{ 'Held here' | t }}</th></tr></thead>
            <tbody>
              @for (line of list; track line.productId; let i = $index) {
                <tr>
                  <td>{{ line.name }}</td>
                  <td class="num"><input class="control number" inputmode="decimal" [attr.aria-label]="'Comes back of ' + line.name" [value]="line.recovered" (input)="setPart(i, 'recovered', $any($event.target).value)" /></td>
                  <td class="num"><input class="control number" inputmode="decimal" [attr.aria-label]="'Lost of ' + line.name" [value]="line.lost" (input)="setPart(i, 'lost', $any($event.target).value)" /></td>
                  <td class="num figures">{{ line.held === null ? '—' : format(line.held) }}</td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="muted">{{ 'The product has no recipe: nothing to take apart.' | t }}</td></tr>
              }
            </tbody>
          </table>
          <p class="muted">{{ 'The product leaves at what it cost; that cost is shared by quantity and average cost: what comes back goes into stock, what is lost to the waste expense. A disassembly is not reversed.' | t }}</p>
          <div><button uiButton variant="primary" [disabled]="busy() || list.length === 0" (click)="disassemble()">{{ 'Disassemble' | t }}</button></div>
        }
      }
      @case ('orders') {
        <section class="form">
          <ui-field [label]="'Order type' | t" [required]="true"><ui-select [options]="orderTypes()" [searchable]="true" [value]="orderType()" (valueChange)="loadOrders($event)" /></ui-field>
          <ui-field [label]="'Made at' | t" [required]="true"><ui-select [options]="places()" [searchable]="true" [value]="place()" (valueChange)="place.set($event)" /></ui-field>
          <ui-field [label]="'Date' | t"><input uiInput type="date" [value]="date()" (input)="date.set($any($event.target).value)" /></ui-field>
        </section>
        @for (order of orders(); track order.id) {
          <article class="order">
            <header>
              <strong>{{ order.code }}</strong><span class="muted">{{ day(order.date) }} · {{ order.client || '—' }}</span>
              <button uiButton size="small" variant="primary" [disabled]="!place() || busy()" (click)="produceOrder(order)">{{ 'Produce' | t }}</button>
            </header>
            <ul>@for (p of order.products; track p.productId) { <li>{{ format(p.quantity) }} × {{ p.name }}</li> }</ul>
          </article>
        } @empty {
          @if (orderType()) { <p class="muted">{{ 'No orders of this type are waiting to be produced.' | t }}</p> }
        }
      }
    }
  `,
  styleUrl: './operations.scss',
})
export class Production {
  private readonly api = inject(OperationsApi);
  private readonly entities = inject(EntityApi);
  private readonly documents = inject(DocumentApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  protected readonly short = short;

  protected readonly tabs: Tab[] = [{ id: 'produce', label: t('Produce') }, { id: 'orders', label: t('Orders to produce') }, { id: 'disassemble', label: t('Disassemble') }];
  protected readonly tab = signal<string | null>('produce');
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly products = signal<SelectOption[]>([]);
  protected readonly units = signal<SelectOption[]>([]);
  protected readonly places = signal<SelectOption[]>([]);
  protected readonly orderTypes = signal<SelectOption[]>([]);
  /** The company's production and disassembly types: a choice shows only when there are several. */
  protected readonly produceTypes = signal<SelectOption[]>([]);
  protected readonly produceType = signal<string | null>(null);
  protected readonly disassemblyTypes = signal<SelectOption[]>([]);
  protected readonly disassemblyType = signal<string | null>(null);
  /** What the place holds of the product being taken apart. */
  protected readonly productHeld = signal<number | null>(null);
  protected readonly product = signal<string | null>(null);
  protected readonly unit = signal<string | null>(null);
  protected readonly quantity = signal('1');
  protected readonly place = signal<string | null>(null);
  protected readonly date = signal(today());
  protected readonly lines = signal<ProductionLine[] | null>(null);
  /** An ingredient added by hand: for a product with no recipe, or one the recipe does not list. */
  protected readonly newProduct = signal<string | null>(null);
  protected readonly newUnit = signal<string | null>(null);
  protected readonly newQuantity = signal('1');
  /** What taking the product apart gives back, adjustable before it is done. */
  protected readonly parts = signal<DisassemblyLine[] | null>(null);
  protected readonly orderType = signal<string | null>(null);
  protected readonly orders = signal<OrderToProduce[]>([]);
  protected readonly canCalculate = computed(() => !!this.product() && !!this.unit() && Number(this.quantity()) > 0);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    const scopes = await this.entities.lookupScopes().catch(() => []);
    const [products, units, places, types] = await Promise.all([registerOptions(this.entities, scopes, 'tbl_product'),
      registerOptions(this.entities, scopes, 'tbl_unit'), registerOptions(this.entities, scopes, 'tbl_cost_center'), this.documents.types().catch(() => [] as ElementType[])]);
    const orders = new Set(await this.api.orderTypes().catch(() => [] as string[]));
    const [making, apart] = await Promise.all([this.api.stockTypes('production').catch(() => [] as string[]), this.api.stockTypes('disassembly').catch(() => [] as string[])]);
    const named = (ids: string[]) => types.filter(t => t.id && ids.includes(t.id)).map(t => ({ value: t.id!, label: `${t.code ?? ''} ${textIn(t.name)}`.trim() }));
    this.produceTypes.set(named(making));
    this.produceType.set(making[0] ?? null);
    this.disassemblyTypes.set(named(apart));
    this.disassemblyType.set(apart[0] ?? null);
    this.products.set(products);
    this.units.set(units);
    this.places.set(places);
    this.orderTypes.set(types.filter(t => t.id && orders.has(t.id)).map(t => ({ value: t.id!, label: `${t.code ?? ''} ${textIn(t.name)}`.trim() })));
  }

  protected async calculate(): Promise<void> {
    this.message.set(null);
    try {
      this.lines.set(await this.api.calculate(this.product()!, this.unit()!, Number(this.quantity()), this.place()));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected setLine(i: number, field: 'quantity' | 'wasteQuantity', typed: string): void {
    const value = Number(typed.replace(',', '.'));
    this.lines.update(list => list?.map((l, n) => (n === i && !Number.isNaN(value) ? { ...l, [field]: value } : l)) ?? null);
  }

  protected addLine(): void {
    const productId = this.newProduct()!;
    const line: ProductionLine = { productId, name: this.products().find(p => p.value === productId)?.label ?? null, unitId: this.newUnit()!,
      quantity: Number(this.newQuantity().replace(',', '.')), wasteQuantity: 0, held: null };
    this.lines.update(list => [...(list ?? []).filter(l => l.productId !== productId), line]);
    this.newProduct.set(null);
    this.newQuantity.set('1');
  }

  protected removeLine(i: number): void {
    this.lines.update(list => list?.filter((_, n) => n !== i) ?? null);
  }

  protected async produce(): Promise<void> {
    if (!this.place()) {
      this.message.set(t('Choose where it is made.'));
      return;
    }
    await this.act(async () => {
      const document = await this.api.produce({ costCenterId: this.place()!, productId: this.product()!, unitId: this.unit()!, quantity: Number(this.quantity()),
        date: this.date() || null, lines: this.lines(), typeId: this.produceTypes().length > 1 ? this.produceType() : null });
      this.open(document, 'Produced');
    });
  }

  protected async calculateParts(): Promise<void> {
    this.message.set(null);
    try {
      this.parts.set(await this.api.calculateDisassembly(this.product()!, this.unit()!, Number(this.quantity()), this.place()));
      this.productHeld.set(this.place() ? await this.api.held(this.place()!, this.product()!, this.unit()!).catch(() => null) : null);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected setPart(i: number, field: 'recovered' | 'lost', typed: string): void {
    const value = Number(typed.replace(',', '.'));
    this.parts.update(list => list?.map((l, n) => (n === i && !Number.isNaN(value) ? { ...l, [field]: value } : l)) ?? null);
  }

  protected async disassemble(): Promise<void> {
    if (!this.place()) {
      this.message.set(t('Choose where it is taken apart.'));
      return;
    }
    await this.act(async () => {
      const document = await this.api.disassemble({ costCenterId: this.place()!, productId: this.product()!, unitId: this.unit()!, quantity: Number(this.quantity()),
        date: this.date() || null, lines: this.parts(), typeId: this.disassemblyTypes().length > 1 ? this.disassemblyType() : null });
      this.parts.set(null);
      this.open(document, 'Disassembled');
    });
  }

  protected async loadOrders(type: string | null): Promise<void> {
    this.orderType.set(type);
    this.orders.set([]);
    if (type) {
      await this.act(async () => this.orders.set(await this.api.ordersToProduce(type)));
    }
  }

  protected async produceOrder(order: OrderToProduce): Promise<void> {
    await this.act(async () => {
      const made = await this.api.produceOrder(order.id, this.place()!, this.date() || null);
      this.toasts.good(t('{0} produced: {1}.', order.code, made.map(d => d.code).join(', ')));
      this.orders.update(list => list.filter(o => o.id !== order.id));
    });
  }

  private open(document: DocumentData, verb: string): void {
    this.toasts.good(`${verb}: ${document.code}.`);
    if (document.id && document.categoryIds[0]) {
      void this.router.navigate(['/documents', document.categoryIds[0], document.id]);
    }
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await work();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected format(value: number): string {
    return formatNumber(value, 3);
  }

  protected day(value: string | null): string {
    return formatDate(value);
  }
}
