import { TranslatePipe } from '../../core/t.pipe';
import { language, t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ConfigApi, ConfigRow } from '../../core/config';
import { Element, ElementType, EntityApi, RightsService, errorsOf } from '../../core/entities';
import { PosApi, PosPlace, PosSettings } from '../../core/pos';
import { Button, Field, Input } from '../../ui/controls';
import { textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Toasts } from '../../ui/toast';

const MODEL = 'point-of-sale-settings';

/** What a point of sale works with: each column of its settings row, with the name the settings give what applies now. */
const COLUMNS = [
  { key: 'billTypeId', label: t('Bill'), shown: 'billType' },
  { key: 'orderTypeId', label: t('Order sent first, bill at payment'), shown: 'orderType' },
  { key: 'moneyInTypeId', label: t('Money in'), shown: 'moneyInType' },
  { key: 'moneyOutTypeId', label: t('Money out'), shown: 'moneyOutType' },
  { key: 'cashDeskId', label: t('Cash desk'), shown: 'cashDesk' },
  { key: 'walkInClientId', label: t('Client of a walk-in bill'), shown: 'walkInClient' },
  { key: 'paymentMethodId', label: t('Paid by default'), shown: 'paymentMethod' },
  { key: 'productId', label: t('A night is sold as'), shown: 'product' },
] as const;

export interface PlaceRow {
  place: PosPlace;
  depth: number;
}

/** The places of a point of sale as a tree: each under the place above it. */
export function placeRows(places: readonly PosPlace[]): PlaceRow[] {
  const rows: PlaceRow[] = [];
  const add = (parent: string | null, depth: number) => {
    for (const place of places.filter(p => p.parentId === parent)) {
      rows.push({ place, depth });
      add(place.id, depth + 1);
    }
  };
  add(null, 0);
  return rows;
}

/** The types of row that go under a row of this type: its children, past folders of types. */
export function typesUnder(types: readonly ElementType[], parentType: string | null): ElementType[] {
  const found: ElementType[] = [];
  for (const type of types.filter(t => t.idSup === parentType && t.active !== false)) {
    if (type.isElement) {
      found.push(type);
    } else {
      found.push(...typesUnder(types, type.id!));
    }
  }
  return found;
}

/**
 * A point of sale's own configuration (the founder, 2026-10-31), opened when one is created: what it works with, over what its kind and the
 * company set, and its places, which the kind decides: halls and tables, tills, auditoriums with rows and seats, floors with rooms.
 */
