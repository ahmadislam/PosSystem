import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import { DashboardSummaryDto, money } from '../../core/models';
import { Toast } from '../../core/ui/toast';

@Component({
  selector: 'app-dashboard',
  imports: [DatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);
  readonly money = money;

  readonly sales = signal<DashboardSummaryDto['recentSales']>([]);
  readonly lowStock = signal<DashboardSummaryDto['lowStockProducts']>([]);
  readonly lowStockCount = signal(0);
  readonly revenue = signal(0);
  readonly count = signal(0);
  readonly bars = signal<{ label: string; value: number; pct: number }[]>([]);

  ngOnInit(): void {
    const end = new Date();
    const start = new Date(end);
    start.setDate(start.getDate() - 6);

    this.api.dashboardSummary(localDate(start), localDate(end)).subscribe({
      next: (summary) => {
        this.sales.set(summary.recentSales);
        this.count.set(summary.totalSales);
        this.revenue.set(summary.revenue);
        this.lowStockCount.set(summary.lowStockCount);
        this.lowStock.set(summary.lowStockProducts);
        this.bars.set(buildBars(summary.dailySales, start, 7));
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }
}

function localDate(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function buildBars(dailySales: DashboardSummaryDto['dailySales'], start: Date, days: number) {
  const revenueByDate = new Map(dailySales.map((day) => [day.date.slice(0, 10), day.revenue]));
  const buckets = Array.from({ length: days }, (_, index) => {
    const date = new Date(start);
    date.setDate(start.getDate() + index);
    return {
      key: localDate(date),
      label: date.toLocaleDateString(undefined, { weekday: 'short' }),
      value: revenueByDate.get(localDate(date)) ?? 0,
    };
  });
  const max = Math.max(1, ...buckets.map((bucket) => bucket.value));
  return buckets.map((bucket) => ({ label: bucket.label, value: bucket.value, pct: (bucket.value / max) * 100 }));
}
