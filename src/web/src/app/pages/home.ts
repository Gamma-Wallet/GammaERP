import { TranslatePipe } from '../core/t.pipe';
import { t } from '../core/i18n';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../core/entities';
import { routeOf } from '../core/menu';
import { SessionService } from '../core/session';
import { text } from '../core/text';
import { Icon } from '../ui/icon';
import { Select, SelectOption } from '../ui/select';
import { Toasts } from '../ui/toast';
import { COUNTRY_NAMES, SetupApi, SetupCountry } from './setup/setup-parts';

/** The first page: one card per top folder of the user's menu, with its items as links. */
@Component({
  selector: 'app-home',
  imports: [TranslatePipe, RouterLink, Icon, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h1>{{ text.home.title }}</h1>
      <p>{{ text.home.subtitle }}</p>
    </div>
    @if (groupBooks()) {
      <section class="finish">
        <div>
          <h2>{{ 'Set up the group\\'s books' | t }}</h2>
          <p>{{ 'The group works as a company of its own: its chart of accounts, document types, units and VAT, from a country\\'s starter pack. What passes between the group and its companies goes by documents. This is done once.' | t }}</p>
          @if (groupMessage()) { <p class="error" role="alert">{{ groupMessage() }}</p> }
        </div>
        <ui-select class="country" [options]="countryOptions()" [searchable]="true" [value]="groupCountry()" [placeholder]="'Country' | t" [ariaLabel]="'Country' | t"
          (valueChange)="groupCountry.set($event)" />
        <button type="button" class="button primary" [disabled]="!groupCountry() || settingUp()" (click)="setUpGroup()">
          {{ settingUp() ? ('Setting up…' | t) : ('Set up the group\\'s books' | t) }}</button>
      </section>
    }
    @if (session.me()?.isAdministrator && !finished()) {
      <section class="finish">
        <div>
          <h2>{{ 'Finish your company' | t }}</h2>
          <p>{{ 'Add its tax number, trade name, address, phone and logo: they print on every document.' | t }}</p>
        </div>
        <a class="button primary" routerLink="/entities/cost-centers">{{ 'Open the company' | t }}</a>
        <button type="button" class="button quiet" (click)="dismiss()">{{ 'Later' | t }}</button>
      </section>
    }
    <div class="cards">
      @for (folder of session.menu(); track folder.id) {
        @if (!folder.isElement) {
          <section class="card">
            <h2><ui-icon [name]="folder.icon" [size]="18" />{{ folder.name }}</h2>
            <ul>
              @for (node of folder.children; track node.id) {
                @if (node.isElement) {
                  <li><a [routerLink]="routeOf(node)">{{ node.name }}</a></li>
                } @else {
                  @for (child of node.children; track child.id) {
                    <li><a [routerLink]="routeOf(child)">{{ child.name }}</a></li>
                  }
                }
              }
            </ul>
          </section>
        }
      }
    </div>
  `,
  styles: `
    :host { display: block; padding: var(--space-5) var(--space-6); }
    .head { margin-bottom: var(--space-5); }
    h1 { font-size: var(--title-l); }
    .head p { margin: var(--space-2) 0 0; color: var(--muted); }
    .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: var(--space-4); }
    .card { padding: var(--space-4) var(--space-5); border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
    h2 { display: flex; align-items: center; gap: var(--space-2); margin-bottom: var(--space-3); font-size: var(--title-s); }
    ul { display: grid; gap: var(--space-1); margin: 0; padding: 0; list-style: none; }
    .card a { color: var(--ink-2); }
    .card a:hover { color: var(--link); }
    .finish { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-3); margin-bottom: var(--space-4); padding: var(--space-4) var(--space-5);
      border: 1px solid var(--line); border-left: 4px solid var(--brand); border-radius: var(--radius-l); background: var(--surface); }
    .finish div { flex: 1; min-width: 260px; } .finish p { margin: var(--space-1) 0 0; color: var(--muted); }
    .finish .error { color: var(--critical); } .country { width: 220px; }
  `,
})
export class Home {
  protected readonly text = text;
  protected readonly session = inject(SessionService);
  protected readonly routeOf = routeOf;
  /** The "finish your company" card, put away per user on this browser. */
  protected readonly finished = signal(readFinished(this.session.me()?.id));
  private readonly http = inject(HttpClient);
  private readonly setup = inject(SetupApi);
  private readonly toasts = inject(Toasts);
  /** At a group with no books of its own yet (the founder, 2026-10-31: a group behaves as a company): the card that sets them up once. */
  protected readonly groupBooks = signal(false);
  protected readonly countries = signal<SetupCountry[]>([]);
  protected readonly countryOptions = computed<SelectOption[]>(() => this.countries().map(c => ({ value: c.code, label: COUNTRY_NAMES[c.code] ?? c.code }))
    .sort((a, b) => a.label.localeCompare(b.label)));
  protected readonly groupCountry = signal<string | null>(null);
  protected readonly groupMessage = signal<string | null>(null);
  protected readonly settingUp = signal(false);

  constructor() {
    if (this.session.me()?.isAdministrator) {
      void this.checkGroupBooks();
    }
  }

  private async checkGroupBooks(): Promise<void> {
    const state = await firstValueFrom(this.http.get<{ needed: boolean }>('/api/v1/group-books')).catch(() => null);
    if (state?.needed) {
      this.countries.set(await this.setup.countries().catch(() => []));
      this.groupBooks.set(true);
    }
  }

  protected async setUpGroup(): Promise<void> {
    this.settingUp.set(true);
    this.groupMessage.set(null);
    try {
      const made = await firstValueFrom(this.http.post<{ rows: number }>('/api/v1/group-books', { country: this.groupCountry() }));
      this.toasts.good(t('The group\'s books are set up ({0} configuration rows).', made.rows));
      this.groupBooks.set(false);
      await this.session.loadMenu();
    } catch (error) {
      this.groupMessage.set(errorsOf(error).message);
    } finally {
      this.settingUp.set(false);
    }
  }

  protected dismiss(): void {
    this.finished.set(true);
    try {
      localStorage.setItem(`gamma-erp.finished.${this.session.me()?.id}`, '1');
    } catch {
      // Without storage the card comes back next time.
    }
  }
}

function readFinished(userId: string | undefined): boolean {
  try {
    return !!userId && localStorage.getItem(`gamma-erp.finished.${userId}`) === '1';
  } catch {
    return false;
  }
}
