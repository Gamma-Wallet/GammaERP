import { describe, expect, it } from 'vitest';
import { Property, PropertyKind, shownProperties, toSaveJson, validateProperties, valuesToSave, withOffset } from './property-form.logic';

const prop = (id: string, kind: PropertyKind, extra: Partial<Property> = {}): Property => ({
  id, name: { en: id }, required: false, kind, sortOrder: Number(id.slice(1)), choices: [], ...extra,
});

// p1 "Sold as" (activating) → choice c1 shows p2 (also activating) → its choice c2 shows p3; p4 is inactive.
const properties: Property[] = [
  prop('p3', PropertyKind.Number, { required: true, shownForChoiceId: 'c2' }),
  prop('p1', PropertyKind.ActivatingList, { choices: [{ id: 'c1', name: { en: 'Draught' } }] }),
  prop('p2', PropertyKind.ActivatingList, { shownForChoiceId: 'c1', choices: [{ id: 'c2', name: { en: 'Large' } }] }),
  prop('p4', PropertyKind.Text, { active: false }),
  prop('p5', PropertyKind.Text, { required: true }),
];

describe('property form rules', () => {
  it('shows a property only when its choice is picked in a shown list, in sort order', () => {
    expect(shownProperties(properties, {}).map(p => p.id)).toEqual(['p1', 'p5']);
    expect(shownProperties(properties, { p1: ['c1'] }).map(p => p.id)).toEqual(['p1', 'p2', 'p5']);
    expect(shownProperties(properties, { p1: ['c1'], p2: ['c2'] }).map(p => p.id)).toEqual(['p1', 'p2', 'p3', 'p5']);
    // c2 is still in the values, but its list is hidden again: p3 is not shown.
    expect(shownProperties(properties, { p1: [], p2: ['c2'] }).map(p => p.id)).toEqual(['p1', 'p5']);
  });

  it('requires only shown properties, and numbers written as exact decimals', () => {
    expect(validateProperties(properties, { p5: { en: ' ' } })).toEqual({ p5: 'Fill this in.' });
    expect(validateProperties(properties, { p5: { sq: 'Birra' } })).toEqual({});
    const chosen = { p1: ['c1'], p2: ['c2'], p5: { en: 'Lager' } };
    expect(validateProperties(properties, chosen)).toEqual({ p3: 'Fill this in.' });
    expect(validateProperties(properties, { ...chosen, p3: '1,5' })).toEqual({ p3: 'Enter a number, e.g. 1500.50.' });
    expect(validateProperties(properties, { ...chosen, p3: '0.5' })).toEqual({});
  });

  it('saves only the shown properties, empty ones as null, numbers with their exact digits', () => {
    const values = { p1: ['c1'], p2: [], p3: '0.10000000000000000001', p4: { en: 'old' }, p5: { en: 'Lager' } };
    const saved = valuesToSave(properties, values);
    expect(Object.keys(saved)).toEqual(['p1', 'p2', 'p5']);
    expect(saved['p2']).toBeNull();

    const exact = valuesToSave(properties, { ...values, p2: ['c2'] });
    expect(toSaveJson(exact)).toBe('{"p1":["c1"],"p2":["c2"],"p3":0.10000000000000000001,"p5":{"en":"Lager"}}');
  });

  it('adds the screen offset to a date and time, and leaves one that has it', () => {
    expect(withOffset('2026-10-29T14:30')).toMatch(/^2026-10-29T14:30:00[+-]\d{2}:\d{2}$/);
    expect(withOffset('2026-10-29T14:30:00+01:00')).toBe('2026-10-29T14:30:00+01:00');
  });
});
