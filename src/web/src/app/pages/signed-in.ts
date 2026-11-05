import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth';
import { text } from '../core/text';
import { Plain } from './plain';

/** Where the API's sign-in page (and sign-out) sends the browser back: with a code it completes the sign-in, without one it says so. */
@Component({
  selector: 'app-signed-in',
  imports: [Plain],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-plain>
      @switch (state()) {
        @case ('working') { <p class="muted">{{ text.signIn.signingIn }}</p> }
        @default {
          <h1>{{ state() === 'failed' ? text.signIn.failed : text.signIn.signedOut }}</h1>
          <button class="button primary" type="button" (click)="signIn()">{{ text.signIn.signInAgain }}</button>
        }
      }
    </app-plain>
  `,
  styles: `h1 { font-size: var(--title-m); margin-bottom: var(--space-4); } .muted { color: var(--muted); }`,
})
export class SignedIn implements OnInit {
  protected readonly text = text;
  protected readonly state = signal<'working' | 'out' | 'failed'>('working');
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  async ngOnInit(): Promise<void> {
    const params = new URLSearchParams(location.search);
    if (!params.has('code')) {
      this.state.set('out');
      return;
    }
    try {
      const back = await this.auth.completeSignIn(params);
      await this.router.navigateByUrl(back, { replaceUrl: true });
    } catch {
      this.state.set('failed');
    }
  }

  protected signIn(): void {
    void this.auth.signIn('/');
  }
}
