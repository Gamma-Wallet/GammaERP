import { describe, expect, it } from 'vitest';
import { addUnit, moveUnit, unitScope } from './product-units';

const unit = (unitId: string) => ({ unitId, code: unitId.toUpperCase(), name: unitId, inUse: false });

describe('product units', () => {
  it('moves a unit up or down the list (the first is the default), never off its ends', () => {
    expect(moveUnit(['piece', 'box', 'kg'], 1, 0)).toEqual(['box', 'piece', 'kg']);
    expect(moveUnit(['piece', 'box', 'kg'], 0, 2)).toEqual(['box', 'kg', 'piece']);
    expect(moveUnit(['piece', 'box'], 0, -1)).toEqual(['piece', 'box']);
  });

  it('adds a unit at the end once', () => {
    expect(addUnit([unit('piece')], unit('box')).map(u => u.unitId)).toEqual(['piece', 'box']);
    expect(addUnit([unit('piece')], unit('piece')).map(u => u.unitId)).toEqual(['piece']);
  });

  it("searches the units in the units register's own scope", () => {
    const scopes = [{ id: 'kinds', table: 'tbl_unit', categoryId: 'weights', typeId: null, name: 'Weights' },
      { id: 'all', table: 'tbl_unit', categoryId: null, typeId: null, name: 'Units' }, { id: 'p', table: 'tbl_product', categoryId: null, typeId: null, name: 'Products' }];
    expect(unitScope(scopes)?.id).toBe('all');
    expect(unitScope(scopes.slice(0, 1))?.id).toBe('kinds');
    expect(unitScope([])).toBeNull();
  });
});
