import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { Pos } from './pos';
import { PosApi } from '../../core/api/pos-api';
import { CartStore } from '../../core/cart/cart.store';
import { InvoiceService } from '../../core/invoice/invoice.service';
import { Toast } from '../../core/ui/toast';
import { ProductDto, SaleDto, ShiftDto } from '../../core/models';

const product: ProductDto = {
  id: 1, sku: 'SKU-EXACT', name: 'Product', description: null, categoryId: 1,
  categoryName: 'General', defaultSupplierId: null, costPrice: 1, salePrice: 10,
  taxRate: 0, stockQuantity: 10, minStockThreshold: 1, isActive: true,
  isLowStock: false, primaryImageUrl: null,
};
const shift: ShiftDto = {
  id: 1, cashierId: 1, startTime: '2026-10-23T00:00:00Z', endTime: null,
  openingCash: 100, expectedCash: 100, closingCash: null, cashVariance: null, status: 'Open',
};
const sale: SaleDto = {
  id: 1, invoiceNumber: 'INV-TEST', saleDate: '2026-10-23T00:00:00Z',
  subtotal: 12, discountAmount: 0, taxAmount: 0, totalAmount: 12,
  paymentMethod: 'Cash', status: 'Completed', items: [], customerId: null,
  customerName: null, couponCode: null, loyaltyPointsEarned: 0, loyaltyPointsRedeemed: 0,
  cashTendered: 20, changeDue: 8,
};

describe('POS checkout and scanning', () => {
  let pos: Pos;
  let cart: CartStore;
  let api: { createSale: ReturnType<typeof vi.fn>; productBySku: ReturnType<typeof vi.fn>; products: ReturnType<typeof vi.fn> };
  let toast: { show: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    api = {
      createSale: vi.fn().mockReturnValue(of(sale)),
      productBySku: vi.fn().mockReturnValue(of(product)),
      products: vi.fn().mockReturnValue(of({ items: [product], totalPages: 1 })),
    };
    toast = { show: vi.fn() };
    TestBed.configureTestingModule({ providers: [
      CartStore,
      { provide: PosApi, useValue: api },
      { provide: Toast, useValue: toast },
      { provide: InvoiceService, useValue: {} },
    ] });
    cart = TestBed.inject(CartStore);
    pos = TestBed.runInInjectionContext(() => new Pos());
    pos.shift.set(shift);
  });

  it('sends cash tender and displays the server change rather than cached preview change', () => {
    cart.add(product); // Cached item costs 10; the server returned authoritative total 12.
    pos.cashTendered = 20;
    pos.checkout();

    expect(api.createSale.mock.calls[0][3].cashTendered).toBe(20);
    expect(pos.lastChangeDue()).toBe(8);
    expect(pos.lastSale()?.totalAmount).toBe(12);
    expect(cart.count()).toBe(0);
  });

  it('keeps the same idempotency key across a failed response and unchanged retry', () => {
    cart.add(product);
    pos.cashTendered = 20;
    api.createSale.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })))
      .mockReturnValueOnce(of(sale));

    pos.checkout();
    expect(cart.count()).toBe(1);
    pos.checkout();

    expect(api.createSale.mock.calls[0][2]).toBe(api.createSale.mock.calls[1][2]);
    expect(cart.count()).toBe(0);
  });

  it('uses a new idempotency key if cash tender changes after a rejected attempt', () => {
    cart.add(product);
    api.createSale.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
    pos.cashTendered = 10;
    pos.checkout();
    pos.cashTendered = 20;
    pos.checkout();

    expect(api.createSale.mock.calls[0][2]).not.toBe(api.createSale.mock.calls[1][2]);
    expect(cart.count()).toBe(1);
  });

  it('does not send stale cash input for a card sale', () => {
    cart.add(product);
    pos.payment = 'Card';
    pos.cashTendered = 20;
    api.createSale.mockReturnValue(of({ ...sale, paymentMethod: 'Card', cashTendered: null, changeDue: null }));
    pos.checkout();
    expect(api.createSale.mock.calls[0][3].cashTendered).toBeNull();
    expect(pos.lastChangeDue()).toBeNull();
  });

  it('normalizes scanner input and queries exact SKU independently of catalog pages', () => {
    pos.barcode = '  sku-exact  ';
    pos.scanBarcode();
    expect(api.productBySku).toHaveBeenCalledWith('SKU-EXACT');
    expect(api.products).not.toHaveBeenCalled();
    expect(cart.count()).toBe(1);
    expect(pos.barcode).toBe('');
  });

  it('shows an unknown scan and leaves the cart unchanged', () => {
    api.productBySku.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    pos.barcode = 'UNKNOWN';
    pos.scanBarcode();
    expect(cart.count()).toBe(0);
    expect(toast.show).toHaveBeenCalledWith('No product found for barcode/SKU UNKNOWN.', 'warn');
  });

  it('previews a small-value coupon without a negative final-line allocation', () => {
    for (let id = 1; id <= 4; id++) cart.add({ ...product, id, salePrice: 0.01 });
    pos.coupons.set([{
      id: 1, code: 'HALF', name: 'Half', isPercentage: true, value: 50,
      startsAt: '2020-01-01T00:00:00Z', endsAt: '2099-01-01T00:00:00Z',
      isActive: true, maxRedemptions: null, redemptionCount: 0,
    }]);
    pos.couponCode = 'HALF';
    expect(pos.promotionPreview()).toEqual({ subtotal: 0.04, discount: 0.02, tax: 0, total: 0.02 });
  });
});
