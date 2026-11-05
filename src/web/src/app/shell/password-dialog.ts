import { TranslatePipe } from '../core/t.pipe';
import { t } from '../core/i18n';
import { DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ConfigApi } from '../core/config';
import { errorsOf } from '../core/entities';
import { Button, Field, Input } from '../ui/controls';
import { DialogFrame } from '../ui/dialog';
import { Toasts } from '../ui/toast';

/** The signed-in user's own new password, after the current one (only its hash is kept). */
@Component({
  selector: 'app-password-dialog',
  imports: [TranslatePipe, Button, DialogFrame, Field, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="'Change password' | t" width="420px">
      <div class="grid">
        <ui-field [label]="'Current password' | t" [error]="errors()['current'] ?? null">
          <input uiInput type="password" autocomplete="current-password" (input)="current.set($any($event.target).value)" />
        </ui-field>
        <ui-field [label]="'New password' | t" [hint]="'At least 12 characters' | t" [error]="errors()['new'] ?? null">
          <input uiInput type="password" autocomplete="new-password" (input)="next.set($any($event.target).value)" />
        </ui-field>
        <ui-field [label]="'New password again' | t" [error]="mismatch() ? 'The two new passwords differ.' : null">
          <input uiInput type="password" autocomplete="new-password" (input)="again.set($any($event.target).value)" />
        </ui-field>
      </div>
      <ng-container dialog-footer>
        <button uiButton variant="quiet" (click)="ref.close()">{{ 'Cancel' | t }}</button>
        <button uiButton variant="primary" [disabled]="!current() || !next() || mismatch() || saving()" (click)="save()">{{ 'Change' | t }}</button>
      </ng-container>
    </ui-dialog>
  `,
  styles: `.grid { display: grid; gap: var(--space-4); }`,
})
export class PasswordDialog {
  protected readonly ref = inject(DialogRef);
  private readonly api = inject(ConfigApi);
  private readonly toasts = inject(Toasts);
  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly again = signal('');
  protected readonly saving = signal(false);
  protected readonly errors = signal<Record<string, string | undefined>>({});
  protected readonly mismatch = computed(() => this.again() !== '' && this.again() !== this.next());

  protected async save(): Promise<void> {
    this.saving.set(true);
    try {
      await this.api.changePassword(this.current(), this.next());
      this.toasts.good(t('Your password was changed.'));
      this.ref.close();
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.set(Object.keys(fields).length ? fields : { new: message });
    } finally {
      this.saving.set(false);
    }
  }
}
