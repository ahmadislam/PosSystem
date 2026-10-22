import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import { ProductDto, SaleDto, money } from '../../core/models';
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

  readonly sales = signal<SaleDto[]>([]);
  readonly lowStock = signal<ProductDto[]>([]);
  readonly revenue = signal(0);
  readonly count = signal(0);
  readonly bars = signal<{ label: string; value: number; pct: number }[]>([]);

  ngOnInit(): void {
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - 6);
    const fromS = from.toISOString().slice(0, 10);
    const toS = to.toISOString().slice(0, 10);

    this.api.listSales(fromS, toS, 1, 100).subscribe({
      next: (page) => {
        this.sales.set(page.items);
        this.count.set(page.totalCount);
        this.revenue.set(page.items.reduce((s, x) => s + x.totalAmount, 0));
        this.bars.set(buildBars(page.items, from, 7));
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });

    this.api.products({ lowStockOnly: true, pageSize: 20 }).subscribe({
      next: (p) => this.lowStock.set(p.items),
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }
}

function buildBars(sales: SaleDto[], start: Date, days: number) {
  const buckets = Array.from({ length: days }, (_, i) => {
    const d = new Date(start);
    d.setDate(start.getDate() + i);
    return { key: d.toISOString().slice(0, 10), label: d.toLocaleDateString(undefined, { weekday: 'short' }), value: 0 };
  });
  for (const sale of sales) {
    const key = sale.saleDate.slice(0, 10);
    const b = buckets.find((x) => x.key === key);
    if (b) b.value += sale.totalAmount;
  }
  const max = Math.max(1, ...buckets.map((b) => b.value));
  return buckets.map((b) => ({ label: b.label, value: b.value, pct: (b.value / max) * 100 }));
}
