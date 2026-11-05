import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, Injectable, computed, inject, input, model } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Checkbox } from '../../ui/controls';
import { Icon } from '../../ui/icon';

export interface SetupCountry {
  code: string;
  currency: string | null;
  languages: string[];
  profiles: string[];
}

export interface Activity {
  code: string;
  parent: string | null;
  name: Record<string, string>;
}

export interface PackGroup {
  name: string;
  requires: string[];
}

/** The 30 countries with a starter pack, by their code. */
export const COUNTRY_NAMES: Record<string, string> = {
  AL: t('Albania'), AT: t('Austria'), BE: t('Belgium'), BG: t('Bulgaria'), CY: t('Cyprus'), CZ: t('Czechia'), DE: t('Germany'), DK: t('Denmark'), EE: t('Estonia'), ES: t('Spain'),
  FI: t('Finland'), FR: t('France'), GR: t('Greece'), HR: t('Croatia'), HU: t('Hungary'), IE: t('Ireland'), IT: t('Italy'), LT: t('Lithuania'), LU: t('Luxembourg'), LV: t('Latvia'),
  MK: t('North Macedonia'), MT: t('Malta'), NL: t('Netherlands'), PL: t('Poland'), PT: t('Portugal'), RO: t('Romania'), SE: t('Sweden'), SI: t('Slovenia'), SK: t('Slovakia'), XK: t('Kosovo'),
};

/** The pack groups' names on the screen. */
export const GROUP_NAMES: Record<string, string | undefined> = {
  chart: t('Chart of accounts'), documents: t('Financial documents'), posting: t('Posting groups'), depreciation: t('Depreciation'),
};

/** The activities under one (the top ones under none), by name. */
export function childrenOf(catalogue: readonly Activity[], parent: string | null): Activity[] {
  return catalogue.filter(a => a.parent === parent).sort((a, b) => (a.name['en'] ?? a.code).localeCompare(b.name['en'] ?? b.code));
}

/** The activity and the ones above it, from the top down. */
export function pathOf(catalogue: readonly Activity[], code: string | null): Activity[] {
  const path: Activity[] = [];
  for (let current = catalogue.find(a => a.code === code); current; current = catalogue.find(a => a.code === current!.parent)) {
    path.unshift(current);
  }
  return path;
}

/**
 * The groups chosen after ticking or unticking one: a ticked group brings what it needs, an unticked one takes along every group that needs it
 * (posting needs the chart and the documents).
 */
export function toggleGroup(groups: readonly PackGroup[], chosen: readonly string[], name: string, on: boolean): string[] {
  const result = new Set(chosen);
  if (on) {
    const add = (g: string) => {
      if (!result.has(g)) {
        result.add(g);
        groups.find(x => x.name === g)?.requires.forEach(add);
      }
    };
    add(name);
  } else {
    const remove = (g: string) => {
      if (result.delete(g)) {
        groups.filter(x => x.requires.includes(g)).forEach(x => remove(x.name));
      }
    };
    remove(name);
  }
  return groups.map(g => g.name).filter(g => result.has(g));
}

/** What a new company of a group starts from: countries, the activity catalogue, the pack groups. */
@Injectable({ providedIn: 'root' })
export class SetupApi {
  private readonly http = inject(HttpClient);

  countries(): Promise<SetupCountry[]> {
    return firstValueFrom(this.http.get<SetupCountry[]>('/api/v1/setup/countries'));
  }

  activities(): Promise<Activity[]> {
    return firstValueFrom(this.http.get<Activity[]>('/api/v1/setup/activities'));
  }

  groups(): Promise<PackGroup[]> {
    return firstValueFrom(this.http.get<PackGroup[]>('/api/v1/setup/groups'));
  }

  addCompany(request: object): Promise<{ company: string; rows: number }> {
    return firstValueFrom(this.http.post<{ company: string; rows: number }>('/api/v1/companies', request));
  }
}

/** The activity, chosen level by level from the catalogue: the path so far, then the activities under the last one. */
@Component({
  selector: 'app-activity-picker',
  imports: [TranslatePipe, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="path" [attr.aria-label]="'Activity' | t">
      @for (a of path(); track a.code; let last = $last) {
        <button type="button" class="crumb" [class.current]="last" (click)="value.set(a.code)">{{ name(a) }}</button>
        @if (!last) { <ui-icon name="chevron-right" [size]="14" /> }
      }
    </nav>
    @if (children().length) {
      <div class="choices" role="group" [attr.aria-label]="'More precisely' | t">
        @for (a of children(); track a.code) {
          <button type="button" class="choice" (click)="value.set(a.code)">{{ name(a) }}</button>
        }
      </div>
    } @else {
      <p class="muted">{{ 'No finer activity under this one.' | t }}</p>
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-3); }
    .path { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-1); }
    .crumb { padding: 2px var(--space-2); border: 0; border-radius: var(--radius-s); background: none; color: var(--link); cursor: pointer; }
    .crumb.current { color: var(--ink); font-weight: 600; }
    .choices { display: flex; flex-wrap: wrap; gap: var(--space-2); }
    .choice { padding: var(--space-2) var(--space-3); border: 1px solid var(--line-strong); border-radius: var(--radius-pill); background: var(--surface); cursor: pointer; }
    .choice:hover { background: var(--hover); }
    p { margin: 0; }
  `,
})
export class ActivityPicker {
  readonly catalogue = input<Activity[]>([]);
  readonly value = model<string>('GENERAL');
  protected readonly path = computed(() => pathOf(this.catalogue(), this.value()));
  protected readonly children = computed(() => childrenOf(this.catalogue(), this.value()));

  protected name(a: Activity): string {
    return a.name['en'] ?? a.code;
  }
}

/** The pack groups to load, as ticks; ticking one brings what it needs, unticking takes along what needs it. */
@Component({
  selector: 'app-groups-picker',
  imports: [TranslatePipe, Checkbox],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (g of groups(); track g.name) {
      <div class="group">
        <ui-checkbox [checked]="value().includes(g.name)" (checkedChange)="value.set(toggle(g.name, $event))">{{ label(g.name) }}</ui-checkbox>
        @if (g.requires.length) { <span class="muted">{{ 'needs {0}' | t: (needs(g)) }}</span> }
      </div>
    }
  `,
  styles: `:host { display: grid; gap: var(--space-2); } .group { display: flex; align-items: center; gap: var(--space-3); } .muted { font-size: var(--text-s); }`,
})
export class GroupsPicker {
  readonly groups = input<PackGroup[]>([]);
  readonly value = model<string[]>([]);

  protected toggle(name: string, on: boolean): string[] {
    return toggleGroup(this.groups(), this.value(), name, on);
  }

  protected label(name: string): string {
    return GROUP_NAMES[name] ?? name;
  }

  protected needs(group: PackGroup): string {
    return group.requires.map(r => this.label(r).toLowerCase()).join(', ');
  }
}
