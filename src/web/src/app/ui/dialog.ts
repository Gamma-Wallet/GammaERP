import { DIALOG_DATA, Dialog, DialogRef, type DialogConfig } from '@angular/cdk/dialog';
import type { ComponentType } from '@angular/cdk/portal';
import { ChangeDetectionStrategy, Component, Injectable, inject, input } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { text } from '../core/text';
import { Button, uniqueId } from './controls';
import { Icon } from './icon';

/**
 * The frame of every dialog: a title, a close button, the body (projected) and the buttons (projected with the dialog-footer attribute).
 * The CDK dialog around it traps the focus, closes on Escape and gives the focus back to what opened it.
 */
@Component({
  selector: 'ui-dialog',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'document', '[style.width]': 'width()' },
  template: `
    <header>
      <h2 [id]="titleId">{{ title() }}</h2>
      <button type="button" class="close" [attr.aria-label]="text.ui.close" (click)="ref?.close()"><ui-icon name="close" /></button>
    </header>
    <div class="body"><ng-content /></div>
    <footer><ng-content select="[dialog-footer]" /></footer>
  `,
  styles: `
    :host { display: flex; flex-direction: column; max-width: 100%; max-height: calc(100vh - 32px); border-radius: var(--radius-l);
      background: var(--surface); box-shadow: var(--shadow-pop); }
    header { display: flex; align-items: center; gap: var(--space-3); padding: var(--space-4) var(--space-5); border-bottom: 1px solid var(--line); }
    h2 { flex: 1; font-size: var(--title-s); }
    .close { display: grid; place-items: center; width: 32px; height: 32px; border: 0; border-radius: var(--radius-s); background: none;
      color: var(--muted); cursor: pointer; }
    .close:hover { background: var(--hover); color: var(--ink); }
    .body { flex: 1; min-height: 0; padding: var(--space-5); overflow: auto; }
    footer { display: flex; justify-content: flex-end; gap: var(--space-2); padding: var(--space-3) var(--space-5);
      border-top: 1px solid var(--line); }
    footer:empty { display: none; }
  `,
})
export class DialogFrame {
  protected readonly text = text;
  protected readonly ref = inject(DialogRef, { optional: true });
  readonly title = input.required<string>();
  readonly width = input('480px');
  /** The id the dialog was opened with as its label (see Dialogs.open), so screen readers name the dialog by its title. */
  readonly titleId = this.ref?.config.ariaLabelledBy ?? uniqueId('dialog-title');
}

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  /** A destructive action (delete, reverse): the button is red. */
  danger?: boolean;
}

@Component({
  selector: 'ui-confirm',
  imports: [DialogFrame, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="data.title" width="420px">
      <p>{{ data.message }}</p>
      <ng-container dialog-footer>
        <button uiButton (click)="ref.close(false)">{{ text.ui.cancel }}</button>
        <button uiButton [variant]="data.danger ? 'danger' : 'primary'" (click)="ref.close(true)">
          {{ data.confirmLabel ?? text.ui.confirm }}
        </button>
      </ng-container>
    </ui-dialog>
  `,
  styles: `p { margin: 0; color: var(--ink-2); }`,
})
export class ConfirmDialog {
  protected readonly text = text;
  protected readonly data = inject<ConfirmOptions>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
}

/** Opens dialogs: a screen's own component (wrapped in ui-dialog), or a yes / no question. */
@Injectable({ providedIn: 'root' })
export class Dialogs {
  private readonly dialog = inject(Dialog);

  open<R = unknown, D = unknown, C = unknown>(component: ComponentType<C>, data?: D, config: DialogConfig<D, DialogRef<R, C>> = {}): DialogRef<R, C> {
    return this.dialog.open<R, D, C>(component, {
      data,
      autoFocus: 'first-tabbable',
      restoreFocus: true,
      backdropClass: 'dialog-backdrop',
      panelClass: 'dialog-panel',
      ariaLabelledBy: uniqueId('dialog-title'),
      ...config,
    });
  }

  /** True when the person confirmed; false when they cancelled, pressed Escape or clicked outside. */
  async confirm(options: ConfirmOptions): Promise<boolean> {
    const ref = this.open<boolean, ConfirmOptions, ConfirmDialog>(ConfirmDialog, options, { role: 'alertdialog' });
    return (await firstValueFrom(ref.closed)) === true;
  }
}
