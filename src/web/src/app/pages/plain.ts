import { ChangeDetectionStrategy, Component } from '@angular/core';
import { AppFooter } from '../shell/app-footer';

/** The frame of the pages outside the shell (signing in, choosing a cost centre): the top bar and a card in the middle. */
@Component({
  selector: 'app-plain',
  imports: [AppFooter],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header><img class="mark" src="gamma-logo-white.png" alt="Gamma" /></header>
    <main><div class="card"><ng-content /></div></main>
    <app-footer />
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 100vh; }
    header { display: flex; align-items: center; gap: var(--space-3); height: var(--topbar-h); padding: 0 var(--space-5);
      background: var(--topbar); color: var(--topbar-ink); font-weight: 600; }
    .mark { display: block; height: 26px; width: auto; }
    main { flex: 1; display: grid; place-items: start center; padding: 12vh var(--space-4) var(--space-6); }
    .card { width: min(520px, 100%); padding: var(--space-6); border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
  `,
})
export class Plain {
}
