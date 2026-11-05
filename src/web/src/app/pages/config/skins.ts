import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { Skin, SkinApi, SkinFile, SkinKind } from '../../core/skins';
import { TranslatePipe } from '../../core/t.pipe';
import { Button } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { Toasts } from '../../ui/toast';

/** The file name a skin downloads as: its name in lowercase with hyphens, and its kind. */
export function fileNameOf(skin: Pick<Skin, 'name' | 'kind'>): string {
  const name = (skin.name ?? 'skin').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'skin';
  return `${name}-${skin.kind}.json`;
}

/** Reads an uploaded skin file; null with the reason when it is not one. */
export function skinFileOf(text: string): SkinFile | string {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return t('The file is not JSON.');
  }
  const file = parsed as Partial<SkinFile>;
  if (!file || typeof file !== 'object' || !file.tokens || typeof file.tokens !== 'object') {
    return t('A skin file has a name, a kind (erp or pos), dark (true or false) and its tokens.');
  }
  return { name: file.name ?? null, kind: file.kind === 'pos' ? 'pos' : 'erp', dark: file.dark === true, tokens: file.tokens as Record<string, string> };
}

/**
 * System settings › Skins (the founder, 2026-11-04): the company's skins for the ERP and for the point of sale. An administrator uploads a skin file,
 * previews it, puts it in use, downloads one to change it, or goes back to the built-in look. A skin changes colours, fonts, corners and density only.
 */