@Component({
  selector: 'app-pos-setup',
  imports: [TranslatePipe, Button, Field, Icon, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <h3>{{ 'What it works with' | t }}</h3>
    <p class="muted">{{ 'Empty takes what its kind or the company sets, shown in grey.' | t }}</p>
    <div class="grid">
      @for (c of shownColumns(); track c.key) {
        <ui-field [label]="c.label" [required]="missing().includes(c.key)">
          <ui-select [options]="$any(options()[c.key]) ?? []" [searchable]="true" [value]="$any(own()[c.key] ?? null)" [placeholder]="inherited(c.shown)" [ariaLabel]="c.label"
            [disabled]="!canWrite()" (valueChange)="set(c.key, $any($event))" />
        </ui-field>
      }
      @if (!till()) {
      <ui-field [label]="'Screen' | t">
        <ui-select [options]="screenOptions" [value]="$any(own()['screen'] ?? null)" [placeholder]="screenName(settings()?.screen)" [ariaLabel]="'Screen' | t" [disabled]="!canWrite()" (valueChange)="set('screen', $any($event))" />
      </ui-field>
      }
      <ui-field [label]="'Bills need an open shift' | t">
        <ui-select [options]="$any(shiftOptions)" [value]="$any(own()['requiresShift'] ?? null)" [placeholder]="(settings()?.requiresShift === false ? 'No' : 'Yes') | t" [ariaLabel]="'Bills need an open shift' | t" [disabled]="!canWrite()" (valueChange)="set('requiresShift', $any($event))" />
      </ui-field>
    </div>
    @if (canWrite()) {
      <div class="row"><span class="spacer"></span><button uiButton variant="primary" [disabled]="saving()" (click)="save()">{{ saving() ? ('Saving…' | t) : ('Save the settings' | t) }}</button></div>
    }

    @if (till()) {
      <p class="muted">{{ tillNote }}</p>
    } @else {
    <h3>{{ 'Its places' | t }}</h3>
    <ul class="places">
      @for (r of rows(); track r.place.id) {
        <li [style.padding-left.px]="8 + r.depth * 20">{{ r.place.name ?? r.place.code }}</li>
      } @empty {
        <li class="none muted">{{ 'No places yet.' | t }}</li>
      }
    </ul>
    @if (canWrite()) {
      <div class="row">
        <ui-select class="under" [options]="parents()" [value]="under()" [ariaLabel]="'Under' | t" (valueChange)="chooseUnder($event)" />
        <ui-select class="kind" [options]="addable()" [value]="addType()" [placeholder]="'Nothing goes here' | t" [ariaLabel]="'Kind of place' | t" (valueChange)="addType.set($event)" />
        <input uiInput [attr.aria-label]="'Name of the place' | t" [placeholder]="'Name' | t" [value]="addName()" (input)="addName.set($any($event.target).value)" />
        <button uiButton [disabled]="!addType() || !addName().trim() || saving()" (click)="addPlace()"><ui-icon name="plus" />{{ 'Add' | t }}</button>
      </div>
    }
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-3); }
    h3 { margin: var(--space-2) 0 0; font-size: 1rem; }
    p { margin: 0; }
    .muted { color: var(--muted); }
    .message { display: flex; align-items: center; gap: var(--space-2); padding: var(--space-2) var(--space-3); border-radius: var(--radius); background: var(--critical-bg);
      color: var(--critical); }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: var(--space-3); }
    .row { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2); }
    .row .under, .row .kind { width: 180px; }
    .row input { flex: 1; min-width: 140px; }
    .spacer { flex: 1; }
    .places { display: grid; gap: 2px; margin: 0; padding: 0; list-style: none; }
    .places li { padding-block: var(--space-1); border-bottom: 1px solid var(--line); }
    .places li.none { border: 0; }
  `,
})
export class PosSetup {
  private readonly config = inject(ConfigApi);
  private readonly pos = inject(PosApi);
  private readonly entities = inject(EntityApi);
  private readonly rights = inject(RightsService);
  private readonly toasts = inject(Toasts);

  readonly element = input.required<Element>();
  readonly types = input<ElementType[]>([]);
  /** A till below a point of sale: only its own cash desk and shift rule; the rest comes from its shop. */
  readonly till = input(false);
  protected readonly tillNote = t("A till works as its shop: the cashier signs in here, bills take the shop's stock and record the till, and the till has its own shift. Give it its own cash desk to count its drawer on its own.");

  /** What a night is sold as matters at a hotel only. */
  protected readonly shownColumns = computed(() => this.till() ? COLUMNS.filter(c => c.key === 'cashDeskId')
    : COLUMNS.filter(c => c.key !== 'productId' || this.settings()?.screen === 'stays'));
  protected readonly message = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly settings = signal<PosSettings | null>(null);
  protected readonly own = signal<ConfigRow>({});
  /** The screens a point of sale can open; empty takes its kind's. */
  protected readonly screenOptions: SelectOption[] = [{ value: 'tables', label: t('Tables') }, { value: 'shop', label: t('Shop') }, { value: 'events', label: t('Cinema / theatre') },
    { value: 'stays', label: t('Hotel') }, { value: 'repairs', label: t('Repair shop') }];
  protected readonly shiftOptions = [{ value: true, label: t('Yes') }, { value: false, label: t('No') }];
  protected screenName(screen: string | null | undefined): string {
    return this.screenOptions.find(o => o.value === screen)?.label ?? '';
  }
  protected readonly options = signal<Record<string, SelectOption[]>>({});
  protected readonly places = signal<PosPlace[]>([]);
  protected readonly under = signal<string | null>(null);
  protected readonly addType = signal<string | null>(null);
  protected readonly addName = signal('');

  protected readonly canWrite = computed(() => this.rights.isAdministrator());
  protected readonly missing = computed<readonly string[]>(() => this.settings()?.missing ?? []);
  protected readonly rows = computed(() => placeRows(this.places()));
  protected readonly parents = computed<SelectOption[]>(() => [
    { value: this.element().id!, label: textIn(this.element().name) || this.element().code || '' },
    ...this.rows().map(r => ({ value: r.place.id, label: `${'  '.repeat(r.depth + 1)}${r.place.name ?? r.place.code ?? ''}` })),
  ]);
  private readonly parentType = computed(() => {
    const under = this.under();
    return !under || under === this.element().id ? this.element().typeId : this.places().find(p => p.id === under)?.typeId ?? null;
  });
  protected readonly addable = computed<SelectOption[]>(() => typesUnder(this.types(), this.parentType()).map(t => ({ value: t.id!, label: textIn(t.name) })));

  constructor() {
    effect(() => {
      const id = this.element().id;
      if (id) {
        untracked(() => void this.load(id));
      }
    });
    effect(() => {
      const first = this.addable().at(0)?.value ?? null;
      untracked(() => {
        if (!this.addable().some(o => o.value === this.addType())) {
          this.addType.set(first);
        }
      });
    });
  }

  protected inherited(shown: (typeof COLUMNS)[number]['shown']): string {
    return (shown === 'product' ? this.settings()?.product : this.settings()?.[shown]) ?? 'Not set';
  }

  protected set(key: string, value: string | boolean | null): void {
    this.own.update(row => ({ ...row, [key]: value }));
  }

  protected chooseUnder(id: string | null): void {
    this.under.set(id);
  }

  private async load(id: string): Promise<void> {
    this.under.set(id);
    try {
      const [rows, settings, places, ...choices] = await Promise.all([this.config.list(MODEL), this.pos.settings(id), this.pos.places(id),
        ...COLUMNS.map(c => this.config.choices(MODEL, c.key))]);
      this.own.set(rows.find(r => r['pointOfSaleId'] === id) ?? {});
      this.settings.set(settings);
      this.places.set(places);
      this.options.set(Object.fromEntries(COLUMNS.map((c, i) => [c.key, choices[i].map(o => ({ value: o.id, label: o.name, hint: o.code ?? undefined }))])));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async save(): Promise<void> {
    const id = this.element().id!;
    this.saving.set(true);
    this.message.set(null);
    try {
      this.own.set(await this.config.save(MODEL, { ...this.own(), pointOfSaleId: id }));
      this.settings.set(await this.pos.settings(id));
      this.toasts.good(t('Settings saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.saving.set(false);
    }
  }

  protected async addPlace(): Promise<void> {
    const id = this.element().id!;
    const type = this.types().find(t => t.id === this.addType());
    if (!type) {
      return;
    }
    this.saving.set(true);
    this.message.set(null);
    try {
      await this.entities.saveElement({ key: 'cost-centers', api: '/api/v1/cost-centers', table: 'tbl_cost_center', hasElements: true }, {
        typeId: type.id!, idSup: this.under() ?? id, name: { en: this.addName().trim(), [language()]: this.addName().trim() }, categoryIds: type.categoryIds.slice(0, 1), values: {},
      });
      this.addName.set('');
      this.places.set(await this.pos.places(id));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.saving.set(false);
    }
  }
}
