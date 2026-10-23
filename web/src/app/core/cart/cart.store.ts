import { Injectable, computed, signal } from '@angular/core';
import { CartLine, ProductDto, lineTotals } from '../models';

@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly linesSig = signal<CartLine[]>([]);
  readonly lines = this.linesSig.asReadonly();
  readonly count = computed(() => this.linesSig().reduce((s, l) => s + l.quantity, 0));
  readonly preview = computed(() => {
    let subtotal = 0;
    let discount = 0;
    let tax = 0;
    let total = 0;
    for (const line of this.linesSig()) {
      const t = lineTotals(line.product.salePrice, line.quantity, line.discountAmount, line.product.taxRate);
      subtotal += t.gross;
      discount += line.discountAmount;
      tax += t.tax;
      total += t.total;
    }
    return { subtotal, discount, tax, total };
  });

  add(product: ProductDto): string | null {
    if (!product.isActive) return 'Product is inactive.';
    if (product.stockQuantity <= 0) return `${product.sku} is out of stock.`;

    const existing = this.linesSig().find((l) => l.product.id === product.id);
    const nextQty = (existing?.quantity ?? 0) + 1;
    if (nextQty > product.stockQuantity) return `Only ${product.stockQuantity} of ${product.sku} in stock.`;

    this.linesSig.update((lines) => {
      if (!existing) return [...lines, { product, quantity: 1, discountAmount: 0 }];
      return lines.map((l) => (l.product.id === product.id ? { ...l, quantity: nextQty } : l));
    });
    return null;
  }

  setQuantity(productId: number, quantity: number): void {
    if (!Number.isFinite(quantity)) return;
    this.linesSig.update((lines) =>
      lines.flatMap((l) => {
        if (l.product.id !== productId) return [l];
        const q = Math.max(0, Math.min(Math.floor(quantity), l.product.stockQuantity));
        return q === 0
          ? []
          : [{ ...l, quantity: q, discountAmount: clampMoney(l.discountAmount, l.product.salePrice * q) }];
      }),
    );
  }

  setDiscount(productId: number, discountAmount: number): void {
    this.linesSig.update((lines) =>
      lines.map((l) => {
        if (l.product.id !== productId) return l;
        const max = l.product.salePrice * l.quantity;
        return { ...l, discountAmount: clampMoney(discountAmount, max) };
      }),
    );
  }

  remove(productId: number): void {
    this.linesSig.update((lines) => lines.filter((l) => l.product.id !== productId));
  }

  clear(): void {
    this.linesSig.set([]);
  }
}

function clampMoney(value: number, maximum: number): number {
  if (!Number.isFinite(value)) return 0;
  const cents = Math.round(Math.max(0, Math.min(value, maximum)) * 100);
  return cents / 100;
}