@Component({
  selector: 'app-skins',
  imports: [TranslatePipe, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h1>{{ 'Skins' | t }}</h1>
      <input #picker class="picker" type="file" accept="application/json,.json" [attr.aria-label]="'Skin file' | t" (change)="upload($any($event.target))" />
      <button uiButton variant="primary" [disabled]="busy()" (click)="picker.click()"><ui-icon name="upload" [size]="16" />{{ 'Upload a skin' | t }}</button>
    </div>
    <p class="muted intro">{{ intro }}</p>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (warnings().length) {
      <div class="warn" role="status"><ui-icon name="warning" [size]="16" />
        <div><strong>{{ 'The skin was added. Its contrast check found:' | t }}</strong><ul>@for (w of warnings(); track w) { <li>{{ w }}</li> }</ul></div>
      </div>
    }
    @if (previewing(); as name) {
      <p class="preview" role="status"><ui-icon name="eye" [size]="16" />{{ 'Previewing {0}. Nothing is saved until you choose Use.' | t: name }}
        <button uiButton variant="quiet" size="small" (click)="endPreview()">{{ 'End the preview' | t }}</button></p>
    }
    @for (group of groups(); track group.kind) {
      <section>
        <div class="group-head">
          <h2>{{ group.title }}</h2>
          <button uiButton variant="quiet" size="small" [disabled]="busy() || !group.skins.some(s => s.inUse)" (click)="builtIn(group.kind)">{{ 'Use the built-in look' | t }}</button>
        </div>
        <table [attr.aria-label]="group.title">
          <thead><tr><th>{{ 'Name' | t }}</th><th>{{ 'Look' | t }}</th><th class="num">{{ 'Tokens' | t }}</th><th>{{ 'State' | t }}</th><th></th></tr></thead>
          <tbody>
            @for (s of group.skins; track s.id) {
              <tr>
                <td>{{ s.name }}</td>
                <td>{{ (s.dark ? 'Dark' : 'Light') | t }}</td>
                <td class="num figures">{{ s.tokens }}</td>
                <td>@if (s.inUse) { <span class="pill"><ui-icon name="check" [size]="14" />{{ 'In use' | t }}</span> }</td>
                <td class="actions">
                  @if (s.kind === 'erp') { <button uiButton variant="quiet" size="small" [disabled]="busy()" (click)="preview(s)">{{ 'Preview' | t }}</button> }
                  <button uiButton variant="quiet" size="small" [disabled]="busy()" (click)="download(s)">{{ 'Download' | t }}</button>
                  @if (!s.inUse) {
                    <button uiButton size="small" [disabled]="busy()" (click)="use(s)">{{ 'Use' | t }}</button>
                    <button uiButton variant="quiet" size="small" [disabled]="busy()" (click)="remove(s)">{{ 'Switch off' | t }}</button>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="muted">{{ 'No skins yet: the built-in look is in use.' | t }}</td></tr>
            }
          </tbody>
        </table>
        @if (!group.skins.some(s => s.inUse)) { <p class="muted">{{ 'The built-in look is in use.' | t }}</p> }
      </section>
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-4); padding: var(--space-5) var(--space-6); align-content: start; }
    .head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); flex-wrap: wrap; }
    h1 { margin: 0; font-family: var(--font-title); font-size: var(--title-l); font-weight: 500; }
    h2 { margin: 0; font-family: var(--font-title); font-size: var(--title-s); font-weight: 500; }
    .intro { margin: 0; max-width: 80ch; }
    .muted { color: var(--muted); }
    .picker { display: none; }
    section { display: grid; gap: var(--space-2); }
    .group-head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); }
    table { width: 100%; border-collapse: collapse; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); font-size: var(--text-s); }
    th, td { padding: 6px var(--space-3); border-bottom: 1px solid var(--line); text-align: left; }
    th { color: var(--muted); font-size: var(--text-xs); font-weight: 600; background: var(--surface-2); }
    .num { text-align: right; } .actions { text-align: right; white-space: nowrap; }
    .pill { display: inline-flex; align-items: center; gap: 4px; padding: 1px 8px; border-radius: var(--radius-pill); background: var(--good-bg); color: var(--good); font-weight: 600; }
    .message, .warn, .preview { display: flex; align-items: flex-start; gap: var(--space-2); padding: var(--space-2) var(--space-3); border-radius: var(--radius); margin: 0; }
    .message { background: var(--critical-bg); color: var(--critical); }
    .warn { background: var(--warning-bg); color: var(--ink); } .warn ul { margin: 4px 0 0; padding-left: 1.2em; }
    .preview { background: var(--info-bg); color: var(--ink); align-items: center; }
  `,
})
export class Skins implements OnDestroy {
  private readonly api = inject(SkinApi);
  private readonly toasts = inject(Toasts);

  protected readonly intro = t("A skin sets the screens' colours, fonts, corners and density; it never changes the layout. One skin is in use for the ERP and one for the point of sale. Download a skin to start a new one, change its values and upload it.");
  protected readonly skins = signal<Skin[]>([]);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly warnings = signal<string[]>([]);
  protected readonly previewing = signal<string | null>(null);
  protected readonly groups = computed(() => (['erp', 'pos'] as SkinKind[]).map(kind => ({
    kind, title: kind === 'erp' ? t('ERP') : t('Point of sale'), skins: this.skins().filter(s => s.kind === kind),
  })));

  constructor() {
    void this.load();
  }

  ngOnDestroy(): void {
    if (this.previewing()) {
      void this.api.applyInUse('erp');
    }
  }

  private async load(): Promise<void> {
    await this.act(async () => this.skins.set(await this.api.list()));
  }

  protected async upload(input: HTMLInputElement): Promise<void> {
    const chosen = input.files?.[0];
    input.value = '';
    if (!chosen) {
      return;
    }
    const file = skinFileOf(await chosen.text());
    if (typeof file === 'string') {
      this.message.set(file);
      return;
    }
    await this.act(async () => {
      const made = await this.api.upload(file);
      this.warnings.set(made.warnings);
      this.toasts.good(t('{0} was added.', made.skin.name ?? ''));
      this.skins.set(await this.api.list());
    });
  }

  protected async preview(skin: Skin): Promise<void> {
    await this.act(async () => {
      const file = await this.api.file(skin.id);
      this.api.apply(document.documentElement, file.tokens, file.dark);
      this.previewing.set(skin.name ?? '');
    });
  }

  protected async endPreview(): Promise<void> {
    this.previewing.set(null);
    await this.api.applyInUse('erp');
  }

  protected async use(skin: Skin): Promise<void> {
    await this.act(async () => {
      await this.api.use(skin.id);
      this.previewing.set(null);
      if (skin.kind === 'erp') {
        await this.api.applyInUse('erp');
      }
      this.toasts.good(t('{0} is in use.', skin.name ?? ''));
      this.skins.set(await this.api.list());
    });
  }

  protected async builtIn(kind: SkinKind): Promise<void> {
    await this.act(async () => {
      await this.api.useBuiltIn(kind);
      if (kind === 'erp') {
        this.previewing.set(null);
        await this.api.applyInUse('erp');
      }
      this.toasts.good(t('The built-in look is in use.'));
      this.skins.set(await this.api.list());
    });
  }

  protected async download(skin: Skin): Promise<void> {
    await this.act(async () => {
      const file = await this.api.file(skin.id);
      const link = document.createElement('a');
      link.href = URL.createObjectURL(new Blob([JSON.stringify(file, null, 2)], { type: 'application/json' }));
      link.download = fileNameOf(skin);
      link.click();
      setTimeout(() => URL.revokeObjectURL(link.href), 1000);
    });
  }

  protected async remove(skin: Skin): Promise<void> {
    await this.act(async () => {
      await this.api.remove(skin.id);
      this.toasts.good(t('{0} was switched off.', skin.name ?? ''));
      this.skins.set(await this.api.list());
    });
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await work();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
