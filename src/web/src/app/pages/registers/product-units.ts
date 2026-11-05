import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { EntityApi, LookupScope, ProductUnit, errorsOf } from '../../core/entities';
import { Button } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Status } from '../../ui/status';
import { Toasts } from '../../ui/toast';

/** A unit moved from one place of the list to another. */
export function moveUnit<T>(list: readonly T[], from: number, to: number): T[] {
  const next = [...list];
  if (from < 0 || from >= next.length || to < 0 || to >= next.length) {
    return next;
  }
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

/** A unit added at the end, unless the list has it already. */
export function addUnit(list: readonly ProductUnit[], unit: ProductUnit): ProductUnit[] {
  return list.some(u => u.unitId === unit.unitId) ? [...list] : [...list, unit];
}

/** The units register's own lookup scope (the whole register: no category, type or record). */
export function unitScope(scopes: readonly LookupScope[]): LookupScope | null {
  return scopes.find(s => s.table === 'tbl_unit' && !s.categoryId && !s.typeId) ?? scopes.find(s => s.table === 'tbl_unit') ?? null;
}

/**
 * The units of a product (its Units tab) or of a product type (in the type editor), the founder's rules (2026-10-31): a product can have
 * more than one unit; the first is the default a document line proposes; a product with none of its own shows its type's. Reordered by
 * dragging (or the arrows), added by searching the units register; a unit in use (stock, prices, a document) cannot be removed.
 */
@Component({
  selector: 'app-product-units',
  imports: [TranslatePipe, Button, CdkDrag, CdkDragHandle, CdkDropList, Icon, Select, Status],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (loading()) {
      <p class="muted">{{ 'Loading…' | t }}</p>
    } @else {
      <p class="muted">{{ 'The first unit is the default a document line proposes.' | t }}@if (canWrite()) { {{ 'Drag the units to put them in order.' | t }} }</p>
      @if (mode() === 'product' && !units().length && typeUnits().length) {
        <div class="from-type">
          <span>{{ 'None of its own: it is offered its type\\'s units:' | t }}
            @for (u of typeUnits(); track u.unitId; let last = $last) { <strong>{{ u.name }}</strong>@if (!last) {, } }.</span>
          @if (canWrite()) { <button uiButton size="small" (click)="copyType()">{{ 'Use the type\\'s units' | t }}</button> }
        </div>
      }
      <ol class="units" cdkDropList [cdkDropListDisabled]="!canWrite()" (cdkDropListDropped)="dropped($event)" [attr.aria-label]="'Units' | t">
        @for (u of units(); track u.unitId; let i = $index; let first = $first; let last = $last) {
          <li cdkDrag>
            @if (canWrite()) { <span class="grip" cdkDragHandle aria-hidden="true"><ui-icon name="rows" [size]="16" /></span> }
            <span class="name">{{ u.name }} @if (u.code) { <span class="muted">{{ u.code }}</span> }</span>
            @if (first) { <ui-status kind="info">{{ 'Default' | t }}</ui-status> }
            @if (u.inUse) { <ui-status kind="neutral">{{ 'In use' | t }}</ui-status> }
            @if (canWrite()) {
              <span class="actions">
                <button uiButton variant="quiet" size="small" [iconOnly]="true" [disabled]="first" [attr.aria-label]="'Move ' + u.name + ' up'" (click)="move(i, i - 1)">
                  <ui-icon name="chevron-up" [size]="14" /></button>
                <button uiButton variant="quiet" size="small" [iconOnly]="true" [disabled]="last" [attr.aria-label]="'Move ' + u.name + ' down'" (click)="move(i, i + 1)">
                  <ui-icon name="chevron-down" [size]="14" /></button>
                <button uiButton variant="quiet" size="small" [iconOnly]="true" [disabled]="u.inUse" [attr.aria-label]="'Remove ' + u.name"
                  [attr.title]="u.inUse ? 'In use (stock, prices or a document): it stays' : null" (click)="remove(i)"><ui-icon name="trash" [size]="14" /></button>
              </span>
            }
          </li>
        } @empty {
          <li class="none muted">{{ 'No units of its own yet.' | t }}</li>
        }
      </ol>
      @if (canWrite()) {
        <div class="add">
          <ui-select [options]="options()" [searchable]="true" [value]="null" [placeholder]="'Add a unit…' | t" [ariaLabel]="'Add a unit' | t" (search)="find($event)"
            (valueChange)="add($event)" />
          <span class="spacer"></span>
          <button uiButton variant="primary" [disabled]="!dirty() || saving()" (click)="save()">{{ saving() ? ('Saving…' | t) : ('Save the units' | t) }}</button>
        </div>
      }
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-3); }
    p { margin: 0; }
    .muted { color: var(--muted); }
    .message { display: flex; align-items: center; gap: var(--space-2); padding: var(--space-2) var(--space-3); border-radius: var(--radius); background: var(--critical-bg);
      color: var(--critical); }
    .from-type { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-3); padding: var(--space-3); border-radius: var(--radius); background: var(--info-bg); }
    .units { display: grid; gap: var(--space-1); margin: 0; padding: 0; list-style: none; }
    .units li { display: flex; align-items: center; gap: var(--space-2); min-height: 40px; padding: 0 var(--space-2); border: 1px solid var(--line); border-radius: var(--radius);
      background: var(--surface); }
    .units li.none { border-style: dashed; }
    .grip { display: grid; place-items: center; color: var(--muted); cursor: grab; }
    .name { flex: 1; }
    .actions { display: flex; gap: 2px; }
    .add { display: flex; align-items: center; gap: var(--space-3); }
    .add ui-select { width: 280px; }
    .spacer { flex: 1; }
    .cdk-drag-preview { box-shadow: var(--shadow-pop); }
    .cdk-drag-placeholder { opacity: 0.4; }
  `,
})
export class ProductUnits {
  private readonly api = inject(EntityApi);
  private readonly toasts = inject(Toasts);

  /** A product's units or a product type's. */
  readonly mode = input.required<'product' | 'type'>();
  readonly id = input.required<string>();
  readonly scopes = input<LookupScope[]>([]);
  /** For a type: only an administrator changes its units. */
  readonly administrator = input(false);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly units = signal<ProductUnit[]>([]);
  protected readonly typeUnits = signal<ProductUnit[]>([]);
  private readonly saved = signal<string[]>([]);
  private readonly writable = signal(false);
  private readonly found = signal<ProductUnit[]>([]);
  protected readonly canWrite = computed(() => (this.mode() === 'type' ? this.administrator() : this.writable()));
  protected readonly dirty = computed(() => this.units().map(u => u.unitId).join() !== this.saved().join());
  protected readonly options = computed<SelectOption[]>(() => this.found().filter(f => !this.units().some(u => u.unitId === f.unitId))
    .map(u => ({ value: u.unitId, label: u.name ?? u.code ?? u.unitId, hint: u.code ?? undefined })));

  constructor() {
    effect(() => {
      const [mode, id] = [this.mode(), this.id()];
      untracked(() => void this.load(mode, id));
    });
  }

  private async load(mode: 'product' | 'type', id: string): Promise<void> {
    this.loading.set(true);
    this.message.set(null);
    try {
      if (mode === 'product') {
        const units = await this.api.productUnits(id);
        this.show(units.units);
        this.typeUnits.set(units.typeUnits);
        this.writable.set(units.canWrite);
      } else {
        this.show(await this.api.typeUnits(id));
      }
      await this.find('');
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  private show(units: ProductUnit[]): void {
    this.units.set(units);
    this.saved.set(units.map(u => u.unitId));
  }

  protected async find(search: string): Promise<void> {
    const scope = unitScope(this.scopes());
    if (!scope) {
      return;
    }
    const page = await this.api.lookup(scope.id, search, 50).catch(() => null);
    this.found.set((page?.items ?? []).map(o => ({ unitId: o.id, code: o.code, name: o.name || o.code, inUse: false })));
  }

  protected add(unitId: string | null): void {
    const unit = this.found().find(u => u.unitId === unitId);
    if (unit) {
      this.units.update(list => addUnit(list, unit));
    }
  }

  protected copyType(): void {
    this.units.set(this.typeUnits().map(u => ({ ...u, inUse: false })));
  }

  protected move(from: number, to: number): void {
    this.units.update(list => moveUnit(list, from, to));
  }

  protected dropped(event: CdkDragDrop<unknown>): void {
    this.move(event.previousIndex, event.currentIndex);
  }

  protected remove(index: number): void {
    this.units.update(list => list.filter((_, i) => i !== index));
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.message.set(null);
    try {
      const ids = this.units().map(u => u.unitId);
      if (this.mode() === 'product') {
        const units = await this.api.saveProductUnits(this.id(), ids);
        this.show(units.units);
        this.typeUnits.set(units.typeUnits);
      } else {
        this.show(await this.api.saveTypeUnits(this.id(), ids));
      }
      this.toasts.good(t('Units saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.saving.set(false);
    }
  }
}
