import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { companyLanguage } from '../core/i18n';

/** The languages a text can be written in (the EU's and the region's), by code. */
export const LANGUAGES: Record<string, string | undefined> = {
  en: 'English', sq: 'Albanian', bg: 'Bulgarian', hr: 'Croatian', cs: 'Czech', da: 'Danish', nl: 'Dutch', et: 'Estonian', fi: 'Finnish',
  fr: 'French', de: 'German', el: 'Greek', hu: 'Hungarian', ga: 'Irish', it: 'Italian', lv: 'Latvian', lt: 'Lithuanian', mk: 'Macedonian',
  mt: 'Maltese', pl: 'Polish', pt: 'Portuguese', ro: 'Romanian', sr: 'Serbian', sk: 'Slovak', sl: 'Slovenian', es: 'Spanish', sv: 'Swedish',
  tr: 'Turkish',
};

/** The languages a text is written in: English first, then the company's own (the founder, 2026-11-02). */
export function languagesOf(): string[] {
  return [...new Set(['en', companyLanguage()])];
}

/** A text in the company's two languages ({"en": "…", "sq": "…"}): one row each, English first. One left empty takes the other's text when saved. */
@Component({
  selector: 'ui-translated-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="rows">
      @for (language of languages; track language) {
        <label class="row">
          <span class="code" [title]="names[language] ?? language">{{ language }}</span>
          @if (multiline()) {
            <textarea class="control" [value]="value()?.[language] ?? ''" [disabled]="disabled()" [attr.aria-label]="label() + ' (' + (names[language] ?? language) + ')'"
              (input)="set(language, $any($event.target).value)"></textarea>
          } @else {
            <input class="control" [value]="value()?.[language] ?? ''" [disabled]="disabled()" [attr.aria-invalid]="invalid() && language === 'en' ? 'true' : null"
              [attr.aria-label]="label() + ' (' + (names[language] ?? language) + ')'" (input)="set(language, $any($event.target).value)" />
          }
        </label>
      }
    </div>
  `,
  styles: `
    :host { display: grid; gap: var(--space-2); }
    .rows { display: grid; gap: var(--space-2); }
    .row { display: flex; align-items: flex-start; gap: var(--space-2); }
    .code { flex: none; width: 24px; padding-top: 9px; color: var(--muted); font-size: var(--text-xs); font-weight: 600; text-transform: uppercase; }
  `,
})
export class TranslatedInput {
  protected readonly names = LANGUAGES;
  readonly value = model<Record<string, string> | null>(null);
  readonly label = input('');
  readonly disabled = input(false);
  readonly multiline = input(false);
  readonly invalid = input(false);
  protected readonly languages = languagesOf();

  protected set(language: string, typed: string): void {
    const next = { ...(this.value() ?? {}) };
    if (typed.trim()) {
      next[language] = typed;
    } else {
      delete next[language];
    }
    this.value.set(next);
  }
}
