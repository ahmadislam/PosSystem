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
  readonly refunds = signal(0);
  readonly netRevenue = signal(0);
  readonly count = signal(0);
  readonly bars = signal<{ label: string; value: number; pct: number }[]>([]);
  readonly monthlyBars = signal<{ label: string; value: number; pct: number }[]>([]);
  readonly topProducts = signal<DashboardSummaryDto['topSellingProducts']>([]);
  readonly categoryBars = signal<{ label: string; value: number; quantity: number; pct: number }[]>([]);

  ngOnInit(): void {
    const end = new Date();
    const monthStart = new Date(end.getFullYear(), end.getMonth() - 11, 1);
    const weekStart = new Date(end);
    weekStart.setDate(weekStart.getDate() - 6);

    this.api.dashboardSummary(localDate(monthStart), localDate(end)).subscribe({
      next: (summary) => {
        this.sales.set(summary.recentSales);
        this.count.set(summary.totalSales);
        this.revenue.set(summary.grossSales);
        this.refunds.set(summary.refunds);
        this.netRevenue.set(summary.netRevenue);
        this.lowStockCount.set(summary.lowStockCount);
        this.lowStock.set(summary.lowStockProducts);
        this.bars.set(buildDailyBars(summary.dailySales, weekStart, 7));
        this.monthlyBars.set(buildMonthlyBars(summary.monthlyRevenue));
        this.topProducts.set(summary.topSellingProducts);
        this.categoryBars.set(buildCategoryBars(summary.salesByCategory));
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

function buildDailyBars(dailySales: DashboardSummaryDto['dailySales'], start: Date, days: number) {
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

function buildMonthlyBars(months: DashboardSummaryDto['monthlyRevenue']) {
  const revenueByMonth = new Map(months.map((month) => [month.month.slice(0, 7), month.revenue]));
  const now = new Date();
  const buckets = Array.from({ length: 12 }, (_, index) => {
    const date = new Date(Date.UTC(now.getFullYear(), now.getMonth() - 11 + index, 1));
    const key = date.toISOString().slice(0, 7);
    return {
      label: date.toLocaleDateString(undefined, { month: 'short', year: '2-digit', timeZone: 'UTC' }),
      value: revenueByMonth.get(key) ?? 0,
    };
  });
  const max = Math.max(1, ...buckets.map((bucket) => bucket.value));
  return buckets.map((bucket) => ({ ...bucket, pct: Math.max(bucket.value > 0 ? 3 : 0, (bucket.value / max) * 100) }));
}

function buildCategoryBars(categories: DashboardSummaryDto['salesByCategory']) {
  const max = Math.max(1, ...categories.map((category) => category.revenue));
  return categories.map((category) => ({
    label: category.categoryName,
    value: category.revenue,
    quantity: category.quantitySold,
    pct: Math.max(category.revenue > 0 ? 3 : 0, (category.revenue / max) * 100),
  }));
}
