import { TestBed } from '@angular/core/testing';
import type { ChartConfiguration } from 'chart.js';
import { DashboardChart } from './dashboard-chart';

const { createChart, destroyChart } = vi.hoisted(() => ({ createChart: vi.fn(), destroyChart: vi.fn() }));

vi.mock('chart.js', () => ({
  Chart: class {
    static register = vi.fn();
    constructor(...args: unknown[]) { createChart(...args); }
    destroy() { destroyChart(); }
  },
  ArcElement: {}, BarController: {}, BarElement: {}, CategoryScale: {},
  DoughnutController: {}, Legend: {}, LinearScale: {}, Tooltip: {},
}));

describe('DashboardChart', () => {
  beforeEach(() => { createChart.mockClear(); destroyChart.mockClear(); });

  it('destroys replaced and detached chart instances and gives the canvas a text alternative', async () => {
    const fixture = TestBed.createComponent(DashboardChart);
    const initial: ChartConfiguration = { type: 'bar', data: { labels: ['Jan'], datasets: [{ data: [125] }] } };
    fixture.componentRef.setInput('config', initial);
    fixture.componentRef.setInput('description', 'January gross sales. Exact values follow in the table.');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(createChart).toHaveBeenCalledOnce();
    const canvas = fixture.nativeElement.querySelector('canvas') as HTMLCanvasElement;
    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('aria-label')).toContain('Exact values follow in the table');

    fixture.componentRef.setInput('config', { ...initial, data: { labels: ['Feb'], datasets: [{ data: [200] }] } });
    fixture.detectChanges();
    await fixture.whenStable();
    expect(destroyChart).toHaveBeenCalledTimes(1);
    expect(createChart).toHaveBeenCalledTimes(2);

    fixture.destroy();
    expect(destroyChart).toHaveBeenCalledTimes(2);
  });
});
