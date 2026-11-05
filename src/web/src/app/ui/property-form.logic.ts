// The rules of a form built from a type's properties (TypeInfoDto), as the API checks them: which properties are shown, what is empty,
// and what is saved. The values are the element's Values: property id → the JSON of the property's kind (see ElementInfo.cs).
import { text } from '../core/text';

/** What a property holds (Gamma.Erp.Models.Base.PropertyKind; the API sends the number). */
export enum PropertyKind {
  Text = 1,
  Number = 2,
  DateTime = 3,
  Date = 4,
  Photo = 5,
  List = 6,
  MultiList = 7,
  Location = 8,
  Lookup = 9,
  MultiText = 10,
  ActivatingList = 11,
}

export type Translated = Record<string, string>;

export interface PropertyChoice {
  id: string;
  name: Translated;
  active?: boolean;
}

/** A property of a type, as the API's TypeInfoDto sends it. */
export interface Property {
  id: string;
  name: Translated;
  description?: Translated | null;
  required: boolean;
  kind: PropertyKind;
  sortOrder: number;
  /** For a lookup: the entity it picks from. */
  elementsId?: string | null;
  isFile?: boolean;
  /** The activating-list choice that shows this property; empty: always shown. */
  shownForChoiceId?: string | null;
  /** A fixed key the engine finds the property by, e.g. BARCODE. */
  code?: string | null;
  choices: PropertyChoice[];
  active?: boolean;
}

export type PropertyValues = Record<string, unknown>;

export interface Location {
  lat: number | string | null;
  lng: number | string | null;
}

/** An exact decimal as people type it: 1500, 1500.5, -0.25 (a point, no thousands separators). */
const DECIMAL = /^-?\d+(\.\d+)?$/;

function ids(value: unknown): string[] {
  return Array.isArray(value) ? value.map(String) : typeof value === 'string' && value ? [value] : [];
}

/**
 * The properties shown, in their order: active ones that are always shown, or whose activating choice is picked in a shown activating
 * list. A choice picked in a list that is itself hidden shows nothing.
 */
export function shownProperties(properties: readonly Property[], values: PropertyValues): Property[] {
  const active = properties.filter(p => p.active !== false).sort((a, b) => a.sortOrder - b.sortOrder);
  let shown = active.filter(p => !p.shownForChoiceId);
  // Each pass may show the properties of a list shown by the pass before; it stops when nothing more is shown.
  for (;;) {
    const picked = new Set(shown.filter(p => p.kind === PropertyKind.ActivatingList).flatMap(p => ids(values[p.id])));
    const next = active.filter(p => !p.shownForChoiceId || picked.has(p.shownForChoiceId));
    if (next.length === shown.length) {
      return next;
    }
    shown = next;
  }
}

export function isEmpty(kind: PropertyKind, value: unknown): boolean {
  if (value === null || value === undefined || value === '') {
    return true;
  }
  switch (kind) {
    case PropertyKind.Text:
      return typeof value !== 'object' || !Object.values(value as Translated).some(t => t?.trim());
    case PropertyKind.MultiText:
      return !Array.isArray(value) || !value.some(t => t && Object.values(t as Translated).some(x => x?.trim()));
    case PropertyKind.MultiList:
    case PropertyKind.ActivatingList:
      return ids(value).length === 0;
    case PropertyKind.Location: {
      const { lat, lng } = value as Location;
      return lat === null || lat === undefined || lat === '' || lng === null || lng === undefined || lng === '';
    }
    default:
      return typeof value === 'string' && !value.trim();
  }
}

function badNumber(value: unknown): boolean {
  return value !== null && value !== undefined && value !== '' && !DECIMAL.test(String(value).trim());
}

/** The errors of the shown properties, by property id: a required one left empty, a number that is not one. */
export function validateProperties(properties: readonly Property[], values: PropertyValues): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const p of shownProperties(properties, values)) {
    const value = values[p.id];
    if (p.required && isEmpty(p.kind, value)) {
      errors[p.id] = text.ui.requiredMissing;
    } else if (p.kind === PropertyKind.Number && badNumber(value)) {
      errors[p.id] = text.ui.notNumber;
    } else if (p.kind === PropertyKind.Location && !isEmpty(p.kind, value)) {
      const { lat, lng } = value as Location;
      if (badNumber(lat) || badNumber(lng)) {
        errors[p.id] = text.ui.notNumber;
      }
    }
  }
  return errors;
}

/**
 * The values to send to the API: only the shown properties (a hidden one is not filled in), empty ones as null, numbers as JSON numbers
 * written exactly as typed (no float rounding: the text goes into the JSON as it is), a date and time with the screen's time offset.
 */
export function valuesToSave(properties: readonly Property[], values: PropertyValues): Record<string, unknown> {
  const saved: Record<string, unknown> = {};
  for (const p of shownProperties(properties, values)) {
    const value = values[p.id];
    if (isEmpty(p.kind, value)) {
      saved[p.id] = null;
      continue;
    }
    switch (p.kind) {
      case PropertyKind.Number:
        saved[p.id] = new RawNumber(String(value).trim());
        break;
      case PropertyKind.Location: {
        const { lat, lng } = value as Location;
        saved[p.id] = { lat: new RawNumber(String(lat).trim()), lng: new RawNumber(String(lng).trim()) };
        break;
      }
      case PropertyKind.DateTime:
        saved[p.id] = withOffset(String(value));
        break;
      case PropertyKind.MultiText:
        saved[p.id] = (value as Translated[]).filter(t => !isEmpty(PropertyKind.Text, t));
        break;
      default:
        saved[p.id] = value;
    }
  }
  return saved;
}

/** A number kept as the text typed; toJSON lets JSON.stringify write it, and toSaveJson writes it without quotes. */
export class RawNumber {
  constructor(readonly digits: string) {}

  toJSON(): number {
    return Number(this.digits);
  }
}

/** The body JSON with every RawNumber written as its own digits, so 0.1 + exact decimals reach the API unchanged. */
export function toSaveJson(body: unknown): string {
  const marks: string[] = [];
  // JSON.stringify calls toJSON before the replacer, so the replacer reads the original from its holder (this[key]).
  const json = JSON.stringify(body, function (this: Record<string, unknown>, key: string, value: unknown) {
    const original = this[key];
    if (original instanceof RawNumber) {
      marks.push(original.digits);
      return `\u0000${marks.length - 1}\u0000`;
    }
    return value;
  });
  return json.replace(/"\\u0000(\d+)\\u0000"/g, (_m, i: string) => marks[Number(i)]);
}

/** "2026-10-29T14:30" (a datetime-local input) as "2026-10-29T14:30:00+01:00", the screen's own offset. */
export function withOffset(local: string): string {
  if (/[zZ]|[+-]\d{2}:\d{2}$/.test(local)) {
    return local;
  }
  const date = new Date(local);
  if (Number.isNaN(date.getTime())) {
    return local;
  }
  const minutes = -date.getTimezoneOffset();
  const sign = minutes >= 0 ? '+' : '-';
  const hh = String(Math.floor(Math.abs(minutes) / 60)).padStart(2, '0');
  const mm = String(Math.abs(minutes) % 60).padStart(2, '0');
  const seconds = local.length === 16 ? `${local}:00` : local;
  return `${seconds}${sign}${hh}:${mm}`;
}

/** A saved moment as a datetime-local input shows it ("2026-10-29T14:30"), in the screen's time. */
export function toLocalInput(value: unknown): string {
  if (typeof value !== 'string' || !value) {
    return '';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
