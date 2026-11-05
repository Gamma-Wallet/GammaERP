import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { itemAt } from '../core/menu';
import { SessionService } from '../core/session';
import { text } from '../core/text';

/** The C3 phase that builds each kind of screen, shown until it is built. */
const PHASE: Record<string, string> = {
  entities: 'phase 3 (registers)', documents: 'phase 4 (documents)', config: 'phase 5 (configuration)', rights: 'phase 5 (configuration)',
  'document-types': 'phase 5 (configuration)', reports: 'phase 6 (reports)', production: 'phase 7 (operations)',
  'stock-convert': 'phase 7 (operations)', depreciation: 'phase 7 (operations)', currencies: 'phase 7 (operations)', files: 'phase 7 (operations)',
  pos: 'phase 9 (point of sale)',
};

/** A screen not built yet: its title from the menu, and when it comes. */
@Component({
  selector: 'app-screen-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ title() }}</h1></div>
    <section class="card">
      <p>{{ text.screen.notBuilt }}</p>
      @if (phase(); as phase) { <p class="muted">{{ text.screen.comesIn }} {{ phase }}.</p> }
    </section>
  `,
  styles: `
    :host { display: block; padding: var(--space-5) var(--space-6); }
    .head { margin-bottom: var(--space-5); }
    h1 { font-size: var(--title-l); }
    .card { padding: var(--space-5); border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
    p { margin: 0; } .muted { margin-top: var(--space-2); color: var(--muted); }
  `,
})
export class ScreenPage {
  protected readonly text = text;
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)), {
    initialValue: this.router.url,
  });
  protected readonly title = computed(() => itemAt(this.session.menu(), this.url())?.name ?? '');
  protected readonly phase = computed(() => PHASE[this.url().split('/')[1] ?? ''] ?? null);
}
