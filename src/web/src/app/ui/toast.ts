import { ChangeDetectionStrategy, Component, Injectable, inject, signal } from '@angular/core';
import { text } from '../core/text';
import { Icon } from './icon';

export type ToastKind = 'good' | 'info' | 'warning' | 'critical';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

/** How long a toast stays; an error stays until closed, so it is never missed. */
const STAYS_MS: Record<ToastKind, number | null> = { good: 4000, info: 5000, warning: 8000, critical: null };

/** Short messages after an action: "Invoice 42 posted." Shown bottom right by ui-toasts (in the app root). */
@Injectable({ providedIn: 'root' })
export class Toasts {
  private nextId = 0;
  readonly shown = signal<Toast[]>([]);

  show(kind: ToastKind, message: string): number {
    const toast = { id: ++this.nextId, kind, message };
    // At most 4 at a time: the oldest goes first.
    this.shown.update(list => [...list, toast].slice(-4));
    const stays = STAYS_MS[kind];
    if (stays !== null) {
      setTimeout(() => this.close(toast.id), stays);
    }
    return toast.id;
  }

  good(message: string): number {
    return this.show('good', message);
  }

  info(message: string): number {
    return this.show('info', message);
  }

  warning(message: string): number {
    return this.show('warning', message);
  }

  critical(message: string): number {
    return this.show('critical', message);
  }

  close(id: number): void {
    this.shown.update(list => list.filter(t => t.id !== id));
  }
}

@Component({
  selector: 'ui-toasts',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="polite" aria-live="polite">
      @for (toast of toasts.shown(); track toast.id) {
        @if (toast.kind !== 'critical') {
          <div class="toast" [class]="toast.kind">
            <ui-icon [name]="toast.kind" [label]="text.ui.status[toast.kind]" />
            <p>{{ toast.message }}</p>
            <button type="button" [attr.aria-label]="text.ui.close" (click)="toasts.close(toast.id)"><ui-icon name="close" [size]="14" /></button>
          </div>
        }
      }
    </div>
    <div class="urgent" role="alert">
      @for (toast of toasts.shown(); track toast.id) {
        @if (toast.kind === 'critical') {
          <div class="toast critical">
            <ui-icon name="critical" [label]="text.ui.status.critical" />
            <p>{{ toast.message }}</p>
            <button type="button" [attr.aria-label]="text.ui.close" (click)="toasts.close(toast.id)"><ui-icon name="close" [size]="14" /></button>
          </div>
        }
      }
    </div>
  `,
  styles: `
    :host { position: fixed; right: var(--space-5); bottom: var(--space-5); z-index: 1100; display: grid; gap: var(--space-2);
      width: min(380px, calc(100vw - 32px)); pointer-events: none; }
    .polite, .urgent { display: grid; gap: var(--space-2); }
    .toast { display: flex; align-items: flex-start; gap: var(--space-3); padding: var(--space-3) var(--space-3) var(--space-3) var(--space-4);
      border-left: 4px solid var(--muted); border-radius: var(--radius); background: var(--topbar); color: var(--topbar-ink);
      box-shadow: var(--shadow-pop); pointer-events: auto; }
    .toast ui-icon { margin-top: 2px; }
    .good { border-left-color: var(--good-on-dark); }
    .info { border-left-color: var(--brand); }
    .warning { border-left-color: var(--warning-on-dark); }
    .critical { border-left-color: var(--critical-on-dark); }
    p { flex: 1; margin: 0; }
    button { display: grid; place-items: center; width: 24px; height: 24px; border: 0; border-radius: var(--radius-xs); background: none;
      color: var(--topbar-muted); cursor: pointer; }
    button:hover { background: var(--topbar-hover); color: var(--topbar-ink); }
  `,
})
export class ToastOutlet {
  protected readonly text = text;
  protected readonly toasts = inject(Toasts);
}
