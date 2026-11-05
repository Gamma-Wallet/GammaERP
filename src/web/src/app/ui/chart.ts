import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, computed, effect, inject, input, signal, viewChild,
} from '@angular/core';
import { text } from '../core/text';
import { ChartSpec, ChartTheme, chartOption, foldSeries, foldSlices } from './chart-option';
import { Button } from './controls';
import { Icon } from './icon';
import { Column, Table } from './table';

type ECharts = typeof import('echarts/core');
type Instance = ReturnType<ECharts['init']>;

let loading: Promise<ECharts> | null = null;

/** ECharts is loaded with the first chart (echarts-setup.ts), so screens without charts never download it. */
function loadECharts(): Promise<ECharts> {
  loading ??= import('./echarts-setup').then(m => m.echarts);
  return loading;
}

function themeFromTokens(element: HTMLElement): ChartTheme {
  const css = getComputedStyle(element);
  const token = (name: string) => css.getPropertyValue(name).trim();
  return {
    series: Array.from({ length: 8 }, (_, i) => token(`--series-${i + 1}`)),
    surface: token('--surface'),
    ink: token('--ink'),
    muted: token('--muted'),
    line: token('--line'),
    axis: token('--line-strong'),
    font: token('--font-text'),
    shadow: token('--shadow-pop'),
    radius: token('--radius'),
    hover: token('--hover'),
    textXs: parseFloat(token('--text-xs')),
    textS: parseFloat(token('--text-s')),
  };
}

interface TableRow {
  id: string;
  label: string;
  [series: string]: string | number | null;
}

/**
 * A chart from a plain description ([spec]: kind, categories, series), drawn by the kit's rules (chart-option.ts). It always has a table
 * view, for screen readers, for exact values and because three of the series colours are below 3:1 on white.
 */
@Component({
  selector: 'ui-chart',
  imports: [Button, Icon, Table],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header>
      <div>
        @if (title()) { <h3>{{ title() }}</h3> }
        @if (subtitle()) { <p class="muted">{{ subtitle() }}</p> }
      </div>
      <button uiButton variant="quiet" size="small" (click)="asTable.set(!asTable())">
        <ui-icon [name]="asTable() ? 'bar-chart' : 'table'" [size]="14" />{{ asTable() ? text.ui.asChart : text.ui.asTable }}
      </button>
    </header>
    <div #canvas class="canvas" role="img" [attr.aria-label]="title()" [hidden]="asTable()" [style.height.px]="height()"></div>
    @if (asTable()) {
      <ui-table class="table" [columns]="tableColumns()" [rows]="tableRows()" [pageSize]="1000" [ariaLabel]="title()" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--space-3); min-width: 0; padding: var(--space-4); border: 1px solid var(--line);
      border-radius: var(--radius-l); background: var(--surface); }
    header { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-3); }
    h3 { font-size: var(--title-s); }
    p { margin: 2px 0 0; font-size: var(--text-s); }
    .canvas { width: 100%; min-width: 0; }
    .table { max-height: 360px; }
  `,
})
export class Chart {
  protected readonly text = text;
  readonly spec = input.required<ChartSpec>();
  readonly title = input<string | null>(null);
  readonly subtitle = input<string | null>(null);
  readonly height = input(280);
  protected readonly asTable = signal(false);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');
  private readonly chart = signal<Instance | null>(null);

  /** The same numbers as the chart: one row per category, one column per series. */
  protected readonly tableColumns = computed<Column<TableRow>[]>(() => {
    const spec = this.spec();
    const names = spec.kind === 'donut' ? [spec.series[0]?.name ?? ''] : foldSeries(spec.series).map(s => s.name);
    return [
      { key: 'label', label: '', sortable: true },
      ...names.map(name => ({ key: `s:${name}`, label: name, format: 'number' as const, decimals: spec.decimals ?? 0, sortable: true })),
    ];
  });

  protected readonly tableRows = computed<TableRow[]>(() => {
    const spec = this.spec();
    if (spec.kind === 'donut') {
      const name = spec.series[0]?.name ?? '';
      return foldSlices(spec.categories, spec.series[0]?.values ?? []).map(s => ({ id: s.name, label: s.name, [`s:${name}`]: s.value }));
    }
    const series = foldSeries(spec.series);
    return spec.categories.map((label, i) => {
      const row: TableRow = { id: `${i}`, label };
      for (const s of series) {
        row[`s:${s.name}`] = s.values[i] ?? null;
      }
      return row;
    });
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(async () => {
      const echarts = await loadECharts();
      if (destroyRef.destroyed) {
        return;
      }
      const chart = echarts.init(this.canvas().nativeElement, null, { renderer: 'svg' });
      this.chart.set(chart);
      const resize = new ResizeObserver(() => chart.resize());
      resize.observe(this.canvas().nativeElement);
      destroyRef.onDestroy(() => {
        resize.disconnect();
        chart.dispose();
      });
    });

    effect(() => {
      const chart = this.chart();
      if (chart) {
        chart.setOption(chartOption(this.spec(), themeFromTokens(this.host.nativeElement)), { notMerge: true });
      }
    });

    // The canvas was hidden while the table showed: draw it at its size again.
    effect(() => {
      if (!this.asTable()) {
        this.chart()?.resize();
      }
    });
  }
}
