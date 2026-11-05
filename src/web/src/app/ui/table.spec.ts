import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Column, Sort, Table, cellText, flattenTree, nextSort, parentIds, sortRows } from './table';

interface Row {
  id: string;
  code: string | null;
  amount: number | null;
  children?: Row[];
}

const columns: Column<Row>[] = [
  { key: 'code', label: 'Code', sortable: true },
  { key: 'amount', label: 'Amount', format: 'number', sortable: true },
];

const rows: Row[] = [
  { id: '1', code: 'A10', amount: 5 },
  { id: '2', code: 'A2', amount: null },
  { id: '3', code: null, amount: 100 },
  { id: '4', code: 'a1', amount: 20 },
];

describe('table rules', () => {
  it('sorts texts as people read them, numbers by value, empty values last either way', () => {
    expect(sortRows(rows, columns, { key: 'code', direction: 'asc' }).map(r => r.id)).toEqual(['4', '2', '1', '3']);
    expect(sortRows(rows, columns, { key: 'code', direction: 'desc' }).map(r => r.id)).toEqual(['1', '2', '4', '3']);
    expect(sortRows(rows, columns, { key: 'amount', direction: 'desc' }).map(r => r.id)).toEqual(['3', '4', '1', '2']);
    expect(sortRows(rows, columns, null).map(r => r.id)).toEqual(['1', '2', '3', '4']);
  });

  it('cycles a heading through ascending, descending and unsorted', () => {
    const asc = nextSort(null, 'code');
    expect(asc).toEqual({ key: 'code', direction: 'asc' });
    expect(nextSort(asc, 'code')).toEqual({ key: 'code', direction: 'desc' });
    expect(nextSort({ key: 'code', direction: 'desc' }, 'code')).toBeNull();
    expect(nextSort({ key: 'code', direction: 'desc' }, 'amount')).toEqual({ key: 'amount', direction: 'asc' });
  });

  it('formats numbers and dates for their column', () => {
    expect(cellText({ id: 'x', code: null, amount: 1500.5 }, columns[1])).toBe('1,500.50');
    expect(cellText({ id: 'x', code: null, amount: null }, columns[1])).toBe('');
    expect(cellText({ day: '2026-10-05' }, { key: 'day', label: '', format: 'date' })).toBe('05/10/2026');
  });

  it('shows a tree depth first, children only under expanded rows', () => {
    const tree: Row[] = [
      { id: 'a', code: '1', amount: null, children: [{ id: 'a1', code: '10', amount: 1, children: [{ id: 'a11', code: '100', amount: 1 }] }] },
      { id: 'b', code: '2', amount: 2 },
    ];
    const lines = flattenTree(tree, r => r.id, r => r.children, new Set(['a', 'a1']));
    expect(lines.map(l => `${l.id}:${l.depth}:${l.hasChildren}`)).toEqual(['a:0:true', 'a1:1:true', 'a11:2:false', 'b:0:false']);
    expect(flattenTree(tree, r => r.id, r => r.children, new Set(['a1'])).map(l => l.id)).toEqual(['a', 'b']);
    expect(parentIds(tree, r => r.id, r => r.children)).toEqual(['a', 'a1']);
  });
});

@Component({
  imports: [Table],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-table [columns]="columns" [rows]="rows" [(sort)]="sort" [(page)]="page" [pageSize]="2" />`,
})
class Host {
  readonly columns = columns;
  readonly rows = rows;
  readonly sort = signal<Sort | null>(null);
  readonly page = signal(1);
}

describe('ui-table', () => {
  it('sorts on a heading click, says so to screen readers, and pages the rows', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    const codes = () => [...el.querySelectorAll('tbody tr')].map(tr => tr.querySelector('td')!.textContent!.trim());

    expect(codes()).toEqual(['A10', 'A2']);
    expect(el.querySelector('.pager')!.textContent).toContain('1–2 of 4');

    el.querySelector<HTMLButtonElement>('th .sort')!.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.sort()).toEqual({ key: 'code', direction: 'asc' });
    expect(el.querySelector('th')!.getAttribute('aria-sort')).toBe('ascending');
    expect(codes()).toEqual(['a1', 'A2']);

    el.querySelector<HTMLButtonElement>('.pager button:last-of-type')!.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.page()).toBe(2);
    expect(codes()).toEqual(['A10', '']);
  });
});
