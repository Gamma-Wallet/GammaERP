import { describe, expect, it } from 'vitest';
import { ChartTheme, OTHER, chartOption, foldSeries, foldSlices } from './chart-option';
import { toggleChip } from './chips';
import { filterOptions } from './select';

const theme: ChartTheme = {
  series: ['#1', '#2', '#3', '#4', '#5', '#6', '#7', '#8'], surface: '#fff', ink: '#000', muted: '#666', line: '#eee', axis: '#ccc', font: 'Inter',
};

describe('chart rules', () => {
  it('folds a ninth series into "Other", adding up the rest', () => {
    const series = Array.from({ length: 10 }, (_, i) => ({ name: `S${i + 1}`, values: [i, i === 9 ? null : 1] }));
    const folded = foldSeries(series);
    expect(folded.map(s => s.name)).toEqual(['S1', 'S2', 'S3', 'S4', 'S5', 'S6', 'S7', OTHER]);
    expect(folded[7].values).toEqual([7 + 8 + 9, 2]);
    expect(foldSlices(['a', 'b'], [1, null])).toEqual([{ name: 'a', value: 1 }, { name: 'b', value: 0 }]);
  });

  it('keeps the colours in their fixed order, one value axis, and a legend only from two series', () => {
    const one = chartOption({ kind: 'bar', categories: ['Mon'], series: [{ name: 'Bills', values: [3] }] }, theme);
    expect(one['color']).toEqual(theme.series);
    expect((one['legend'] as { show: boolean }).show).toBe(false);
    expect(Array.isArray(one['yAxis'])).toBe(false);

    const two = chartOption({ kind: 'line', categories: ['May'], series: [{ name: 'A', values: [1] }, { name: 'B', values: [2] }] }, theme);
    expect((two['legend'] as { show: boolean }).show).toBe(true);
    const lines = two['series'] as { lineStyle: { width: number }; endLabel: { show: boolean } }[];
    expect(lines.map(l => [l.lineStyle.width, l.endLabel.show])).toEqual([[2, true], [2, true]]);
    expect((two['tooltip'] as { trigger: string }).trigger).toBe('axis');
  });
});

describe('chips and select rules', () => {
  it('toggles chips one at a time or several', () => {
    expect(toggleChip(['a'], 'b', true)).toEqual(['a', 'b']);
    expect(toggleChip(['a'], 'b', false)).toEqual(['b']);
    expect(toggleChip(['a', 'b'], 'a', true)).toEqual(['b']);
  });

  it('finds options holding every word typed, in the label or the hint', () => {
    const options = [{ value: 1, label: 'North Bakery', hint: 'P-100' }, { value: 2, label: 'Lantern Bar', hint: 'P-105' }];
    expect(filterOptions(options, 'bar').map(o => o.value)).toEqual([2]);
    expect(filterOptions(options, 'p-10 north').map(o => o.value)).toEqual([1]);
    expect(filterOptions(options, '  ').map(o => o.value)).toEqual([1, 2]);
  });
});
