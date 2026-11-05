import { describe, expect, it } from 'vitest';
import { MenuNode, itemAt, itemsOf, routeOf } from './menu';

const node = (code: string, screen: string | null, parameter: string | null = null, extra: Partial<MenuNode> = {}): MenuNode => ({
  id: code, code, name: code, isElement: screen !== null, screen, parameter, documentCategoryId: null, icon: null, documentTypes: [], children: [],
  ...extra,
});

describe('menu', () => {
  it('routes each kind of item by its screen key and parameter', () => {
    expect(routeOf(node('SALES', 'documents', null, { documentCategoryId: 'c1' }))).toBe('/documents/c1');
    expect(routeOf(node('PRODUCTS', 'entities', 'products'))).toBe('/entities/products');
    expect(routeOf(node('VAT_GROUPS', 'config', 'vat-groups'))).toBe('/config/vat-groups');
    expect(routeOf(node('SETUP_PARTNERS', 'setup', 'partners'))).toBe('/setup/partners');
    expect(routeOf(node('ASSET_VALUE', 'asset-value', null))).toBe('/asset-value');
    expect(routeOf(node('TRIAL_BALANCE', 'report', 'trial-balance'))).toBe('/reports/trial-balance');
    expect(routeOf(node('POINT_OF_SALE', 'pos', 'sme'))).toBe('/pos/sme');
    expect(routeOf(node('FILES', 'files'))).toBe('/files');
    expect(routeOf(node('REPORTS', null))).toBeNull();
  });

  it('finds the item a URL shows, through the folders', () => {
    const trial = node('TRIAL_BALANCE', 'report', 'trial-balance');
    const tree = [node('REPORTS', null, null, { children: [node('FINANCIAL', null, null, { children: [trial] })] }), node('FILES', 'files')];
    expect(itemsOf(tree).map(n => n.code)).toEqual(['TRIAL_BALANCE', 'FILES']);
    expect(itemAt(tree, '/reports/trial-balance?from=2026-10-01')).toBe(trial);
    expect(itemAt(tree, '/reports/trial-balance/pdf')).toBe(trial);
    expect(itemAt(tree, '/reports/ledger')).toBeNull();
  });
});
