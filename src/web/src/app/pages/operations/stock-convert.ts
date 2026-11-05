import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { EntityApi, errorsOf } from '../../core/entities';
import { OperationsApi } from '../../core/operations';
import { DocumentApi } from '../../core/documents';
import { Button, Field, Input } from '../../ui/controls';
import { formatNumber, textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Toasts } from '../../ui/toast';
import { registerOptions, today } from './register-options';

/**
 * Stock conversion: a quantity of a product at a place turned from one unit into another by the conversion's
 * factor (the product's own, else the general one), shown before it is made; it opens the conversion document.
 */
@Component({
  selector: 'app-stock-convert',
  imports: [TranslatePipe, Button, Field, Icon, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Convert stock between units' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <section class="form">
      <ui-field [label]="'At' | t" [required]="true"><ui-select [options]="places()" [searchable]="true" [value]="place()" (valueChange)="place.set($event)" /></ui-field>
      <ui-field [label]="'Product' | t" [required]="true"><ui-select [options]="products()" [searchable]="true" [value]="product()" (valueChange)="product.set($event)" /></ui-field>
      <ui-field [label]="'From unit' | t" [required]="true"><ui-select [options]="unitChoices()" [searchable]="true" [value]="from()" (valueChange)="from.set($event)" /></ui-field>
      <ui-field [label]="'To unit' | t" [required]="true"><ui-select [options]="unitChoices()" [searchable]="true" [value]="to()" (valueChange)="to.set($event)" /></ui-field>
      <ui-field [label]="'Quantity' | t" [required]="true"><input uiInput [numeric]="true" inputmode="decimal" [value]="quantity()" (input)="quantity.set($any($event.target).value)" /></ui-field>
      <ui-field [label]="'Date' | t"><input uiInput type="date" [value]="date()" (input)="date.set($any($event.target).value)" /></ui-field>
      @if (splitTypes().length > 1) {
        <ui-field [label]="'Document type' | t" [required]="true"><ui-select [options]="splitTypes()" [value]="splitType()" (valueChange)="splitType.set($event)" /></ui-field>
      }
    </section>
    @if (from() && to()) {
      <p class="preview" role="status">
        @if (factor() === null) {
          <span class="muted">{{ 'These units do not convert: add a unit conversion in Configuration first.' | t }}</span>
        } @else {
          {{ format(qty()) }} {{ unitName(from()) }} = <strong>{{ format(qty() * factor()!) }} {{ unitName(to()) }}</strong>
          <span class="muted">(1 = {{ format(factor()!) }})</span>
        }
      </p>
    }
    <div><button uiButton variant="primary" [disabled]="!ready() || busy()" (click)="convert()">{{ 'Convert' | t }}</button></div>
  `,
  styleUrl: './operations.scss',
  styles: `.preview { font-size: var(--text-l); }`,
})
export class StockConvert {
  private readonly api = inject(OperationsApi);
  private readonly entities = inject(EntityApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);

  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly places = signal<SelectOption[]>([]);
  protected readonly products = signal<SelectOption[]>([]);
  protected readonly units = signal<SelectOption[]>([]);
  protected readonly place = signal<string | null>(null);
  protected readonly product = signal<string | null>(null);
  protected readonly from = signal<string | null>(null);
  protected readonly to = signal<string | null>(null);
  protected readonly quantity = signal('1');
  protected readonly date = signal(today());
  /** The product's units (its own, else its type's); every unit until a product is chosen. */
  private readonly productUnits = signal<Set<string> | null>(null);
  protected readonly unitChoices = computed(() => {
    const own = this.productUnits();
    return own ? this.units().filter(u => own.has(u.value)) : this.units();
  });
  private readonly loadProductUnits = effect(() => {
    const product = this.product();
    untracked(() => {
      this.productUnits.set(null);
      if (product) {
        void this.entities.productUnits(product).then(u => this.productUnits.set(new Set((u.units.length ? u.units : u.typeUnits).map(x => x.unitId)))).catch(() => undefined);
      }
    });
  });
  /** The company's unit split types: a choice shows only when there are several. */
  protected readonly splitTypes = signal<SelectOption[]>([]);
  protected readonly splitType = signal<string | null>(null);
  private readonly documents = inject(DocumentApi);
  protected readonly factor = signal<number | null>(null);
  protected readonly qty = computed(() => Number(this.quantity().replace(',', '.')) || 0);
  protected readonly ready = computed(() => !!this.place() && !!this.product() && !!this.from() && !!this.to() && this.factor() !== null && this.qty() > 0);

  constructor() {
    void this.load();
    effect(() => {
      const [from, to, product] = [this.from(), this.to(), this.product()];
      untracked(() => void this.loadFactor(from, to, product));
    });
  }

  private async load(): Promise<void> {
    const scopes = await this.entities.lookupScopes().catch(() => []);
    const [places, products, units] = await Promise.all([registerOptions(this.entities, scopes, 'tbl_cost_center'),
      registerOptions(this.entities, scopes, 'tbl_product'), registerOptions(this.entities, scopes, 'tbl_unit')]);
    this.places.set(places);
    this.products.set(products);
    this.units.set(units);
    const ids = await this.api.stockTypes('unit-split').catch(() => [] as string[]);
    if (ids.length > 1) {
      const types = await this.documents.types().catch(() => []);
      this.splitTypes.set(types.filter(t => t.id && ids.includes(t.id)).map(t => ({ value: t.id!, label: `${t.code ?? ''} ${textIn(t.name)}`.trim() })));
      this.splitType.set(ids[0]);
    }
  }

  private async loadFactor(from: string | null, to: string | null, product: string | null): Promise<void> {
    this.factor.set(from && to ? await this.api.factor(from, to, product) : null);
  }

  protected unitName(id: string | null): string {
    return this.units().find(u => u.value === id)?.label ?? '';
  }

  protected format(value: number): string {
    return formatNumber(value, 3);
  }

  protected async convert(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      const document = await this.api.convert({ costCenterId: this.place()!, productId: this.product()!, fromUnitId: this.from()!, toUnitId: this.to()!,
        quantity: this.qty(), date: this.date() || null, typeId: this.splitTypes().length > 1 ? this.splitType() : null });
      this.toasts.good(t('Converted: {0}.', document.code));
      if (document.id && document.categoryIds[0]) {
        void this.router.navigate(['/documents', document.categoryIds[0], document.id]);
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
