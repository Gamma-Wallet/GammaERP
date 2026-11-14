import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BankConnection, BankConnectionsApi, ibanShown } from '../../core/banking';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button } from '../../ui/controls';
import { formatDate } from '../../ui/format';
import { Icon } from '../../ui/icon';

/**
 * The return address (the founder, 2026-11-14): the bank sends the person back here after they sign in, with a state and a code. The page finishes the
 * connection; with several accounts the person chooses one on Bank statements.
 */
@Component({
  selector: 'app-bank-connected',
  imports: [TranslatePipe, Button, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Bank connection' | t }}</h1></div>
    @if (busy()) { <p class="muted">{{ 'Finishing the connection…' | t }}</p> }
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (done(); as c) {
      @if (c.connected) {
        <p>{{ '{0} is connected to its bank ({1}) until {2}.' | t: (c.bank ?? '') : (c.aspspName ?? '') : day(c.validUntil) }}</p>
        <p class="muted">{{ iban(c.iban) }} · {{ c.currency }} · {{ c.holderName }}</p>
      } @else {
        <p>{{ 'The bank gave several accounts: choose the one that is {0} on Bank statements.' | t: (c.bank ?? '') }}</p>
      }
    }
    <a uiButton variant="primary" routerLink="/bank-statements">{{ 'Back to bank statements' | t }}</a>
  `,
  styleUrl: './operations.scss',
})
export class BankConnected {
  private readonly api = inject(BankConnectionsApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly done = signal<BankConnection | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(true);
  protected readonly iban = ibanShown;

  constructor() {
    void this.finish();
  }

  protected day(value: string | null): string {
    return value ? formatDate(value) : '';
  }

  private async finish(): Promise<void> {
    const query = this.route.snapshot.queryParamMap;
    const [state, code, error] = [query.get('state'), query.get('code'), query.get('error_description') ?? query.get('error')];
    try {
      if (!state || !code) {
        this.message.set(error ? t('The bank did not connect the account: {0}', error) : t('The bank sent no code back: the sign-in was cancelled. Start it again on Bank statements.'));
        return;
      }
      this.done.set(await this.api.finish(state, code));
    } catch (e) {
      this.message.set(errorsOf(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
