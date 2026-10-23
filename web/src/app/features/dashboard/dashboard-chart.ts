import { Component, ElementRef, effect, input, viewChild } from '@angular/core';
import {
  ArcElement, BarController, BarElement, CategoryScale, Chart, DoughnutController,
  Legend, LinearScale, Tooltip, type ChartConfiguration,
} from 'chart.js';

Chart.register(ArcElement, BarController, BarElement, CategoryScale, DoughnutController, Legend, LinearScale, Tooltip);

/** Owns the canvas lifecycle so navigation and data refreshes never leak chart instances. */
@Component({
  selector: 'app-dashboard-chart',
  template: '<canvas #canvas role="img" [attr.aria-label]="description()">{{ description() }}</canvas>',
  styles: ':host { display: block; position: relative; height: 280px; min-width: 0; }',
})
export class DashboardChart {
  readonly config = input.required<ChartConfiguration>();
  readonly description = input.required<string>();
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');

  constructor() {
    effect((onCleanup) => {
      const canvas = this.canvas()?.nativeElement;
      const config = this.config();
      if (!canvas) return;
      const chart = new Chart(canvas, config);
      onCleanup(() => chart.destroy());
    });
  }
}
