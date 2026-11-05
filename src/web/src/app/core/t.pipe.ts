import { Pipe, PipeTransform } from '@angular/core';
import { t } from './i18n';

/** A screen's text in its language: {{ 'Save' | t }}. Pure: changing the language reloads the page. */
@Pipe({ name: 't' })
export class TranslatePipe implements PipeTransform {
  transform(english: string, ...values: readonly unknown[]): string {
    return t(english, ...values);
  }
}
