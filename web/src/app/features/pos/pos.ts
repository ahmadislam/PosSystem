import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { CartStore } from '../../core/cart/cart.store';
import { allocateDiscount } from '../../core/cart/discount-allocation';
import { apiError } from '../../core/http/api-error';
import { InvoiceService } from '../../core/invoice/invoice.service';
import { CategoryDto, CouponDto, CustomerDto, LoyaltyRulesDto, PaymentMethod, ProductDto, SaleDto, ShiftDto, lineTotals, money } from '../../core/models';
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
  barcode = '';
  categoryId: number | null = null;
  payment: PaymentMethod = 'Cash';
  cashTendered: number | null = null;
  paymentProvider = '';
  paymentReference = '';
  private checkoutFingerprint = '';
  private checkoutKey = '';
  readonly page = signal(1);
  readonly totalPages = signal(1);
  readonly categories = signal<CategoryDto[]>([]);
  readonly customers = signal<CustomerDto[]>([]);
  readonly coupons = signal<CouponDto[]>([]);
  readonly loyaltyRules = signal<LoyaltyRulesDto>({ earnPointsPerCurrencyUnit: 1, redemptionPointsPerCurrencyUnit: 100 });
  customerId: number | null = null;
  couponCode = '';
  loyaltyPointsToRedeem = 0;
  readonly products = signal<ProductDto[]>([]);
  readonly shift = signal<ShiftDto | null>(null);
  readonly busy = signal(false);
  readonly lastSale = signal<SaleDto | null>(null);
  readonly lastChangeDue = signal<number | null>(null);

  ngOnInit(): void {
    this.api.categories().subscribe({ next: (c) => this.categories.set(c) });
    this.api.customers().subscribe({ next: (c) => this.customers.set(c.filter((customer) => customer.isActive)) });
    this.api.coupons().subscribe({ next: (c) => this.coupons.set(c) });
    this.api.loyaltyRules().subscribe({ next: (rules) => this.loyaltyRules.set(rules) });
    this.refreshShift();
    this.loadProducts();
  }

  loadProducts(): void {
    this.api
      .products({
        search: this.search.trim() || undefined,
        categoryId: this.categoryId,
        page: this.page(),
        pageSize: 24,
      })
      .subscribe({
        next: (page) => {
          this.products.set(page.items.filter((p) => p.isActive));
          this.totalPages.set(Math.max(page.totalPages, 1));
        },
        error: (e) => this.toast.show(apiError(e), 'err'),
      });
  }

  searchProducts(): void {
    this.page.set(1);
    this.loadProducts();
  }

  movePage(delta: number): void {
    this.page.update((page) => Math.max(1, Math.min(this.totalPages(), page + delta)));
    this.loadProducts();
  }

  scanBarcode(): void {
    const barcode = this.barcode.trim().toUpperCase();
    if (!barcode) return;
    this.api.productBySku(barcode).subscribe({
      next: (product) => {
        const message = this.cart.add(product);
        if (message) this.toast.show(message, 'warn');
        else this.toast.show(`${product.sku} added to cart.`);
        this.barcode = '';
      },
      error: (e) => this.toast.show(e.status === 404
        ? `No product found for barcode/SKU ${barcode}.` : apiError(e), e.status === 404 ? 'warn' : 'err'),
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
    if (this.busy()) return;
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
    const totals = this.promotionPreview();
    const total = totals.total;
    const payment = this.payment;
    const cashTendered = payment === 'Cash' ? this.cashTendered : null;
    const paymentReference = payment === 'Cash' ? null : this.paymentReference.trim();
    const paymentProvider = payment === 'Cash' ? null : this.paymentProvider.trim();
    if (payment !== 'Cash' && !paymentReference) {
      this.toast.show('Enter a payment reference for card or online payment.', 'warn');
      return;
    }
    if (payment === 'Cash' && (!Number.isFinite(cashTendered) || (cashTendered ?? -1) < total)) {
      this.toast.show('Cash tendered must be at least the sale total.', 'warn');
      return;
    }
    const payload = {
      payment: this.payment,
      customerId: this.customerId,
      couponCode: this.couponCode.trim().toUpperCase() || null,
      loyaltyPointsToRedeem: this.loyaltyPointsToRedeem,
      cashTendered,
      items: lines.map((line) => ({
        productId: line.product.id,
        quantity: line.quantity,
        discountAmount: line.discountAmount,
      })),
    };
    const fingerprint = JSON.stringify(payload);
    if (fingerprint !== this.checkoutFingerprint) {
      this.checkoutFingerprint = fingerprint;
      this.checkoutKey = crypto.randomUUID();
    }
    this.busy.set(true);
    this.api
      .createSale(payment, payload.items, this.checkoutKey, {
        customerId: this.customerId,
        couponCode: this.couponCode.trim().toUpperCase() || null,
        loyaltyPointsToRedeem: this.loyaltyPointsToRedeem,
        cashTendered,
        paymentProvider,
        paymentReference,
      })
      .subscribe({
        next: (sale) => {
          this.checkoutFingerprint = '';
          this.checkoutKey = '';
          this.lastChangeDue.set(sale.changeDue);
          this.cart.clear();
          this.cashTendered = null;
          this.paymentProvider = '';
          this.paymentReference = '';
          this.couponCode = '';
          this.loyaltyPointsToRedeem = 0;
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

  downloadInvoice(): void {
    const sale = this.lastSale();
    if (sale) this.invoice.download(sale);
  }

  changeDue(): number {
    if (this.payment !== 'Cash' || !Number.isFinite(this.cashTendered)) return 0;
    return Math.max(0, (this.cashTendered ?? 0) - this.promotionPreview().total);
  }

  promotionPreview(): { subtotal: number; discount: number; tax: number; total: number } {
    const lines = this.cart.lines();
    const base = lines.reduce((sum, line) =>
      sum + Math.max(0, line.product.salePrice * line.quantity - line.discountAmount), 0);
    const coupon = this.coupons().find((candidate) => candidate.code === this.couponCode.trim().toUpperCase());
    const couponDiscount = !coupon ? 0 : coupon.isPercentage
      ? Math.min(base, this.roundMoney(base * coupon.value / 100))
      : Math.min(base, coupon.value);
    const customer = this.customers().find((item) => item.id === this.customerId);
    const rate = this.loyaltyRules().redemptionPointsPerCurrencyUnit;
    const points = customer && this.loyaltyPointsToRedeem > 0 &&
      this.loyaltyPointsToRedeem <= customer.loyaltyPoints && this.loyaltyPointsToRedeem % rate === 0
      ? this.roundMoney(this.loyaltyPointsToRedeem / rate) : 0;
    const loyaltyDiscount = Math.min(points, Math.max(0, base - couponDiscount));
    const additionalDiscount = couponDiscount + loyaltyDiscount;
    const additionalAllocations = allocateDiscount(lines.map((line) =>
      Math.max(0, line.product.salePrice * line.quantity - line.discountAmount)), additionalDiscount);
    const subtotal = lines.reduce((sum, line) => sum + line.product.salePrice * line.quantity, 0);
    let discount = 0;
    let tax = 0;
    lines.forEach((line, index) => {
      const extra = additionalAllocations[index];
      discount += line.discountAmount + extra;
      tax += lineTotals(line.product.salePrice, line.quantity, line.discountAmount + extra, line.product.taxRate).tax;
    });
    discount = this.roundMoney(discount);
    tax = this.roundMoney(tax);
    return { subtotal, discount, tax, total: this.roundMoney(subtotal - discount + tax) };
  }

  private roundMoney(amount: number): number {
    return Math.round((amount + Number.EPSILON) * 100) / 100;
  }
}
