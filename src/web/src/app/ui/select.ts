import { CdkConnectedOverlay, CdkOverlayOrigin, type ConnectedPosition } from '@angular/cdk/overlay';
import {
  ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, inject, input, model, output, signal, viewChild,
} from '@angular/core';
import { text } from '../core/text';
import { Field, uniqueId } from './controls';
import { Icon } from './icon';

export interface SelectOption<T = string> {
  value: T;
  label: string;
  /** A second, quieter text: a code, a balance. */
  hint?: string;
  disabled?: boolean;
}

/** The options whose label or hint holds every word of the search, in their order. */
export function filterOptions<T>(options: readonly SelectOption<T>[], search: string): SelectOption<T>[] {
  const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) {
    return [...options];
  }
  return options.filter(o => {
    const haystack = `${o.label} ${o.hint ?? ''}`.toLowerCase();
    return words.every(w => haystack.includes(w));
  });
}

const BELOW: ConnectedPosition[] = [
  { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetY: 4 },
  { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetY: -4 },
];

/**
 * A dropdown to pick one value ([(value)]) or several ([multiple]="true", [(values)]). With more than 8 options, or [searchable], it has
 * a search box; (search) also tells the screen what was typed, so a lookup can load its options from the API.
 */
@Component({
  selector: 'ui-select',
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button #trigger type="button" class="control trigger" cdkOverlayOrigin #origin="cdkOverlayOrigin" role="combobox"
      aria-haspopup="listbox" [attr.aria-expanded]="open()" [attr.aria-controls]="open() ? listId : null"
      [attr.aria-activedescendant]="open() && !hasSearch() ? activeId() : null" [attr.id]="field?.id ?? null"
      [attr.aria-describedby]="field?.describedBy() ?? null" [attr.aria-invalid]="field?.error() ? 'true' : null"
      [attr.aria-label]="ariaLabel()" [disabled]="disabled()" (click)="toggle()" (keydown)="onKey($event)">
      <span class="shown" [class.placeholder]="!shown()">{{ shown() || placeholder() || text.ui.choose }}</span>
      <ui-icon name="chevron-down" />
    </button>
    <ng-template cdkConnectedOverlay [cdkConnectedOverlayOrigin]="origin" [cdkConnectedOverlayOpen]="open()"
      [cdkConnectedOverlayPositions]="positions" [cdkConnectedOverlayMinWidth]="width()" (overlayOutsideClick)="close()"
      (detach)="close()" (overlayKeydown)="$event.key === 'Escape' && close(true)" (attach)="attached()">
      <div class="select-panel">
        @if (hasSearch()) {
          <input #searchBox class="control search" type="search" [placeholder]="text.ui.search" [value]="query()"
            role="combobox" aria-autocomplete="list" [attr.aria-controls]="listId" [attr.aria-activedescendant]="activeId()"
            [attr.aria-label]="text.ui.search" (input)="onSearch($any($event.target).value)" (keydown)="onKey($event)" />
        }
        <ul role="listbox" [id]="listId" [attr.aria-multiselectable]="multiple() || null">
          @for (option of shownOptions(); track $index; let i = $index) {
            <li role="option" [id]="listId + '-' + i" [attr.aria-selected]="isChosen(option)" [attr.aria-disabled]="option.disabled || null"
              [class.active]="i === active()" (mouseenter)="active.set(i)" (mousedown)="$event.preventDefault()" (click)="pick(option)">
              <span class="tick"><ui-icon [name]="isChosen(option) ? 'check' : null" [size]="14" /></span>
              <span class="label">{{ option.label }}</span>
              @if (option.hint) { <span class="hint">{{ option.hint }}</span> }
            </li>
          } @empty {
            <li class="empty">{{ text.ui.noMatch }}</li>
          }
        </ul>
      </div>
    </ng-template>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .trigger { display: flex; align-items: center; gap: var(--space-2); text-align: left; cursor: pointer; }
    .trigger:disabled { cursor: default; }
    .shown { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .placeholder { color: var(--muted); }
    .search { flex: none; margin-bottom: var(--space-1); }
    ul { flex: 1; min-height: 0; margin: 0; padding: 0; overflow-y: auto; list-style: none; }
    li { display: flex; align-items: center; gap: var(--space-2); min-height: 32px; padding: var(--space-1) var(--space-2);
      border-radius: var(--radius-s); cursor: pointer; }
    li.active { background: var(--hover); }
    li[aria-selected='true'] { font-weight: 500; }
    li[aria-disabled='true'] { opacity: 0.5; cursor: default; }
    .tick { display: inline-flex; width: 14px; flex: none; }
    .label { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .hint { color: var(--muted); font-size: var(--text-s); }
    .empty { color: var(--muted); cursor: default; }
  `,
})
export class Select<T = string> {
  protected readonly text = text;
  protected readonly field = inject(Field, { optional: true });
  private readonly injector = inject(Injector);
  protected readonly positions = BELOW;
  protected readonly listId = uniqueId('listbox');

  readonly options = input<readonly SelectOption<T>[]>([]);
  readonly placeholder = input<string | null>(null);
  readonly multiple = input(false);
  /** Always show the search box; null: only when there are more than 8 options. */
  readonly searchable = input<boolean | null>(null);
  readonly disabled = input(false);
  readonly ariaLabel = input<string | null>(null);
  /** The value picked (one). */
  readonly value = model<T | null>(null);
  /** The values picked (multiple). */
  readonly values = model<T[]>([]);
  /** What is typed in the search box. */
  readonly search = output<string>();

  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly active = signal(0);
  protected readonly width = signal(0);
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');

  protected readonly hasSearch = computed(() => this.searchable() ?? this.options().length > 8);
  protected readonly shownOptions = computed(() => filterOptions(this.options(), this.query()));
  protected readonly activeId = computed(() => (this.shownOptions().length ? `${this.listId}-${this.active()}` : null));
  protected readonly shown = computed(() => {
    const options = this.options();
    if (this.multiple()) {
      const labels = this.values().map(v => options.find(o => o.value === v)?.label).filter(Boolean);
      return labels.join(', ');
    }
    // An option may stand for no value (null), e.g. "— At the top —".
    const value = this.value();
    return options.find(o => o.value === value)?.label ?? '';
  });

  protected isChosen(option: SelectOption<T>): boolean {
    return this.multiple() ? this.values().includes(option.value) : this.value() === option.value;
  }

  protected toggle(): void {
    if (this.open()) {
      this.close();
    } else {
      this.show();
    }
  }

  private show(): void {
    this.width.set(this.trigger().nativeElement.offsetWidth);
    this.query.set('');
    const chosen = this.shownOptions().findIndex(o => this.isChosen(o));
    this.active.set(Math.max(chosen, 0));
    this.open.set(true);
  }

  protected close(refocus = false): void {
    if (!this.open()) {
      return;
    }
    this.open.set(false);
    if (refocus) {
      this.trigger().nativeElement.focus();
    }
  }

  protected attached(): void {
    afterNextRender(() => this.searchBox()?.nativeElement.focus(), { injector: this.injector });
  }

  protected onSearch(query: string): void {
    this.query.set(query);
    this.active.set(0);
    this.search.emit(query);
  }

  protected pick(option: SelectOption<T>): void {
    if (option.disabled) {
      return;
    }
    if (this.multiple()) {
      const values = this.values();
      this.values.set(values.includes(option.value) ? values.filter(v => v !== option.value) : [...values, option.value]);
      return;
    }
    this.value.set(option.value);
    this.close(true);
  }

  protected onKey(event: KeyboardEvent): void {
    const count = this.shownOptions().length;
    if (!this.open()) {
      if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) {
        event.preventDefault();
        this.show();
      }
      return;
    }
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.active.set(count ? (this.active() + 1) % count : 0);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.active.set(count ? (this.active() - 1 + count) % count : 0);
        break;
      case 'Home':
      case 'End':
        if (!this.hasSearch()) {
          event.preventDefault();
          this.active.set(event.key === 'Home' ? 0 : Math.max(count - 1, 0));
        }
        break;
      case 'Enter': {
        event.preventDefault();
        const option = this.shownOptions()[this.active()];
        if (option) {
          this.pick(option);
        }
        break;
      }
      case ' ':
        if (!this.hasSearch()) {
          event.preventDefault();
          const option = this.shownOptions()[this.active()];
          if (option) {
            this.pick(option);
          }
        }
        break;
      case 'Escape':
        event.preventDefault();
        this.close(true);
        break;
      case 'Tab':
        this.close();
        break;
    }
  }
}
