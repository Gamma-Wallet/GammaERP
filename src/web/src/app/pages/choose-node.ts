import { TranslatePipe } from '../core/t.pipe';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MeCostCenter, SessionService } from '../core/session';
import { text } from '../core/text';
import { Icon } from '../ui/icon';
import { Plain } from './plain';

/** The places in tree order with their depth: a place under the one above it, a place with none above it at the top. */
export function placeRows(places: readonly MeCostCenter[]): { place: MeCostCenter; depth: number }[] {
  const ids = new Set(places.map(p => p.id));
  const rows: { place: MeCostCenter; depth: number }[] = [];
  const walk = (place: MeCostCenter, depth: number): void => {
    rows.push({ place, depth });
    places.filter(p => p.parentId === place.id && p.id !== place.id).forEach(child => walk(child, depth + 1));
  };
  places.filter(p => !p.parentId || !ids.has(p.parentId)).forEach(top => walk(top, 0));
  return rows;
}

/**
 * Choosing the cost centre to work at (the API's X-Gamma-Node): the places linked to the user and every workplace below them (the
 * founder, 2026-10-31: the group and its companies, not the cost centres inside a company), as a tree.
 */
@Component({
  selector: 'app-choose-node',
  imports: [TranslatePipe, Plain, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-plain>
      <h1>{{ text.choose.title }}</h1>
      @if (rows().length) {
        <p class="sub">{{ text.choose.subtitle }}</p>
        <ul>
          @for (row of rows(); track row.place.id) {
            <li>
              <button type="button" [class.current]="row.place.id === session.node()" [style.padding-left.px]="16 + row.depth * 24" (click)="choose(row.place)">
                <ui-icon [name]="!row.place.parentId || !row.place.companyId || row.place.companyId === row.place.id ? 'building' : 'map-pin'" />
                <span>{{ row.place.name }}</span>
                <small>{{ row.place.code }}</small>
                <ui-icon name="chevron-right" [size]="14" />
              </button>
            </li>
          }
        </ul>
      } @else {
        <p class="sub">{{ text.choose.none }}</p>
      }
      @if (session.me()?.isAdministrator) {
        <div class="new">
          <a class="button" routerLink="/new-company"><ui-icon name="plus" />{{ 'New company' | t }}</a>
          <span class="muted">{{ 'Set up another company: its country, activity and starting configuration.' | t }}</span>
        </div>
      }
    </app-plain>
  `,
  styles: `
    h1 { font-size: var(--title-l); }
    .sub { margin: var(--space-2) 0 var(--space-5); color: var(--muted); }
    ul { display: grid; margin: 0; padding: 0; border: 1px solid var(--line); border-radius: var(--radius); list-style: none; overflow: hidden; }
    li + li { border-top: 1px solid var(--line); }
    button { display: flex; width: 100%; align-items: center; gap: var(--space-3); padding: var(--space-3) var(--space-4); border: 0;
      background: var(--surface); text-align: left; cursor: pointer; }
    button:hover { background: var(--hover); }
    button.current { background: var(--selected); font-weight: 500; }
    span { flex: 1; }
    small { color: var(--muted); }
    .new { display: grid; gap: var(--space-2); margin-top: var(--space-5); padding-top: var(--space-4); border-top: 1px solid var(--line); }
    .new .button { justify-self: start; }
    .muted { color: var(--muted); font-size: var(--text-s); }
  `,
})
export class ChooseNode {
  protected readonly text = text;
  protected readonly session = inject(SessionService);
  private readonly router = inject(Router);

  /** Every place the user may work at, as a tree: each under the place above it (the group, its companies, their cash desks…). */
  protected readonly rows = computed(() => placeRows(this.session.me()?.costCenters ?? []));

  protected async choose(place: MeCostCenter): Promise<void> {
    await this.session.choose(place.id);
    await this.router.navigateByUrl('/');
  }
}
