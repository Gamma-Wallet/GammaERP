import { describe, expect, it } from 'vitest';
import { companyProblem, companyRequestOf } from './new-company';
import { childrenOf, pathOf, toggleGroup } from './setup-parts';

const groups = [{ name: 'chart', requires: [] }, { name: 'documents', requires: [] }, { name: 'posting', requires: ['chart', 'documents'] },
  { name: 'depreciation', requires: ['chart'] }];
const catalogue = [
  { code: 'GENERAL', parent: null, name: { en: 'General' } }, { code: 'RETAIL', parent: 'GENERAL', name: { en: 'Retail' } },
  { code: 'FOOD', parent: 'GENERAL', name: { en: 'Food and drink' } }, { code: 'BAR', parent: 'FOOD', name: { en: 'Bar' } },
];

describe('setup rules', () => {
  it('brings what a group needs, and takes along what needs an unticked one', () => {
    expect(toggleGroup(groups, [], 'posting', true)).toEqual(['chart', 'documents', 'posting']);
    expect(toggleGroup(groups, ['chart', 'documents', 'posting', 'depreciation'], 'chart', false)).toEqual(['documents']);
    expect(toggleGroup(groups, ['chart', 'documents'], 'documents', false)).toEqual(['chart']);
  });

  it('walks the activity catalogue level by level', () => {
    expect(childrenOf(catalogue, 'GENERAL').map(a => a.code)).toEqual(['FOOD', 'RETAIL']);
    expect(pathOf(catalogue, 'BAR').map(a => a.code)).toEqual(['GENERAL', 'FOOD', 'BAR']);
  });

  it('sends the new company as the API takes it', () => {
    // No cash desks or banks: they are cost centres, added in the cost-centre register afterwards.
    const company = { country: 'IT', name: ' Bar Roma ', code: '', activity: 'BAR', fromCatalogue: false, copyFrom: 'c1', groups: ['chart'] };
    expect(companyRequestOf(company)).toMatchObject({ name: 'Bar Roma', code: null, copyFrom: 'c1' });
    expect(companyRequestOf(company)).not.toHaveProperty('banks');
    expect(companyProblem(1, { ...company, name: ' ' })).toContain('Name');
  });
});
