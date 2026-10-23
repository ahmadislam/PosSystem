import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { AuthService } from '../../core/auth/auth.service';
import { apiError } from '../../core/http/api-error';
import { InvoiceService } from '../../core/invoice/invoice.service';
import { PaymentMethod, SaleDto, SaleReturnDto, money } from '../../core/models';
import { Toast } from '../../core/ui/toast';

@Component({
  selector: 'app-sales',
  imports: [FormsModule, DatePipe],
  templateUrl: './sales.html',
  styleUrl: './sales.scss',
})
export class Sales implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);
  private readonly invoice = inject(InvoiceService);
  readonly auth = inject(AuthService);
  readonly money = money;
  from = '';
  to = '';
  readonly sales = signal<SaleDto[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly totalPages = signal(1);
  readonly downloadingReport = signal(false);
  readonly returningSale = signal<SaleDto | null>(null);
  readonly returnHistory = signal<SaleReturnDto[]>([]);
  readonly returnQuantities = signal<Record<number, number>>({});
  readonly loadingReturnHistory = signal(false);
  readonly submittingReturn = signal(false);
  refundMethod: PaymentMethod = 'Cash';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.listSales(this.from || undefined, this.to || undefined, this.page(), 50).subscribe({
      next: (p) => {
        const pages = Math.max(p.totalPages, 1);
        if (this.page() > pages) {
          this.page.set(pages);
          this.load();
          return;
        }
        this.sales.set(p.items);
        this.total.set(p.totalCount);
        this.totalPages.set(pages);
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  filter(): void {
    this.page.set(1);
    this.load();
  }

  movePage(delta: number): void {
    this.page.update((page) => Math.max(1, Math.min(this.totalPages(), page + delta)));
    this.load();
  }

  print(s: SaleDto): void {
    this.invoice.print(s);
  }

  downloadReport(): void {
    this.downloadingReport.set(true);
    this.api.salesReport(this.from || undefined, this.to || undefined).subscribe({
      next: (file) => {
        const url = URL.createObjectURL(file);
        const link = document.createElement('a');
        link.href = url;
        link.download = `sales-report-${this.from || 'last-30-days'}-${this.to || 'today'}.xlsx`;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        this.downloadingReport.set(false);
      },
      error: (e) => {
        this.downloadingReport.set(false);
        this.toast.show(apiError(e), 'err');
      },
    });
  }

  openReturns(sale: SaleDto): void {
    this.returningSale.set(sale);
    this.returnHistory.set([]);
    this.returnQuantities.set({});
    this.loadingReturnHistory.set(true);
    this.submittingReturn.set(false);
    this.refundMethod = sale.paymentMethod;
    this.api.saleReturns(sale.id).subscribe({
      next: (returns) => {
        this.returnHistory.set(returns);
        this.returnQuantities.set(Object.fromEntries(sale.items.map((item) => [item.saleItemId, 0])));
        this.loadingReturnHistory.set(false);
      },
      error: (e) => {
        this.toast.show(apiError(e), 'err');
        this.closeReturns();
      },
    });
  }

  returnedQuantity(saleItemId: number): number {
    return this.returnHistory().reduce(
      (total, current) =>
        total + current.items.reduce((itemTotal, item) => itemTotal + (item.saleItemId === saleItemId ? item.quantity : 0), 0),
      0,
    );
  }

  setReturnQuantity(saleItemId: number, quantity: number, max: number): void {
    const parsed = Number.isFinite(quantity) ? Math.trunc(quantity) : 0;
    this.returnQuantities.update((current) => ({
      ...current,
      [saleItemId]: Math.max(0, Math.min(parsed, max)),
    }));
  }

  closeReturns(): void {
    this.returningSale.set(null);
    this.returnHistory.set([]);
    this.returnQuantities.set({});
    this.loadingReturnHistory.set(false);
    this.submittingReturn.set(false);
  }

  submitReturn(): void {
    const sale = this.returningSale();
    if (!sale) return;
    const items = sale.items
      .map((item) => ({ saleItemId: item.saleItemId, quantity: this.returnQuantities()[item.saleItemId] ?? 0 }))
      .filter((item) => item.quantity > 0);
    if (items.length === 0) {
      this.toast.show('Enter a quantity to return.', 'warn');
      return;
    }

    this.submittingReturn.set(true);
    this.api.returnSale(sale.id, this.refundMethod, items).subscribe({
      next: (result) => {
        this.toast.show(`Return recorded. Refund ${money(result.totalAmount)}.`);
        this.closeReturns();
        this.load();
      },
      error: (e) => {
        this.submittingReturn.set(false);
        this.toast.show(apiError(e), 'err');
      },
    });
  }
}
