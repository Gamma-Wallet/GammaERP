import { t } from '../core/i18n';
// The kit's chart rules in one pure function, so every chart reads the same and the rules are tested:
// colours by series in a fixed order (never by rank), one value axis, thin marks, a legend only from two series, line names at the line's
// end, a hover tooltip on every chart, and a ninth series folded into "Other".
import { formatNumber } from './format';

export type ChartKind = 'line' | 'bar' | 'stacked-bar' | 'donut';

export interface ChartSeries {
  name: string;
  values: (number | null)[];
}

export interface ChartSpec {
  kind: ChartKind;
  /** The x axis (months, days) or, for a donut, the slices. */
  categories: string[];
  /** A donut takes one series: its values are the slices. */
  series: ChartSeries[];
  decimals?: number;
  unit?: string;
}

/** The theme a chart is drawn in, read from the CSS tokens. */
export interface ChartTheme {
  series: string[];
  surface: string;
  ink: string;
  muted: string;
  line: string;
  axis: string;
  font: string;
  /** The skin's pop-up shadow, corner radius and hover fill, for the tooltip and the bar pointer. */
  shadow?: string;
  radius?: string;
  hover?: string;
  /** The skin's small and medium-small text sizes, in px. */
  textXs?: number;
  textS?: number;
}

export const MAX_SERIES = 8;
export const OTHER = t('Other');

/** At most 8 series: the rest are added up into "Other", so no colour is ever invented or repeated. */
export function foldSeries(series: readonly ChartSeries[], max = MAX_SERIES): ChartSeries[] {
  if (series.length <= max) {
    return [...series];
  }
  const kept = series.slice(0, max - 1);
  const rest = series.slice(max - 1);
  const length = Math.max(...rest.map(s => s.values.length));
  const values = Array.from({ length }, (_, i) => {
    const parts = rest.map(s => s.values[i]).filter((v): v is number => v !== null && v !== undefined);
    return parts.length ? parts.reduce((a, b) => a + b, 0) : null;
  });
  return [...kept, { name: OTHER, values }];
}

/** A donut's slices, the largest kept and the rest added up into "Other". */
export function foldSlices(categories: readonly string[], values: readonly (number | null)[], max = MAX_SERIES): { name: string; value: number }[] {
  const slices = categories.map((name, i) => ({ name, value: values[i] ?? 0 }));
  if (slices.length <= max) {
    return slices;
  }
  const kept = slices.slice(0, max - 1);
  const other = slices.slice(max - 1).reduce((sum, s) => sum + s.value, 0);
  return [...kept, { name: OTHER, value: other }];
}

export function chartOption(spec: ChartSpec, theme: ChartTheme): Record<string, unknown> {
  const decimals = spec.decimals ?? 0;
  const format = (v: unknown) => (v === null || v === undefined ? '' : `${formatNumber(v as number, decimals)}${spec.unit ? ` ${spec.unit}` : ''}`);
  const small = theme.textXs || 12;
  const text = { color: theme.muted, fontFamily: theme.font, fontSize: small };
  const tooltip = {
    backgroundColor: theme.surface,
    borderColor: theme.line,
    borderWidth: 1,
    padding: [8, 12],
    textStyle: { color: theme.ink, fontFamily: theme.font, fontSize: theme.textS || 13 },
    extraCssText: `box-shadow: ${theme.shadow || '0 8px 24px rgb(0 0 0 / 0.12)'}; border-radius: ${theme.radius || '8px'};`,
    valueFormatter: format,
  };

  if (spec.kind === 'donut') {
    const slices = foldSlices(spec.categories, spec.series[0]?.values ?? []);
    return {
      color: theme.series,
      textStyle: { fontFamily: theme.font },
      tooltip: { ...tooltip, trigger: 'item' },
      legend: { show: slices.length >= 2, bottom: 0, icon: 'circle', itemWidth: 10, itemHeight: 10, textStyle: { ...text, color: theme.ink } },
      series: [{
        type: 'pie',
        radius: ['55%', '78%'],
        center: ['50%', '45%'],
        avoidLabelOverlap: true,
        // A 2px ring in the surface colour between slices.
        itemStyle: { borderColor: theme.surface, borderWidth: 2, borderRadius: 4 },
        label: { show: false },
        emphasis: { scale: true, scaleSize: 4 },
        data: slices,
      }],
    };
  }

  const series = foldSeries(spec.series);
  const many = series.length >= 2;
  const line = spec.kind === 'line';
  const stacked = spec.kind === 'stacked-bar';
  return {
    color: theme.series,
    textStyle: { fontFamily: theme.font },
    grid: { left: 8, right: line && many && series.length <= 4 ? 88 : 16, top: many ? 40 : 16, bottom: 8, containLabel: true },
    legend: { show: many, top: 0, left: 0, icon: line ? 'roundRect' : 'circle', itemWidth: line ? 14 : 10, itemHeight: line ? 3 : 10,
      textStyle: { ...text, color: theme.ink } },
    tooltip: {
      ...tooltip,
      trigger: 'axis',
      axisPointer: line ? { type: 'line', lineStyle: { color: theme.axis, width: 1 } } : { type: 'shadow', shadowStyle: { color: theme.hover || 'rgb(0 0 0 / 0.04)' } },
    },
    xAxis: {
      type: 'category',
      data: spec.categories,
      boundaryGap: !line,
      axisLine: { lineStyle: { color: theme.axis } },
      axisTick: { show: false },
      axisLabel: text,
    },
    // One value axis, always: two measures of a different scale are two charts.
    yAxis: {
      type: 'value',
      axisLabel: { ...text, formatter: (v: number) => formatNumber(v, 0) },
      splitLine: { lineStyle: { color: theme.line, width: 1 } },
    },
    series: series.map(s => line
      ? {
          type: 'line',
          name: s.name,
          data: s.values,
          lineStyle: { width: 2 },
          symbol: 'circle',
          symbolSize: 8,
          showSymbol: false,
          emphasis: { focus: 'series', itemStyle: { borderColor: theme.surface, borderWidth: 2 } },
          // Up to four lines carry their name at the end, so a line is never told apart by colour alone.
          endLabel: { show: many && series.length <= 4, formatter: '{a}', color: theme.ink, fontFamily: theme.font, fontSize: small },
          // Lines ending close together push their names apart instead of printing one over the other.
          labelLayout: { moveOverlap: 'shiftY' },
          connectNulls: false,
        }
      : {
          type: 'bar',
          name: s.name,
          data: s.values,
          stack: stacked ? 'total' : undefined,
          barMaxWidth: 28,
          barGap: '20%',
          // Rounded only at the data end; a 2px surface gap between stacked parts.
          itemStyle: { borderRadius: stacked ? 0 : [4, 4, 0, 0], borderColor: theme.surface, borderWidth: stacked ? 2 : 0 },
          emphasis: { focus: 'series' },
        }),
  };
}
