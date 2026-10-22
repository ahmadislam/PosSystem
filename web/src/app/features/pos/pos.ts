import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { CartStore } from '../../core/cart/cart.store';
import { apiError } from '../../core/http/api-error';
import { InvoiceService } from '../../core/invoice/invoice.service';
import { CategoryDto, PaymentMethod, ProductDto, SaleDto, ShiftDto, lineTotals, money } from '../../core/models';
import { Toast } from '../../core/ui/toast';

@Component({
  selector: 'app-pos',
  imports: [FormsModule],
  templateUrl: './pos.html',
  styleUrl: './pos.scss',
})
export class Pos implements OnInit {
  readonly api = inject(PosApi);
  readonly cart = inject(CartStore);
  private readonly toast = inject(Toast);
  private readonly invoice = inject(InvoiceService);

  readonly money = money;
  readonly lineTotals = lineTotals;

  search = '';
  categoryId: number | null = null;
  payment: PaymentMethod = 'Cash';
  readonly categories = signal<CategoryDto[]>([]);
  readonly products = signal<ProductDto[]>([]);
  readonly shift = signal<ShiftDto | null>(null);
  readonly busy = signal(false);
  readonly lastSale = signal<SaleDto | null>(null);

  ngOnInit(): void {
    this.api.categories().subscribe({ next: (c) => this.categories.set(c) });
    this.refreshShift();
    this.loadProducts();
  }

  loadProducts(): void {
    this.api
      .products({ search: this.search.trim() || undefined, categoryId: this.categoryId, pageSize: 100 })
      .subscribe({
        next: (page) => this.products.set(page.items.filter((p) => p.isActive)),
        error: (e) => this.toast.show(apiError(e), 'err'),
      });
  }

  refreshShift(): void {
    this.api.currentShift().subscribe({
      next: (s) => this.shift.set(s),
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  add(p: ProductDto): void {
    const msg = this.cart.add(p);
    if (msg) this.toast.show(msg, 'warn');
  }

  checkout(): void {
    const shift = this.shift();
    if (!shift || shift.status !== 'Open') {
      this.toast.show('Open a shift before selling.', 'warn');
      return;
    }
    const lines = this.cart.lines();
    if (lines.length === 0) {
      this.toast.show('Cart is empty.', 'warn');
      return;
    }
    this.busy.set(true);
    this.api
      .createSale(
        this.payment,
        lines.map((l) => ({
          productId: l.product.id,
          quantity: l.quantity,
          discountAmount: l.discountAmount,
        })),
      )
      .subscribe({
        next: (sale) => {
          this.cart.clear();
          this.lastSale.set(sale);
          this.busy.set(false);
          this.toast.show(`Sale ${sale.invoiceNumber} recorded.`);
          this.loadProducts();
        },
        error: (e) => {
          this.busy.set(false);
          this.toast.show(apiError(e), 'err');
        },
      });
  }

  printReceipt(): void {
    const sale = this.lastSale();
    if (sale) this.invoice.print(sale);
  }
}
