import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '../core/t.pipe';

/** The foot of every page: who it is powered by, and where to find out more. */
@Component({
  selector: 'app-footer',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span>{{ 'Powered by' | t }} <a href="https://www.gamma-wallet.com/en" target="_blank" rel="noopener">Gamma Wallet</a></span>
    <span>{{ 'A cardless digital wallet for reward contracts' | t }}</span>
    <a href="https://blog.gamma-wallet.com" target="_blank" rel="noopener">{{ 'Help' | t }}</a>
    <span>© {{ year }} Gamma Wallet Ltd · {{ 'England and Wales, no. {0}' | t : '17168282' }}</span>
  `,
  styles: `
    :host { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--space-1) var(--space-4); padding: var(--space-3) var(--space-4); border-top: 1px solid var(--line);
      color: var(--muted); font-size: var(--text-xs); }
    a { color: inherit; text-decoration: underline; }
  `,
})
export class AppFooter {
  protected readonly year = new Date().getFullYear();
}
