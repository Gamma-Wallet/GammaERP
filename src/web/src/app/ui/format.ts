import { language } from '../core/i18n';

// How the screens show values. English formats for now (the screens are English only, the founder 2026-10-30); the locale is one place.
const LOCALE = 'en-GB';

const numbers = new Map<number, Intl.NumberFormat>();

/** A number with a fixed count of decimals (amounts 2, quantities as the unit says); empty for null. */
export function formatNumber(value: number | string | null | undefined, decimals = 2): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  const n = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(n)) {
    return String(value);
  }
  let format = numbers.get(decimals);
  if (!format) {
    format = new Intl.NumberFormat(LOCALE, { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    numbers.set(decimals, format);
  }
  return format.format(n);
}

const dates = new Intl.DateTimeFormat(LOCALE, { day: '2-digit', month: '2-digit', year: 'numeric' });
const dateTimes = new Intl.DateTimeFormat(LOCALE, { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });

/** A date ("2026-10-30") or a moment, as the screens show it; the text itself when it is not one. */
export function formatDate(value: string | Date | null | undefined, withTime = false): string {
  if (!value) {
    return '';
  }
  // A bare date is a calendar day, not midnight UTC: read it as local so it never shows as the day before.
  const date = value instanceof Date ? value : /^\d{4}-\d{2}-\d{2}$/.test(value) ? new Date(`${value}T00:00:00`) : new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : (withTime ? dateTimes : dates).format(date);
}

/** A change as a signed percentage, e.g. +12.5%. */
export function formatChange(percent: number): string {
  const sign = percent > 0 ? '+' : percent < 0 ? '−' : '';
  return `${sign}${formatNumber(Math.abs(percent), 1)}%`;
}

/** A translated text ({"en": "…", "sq": "…"}) in the language, else English, else any; a plain text as it is. */
export function textIn(value: Record<string, string> | string | null | undefined, code = language()): string {
  if (!value) {
    return '';
  }
  if (typeof value === 'string') {
    return value;
  }
  return value[code] ?? value['en'] ?? Object.values(value)[0] ?? '';
}
