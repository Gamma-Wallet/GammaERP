import { TranslatePipe } from '../../core/t.pipe';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { EntityApi, errorsOf } from '../../core/entities';
import { DepreciationBook, OperationsApi } from '../../core/operations';
import { Button, Field } from '../../ui/controls';
import { formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { registerOptions } from './register-options';

/** What an asset is still worth in a depreciation book: its values less their depreciation. */
@Component({
  selector: 'app-asset-value',
  imports: [TranslatePipe, Button, Field, Icon, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Remaining asset value' | t }}</h1></div>
    <section class="form">
      <ui-field [label]="'Book' | t" [required]="true"><ui-select [options]="bookOptions()" [value]="book()" (valueChange)="choose(book, $event)" /></ui-field>
      <ui-field [label]="'Asset (product)' | t" [required]="true"><ui-select [options]="products()" [searchable]="true" [value]="asset()" (valueChange)="choose(asset, $event)" /></ui-field>
      <ui-field [label]="'Unit' | t" [required]="true"><ui-select [options]="units()" [searchable]="true" [value]="unit()" (valueChange)="choose(unit, $event)" /></ui-field>
      <div class="actions"><button uiButton variant="primary" [disabled]="!book() || !asset() || !unit() || busy()" (click)="show()">{{ 'Show' | t }}</button></div>
    </section>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (remaining() !== null) { <p class="result">{{ 'Remaining value' | t }} <strong class="figures">{{ n(remaining()!) }}</strong></p> }
  `,
  styleUrl: './operations.scss',
  styles: `.result { font-size: 1.25rem; }`,
})
export class AssetValue {
  private readonly api = inject(OperationsApi);
  private readonly entities = inject(EntityApi);

  protected readonly n = formatNumber;
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  private readonly books = signal<DepreciationBook[]>([]);
  protected readonly products = signal<SelectOption[]>([]);
  protected readonly units = signal<SelectOption[]>([]);
  protected readonly book = signal<string | null>(null);
  protected readonly asset = signal<string | null>(null);
  protected readonly unit = signal<string | null>(null);
  protected readonly remaining = signal<number | null>(null);
  protected readonly bookOptions = computed<SelectOption[]>(() => this.books().map(b => ({ value: b.id, label: b.name })));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const scopes = await this.entities.lookupScopes().catch(() => []);
      const [books, products, units] = await Promise.all([this.api.books(), this.api.assetProducts(), registerOptions(this.entities, scopes, 'tbl_unit')]);
      this.books.set(books);
      this.products.set(products);
      this.units.set(units);
      if (books.length === 1) {
        this.book.set(books[0].id);
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected choose(field: { set(value: string | null): void }, value: string | null): void {
    field.set(value);
    this.remaining.set(null);
  }

  protected async show(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      this.remaining.set(await this.api.remainingValue(this.asset()!, this.unit()!, this.book()!));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
