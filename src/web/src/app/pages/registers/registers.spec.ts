import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { Category, Element, ElementType, buildTree, errorsOf, withDescendants } from '../../core/entities';
import { PropertyKind } from '../../ui/property-form.logic';
import { setCompanyLanguage } from '../../core/i18n';
import { languagesOf } from '../../ui/translated-input';
import { typesFor, versionsOf } from './element-panel';
import { readableTree } from './register-page';
import { typesIn } from './register-setup';
import { typeToSave } from './type-editor';

const category = (id: string, idSup: string | null, sortOrder: number, isElement = true): Category => ({
  id, idSup, active: true, isElement, code: id.toUpperCase(), name: { en: id }, description: null, sortOrder,
});

const type = (id: string, categoryIds: string[], isElement = true): ElementType => ({
  id, idSup: null, active: true, isElement, code: id, codeStart: null, codeEnd: null, codeCurrent: null, name: { en: id }, description: null,
  sortOrder: 0, categoryIds, properties: [],
});

describe('register rules', () => {
  // partners: a folder "trade" holding clients and suppliers, and "staff" on its own.
  const categories = [category('suppliers', 'trade', 2), category('trade', null, 1, false), category('clients', 'trade', 1), category('staff', null, 2)];

  it('builds the category tree in sort order, and shows only what may be read with the folders above it', () => {
    const tree = buildTree(categories);
    expect(tree.map(n => n.id)).toEqual(['trade', 'staff']);
    expect(tree[0].children.map(n => n.id)).toEqual(['clients', 'suppliers']);

    const seen = readableTree(tree, id => id === 'suppliers');
    expect(seen.map(n => n.id)).toEqual(['trade']);
    expect(seen[0].children.map(n => n.id)).toEqual(['suppliers']);
    expect(readableTree(tree, () => false)).toEqual([]);
  });

  it('never offers a row, or one below it, as its own parent', () => {
    expect([...withDescendants(categories, 'trade')].sort()).toEqual(['clients', 'suppliers', 'trade']);
    expect([...withDescendants(categories, 'staff')]).toEqual(['staff']);
  });

  it('offers the element types linked to the row categories, or every element type when none is linked', () => {
    const types = [type('ltd', ['clients']), type('person', ['clients', 'suppliers']), type('group', [], false), type('loose', [])];
    expect(typesFor(types, ['suppliers']).map(t => t.id)).toEqual(['person']);
    expect(typesFor(types, ['staff']).map(t => t.id)).toEqual(['ltd', 'person', 'loose']);
  });

  it('tells what changed in each earlier version against the one after it', () => {
    const current: Element = { id: 'e', idSup: null, active: true, typeId: 't1', code: 'C-2', name: { en: 'Hotel Vlora' }, description: null,
      isComposite: false, categoryIds: [], values: {}, accessUserIds: [], accessRoleIds: [] };
    const versions = versionsOf([
      { id: 'v2', createdOn: '2026-10-30T10:00:00Z', createdBy: null, row: { Code: 'C-1', Name: { en: 'Hotel Vlora' }, TypeId: 't1', Active: false } },
      { id: 'v1', createdOn: '2026-10-29T10:00:00Z', createdBy: null, row: { Code: 'C-1', Name: { en: 'Vlora' }, TypeId: 't1', Active: false } },
    ], current);
    expect(versions.map(v => [v.code, v.name, v.changed])).toEqual([['C-1', 'Hotel Vlora', ['Code']], ['C-1', 'Vlora', ['Name']]]);
  });

  it('sends a type with its properties numbered, new ones without ids, choices only where a kind has them', () => {
    const saved = typeToSave({
      ...type('t', []),
      properties: [
        { id: 'new:1', name: { en: 'Packaging' }, required: true, kind: PropertyKind.List, sortOrder: 9, elementsId: 'x',
          choices: [{ id: 'new:2', name: { en: 'Can' } }, { id: 'c1', name: { en: 'Bottle' } }, { id: 'new:3', name: { en: ' ' } }] },
        { id: 'p1', name: { en: 'Brand' }, required: false, kind: PropertyKind.Text, sortOrder: 1, choices: [{ id: 'c9', name: { en: 'old' } }] },
      ],
    }) as { properties: { id: string | null; sortOrder: number; elementsId: string | null; choices: { id: string | null }[] }[] };
    expect(saved.properties.map(p => [p.id, p.sortOrder, p.elementsId, p.choices.map(c => c.id)])).toEqual([
      [null, 1, null, [null, 'c1']],
      ['p1', 2, null, []],
    ]);
  });

  it('shows English first, then the languages a text has, then those added', () => {
    expect(languagesOf()).toEqual(['en']);
    setCompanyLanguage('sq');
    expect(languagesOf()).toEqual(['en', 'sq']);
    setCompanyLanguage('en');
  });

  it('reads the API refusal by field', () => {
    const refused = new HttpErrorResponse({ status: 422, error: { title: 'Refused', errors: { Name: ['NameError: taken.'], 'properties[0].kind': ['Unknown kind.'] } } });
    expect(errorsOf(refused).fields).toEqual({ name: 'NameError: taken.', 'properties[0].kind': 'Unknown kind.' });
    expect(errorsOf(new HttpErrorResponse({ status: 0 })).message).toContain('did not answer');
  });
});

describe('register set-up', () => {
  const category = (id: string, idSup: string | null = null) => ({ id, idSup }) as unknown as Category;
  const type = (id: string, categoryIds: string[], idSup: string | null = null) => ({ id, idSup, categoryIds }) as unknown as ElementType;
  const categories = [category('all'), category('goods', 'all'), category('other')];
  const types = [type('folder', []), type('beer', ['goods'], 'folder'), type('car', ['other']), type('both', ['goods', 'other'])];

  it('shows every type until a category is chosen', () => {
    expect(typesIn(types, categories, null).map(t => t.id)).toEqual(['folder', 'beer', 'car', 'both']);
  });

  it('filters the types by the category, a folder holding what is under it, keeping the folders above them', () => {
    expect(typesIn(types, categories, 'goods').map(t => t.id)).toEqual(['folder', 'beer', 'both']);
    expect(typesIn(types, categories, 'all').map(t => t.id)).toEqual(['folder', 'beer', 'both']);
    expect(typesIn(types, categories, 'other').map(t => t.id)).toEqual(['car', 'both']);
  });
});
