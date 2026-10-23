import { CartStore } from './cart.store';
import { ProductDto } from '../models';

const product: ProductDto = {
  id: 1,
  sku: 'SKU-1',
  name: 'Product',
  description: null,
  categoryId: 1,
  categoryName: 'General',
  defaultSupplierId: null,
  costPrice: 1,
  salePrice: 10,
  taxRate: 7.5,
  stockQuantity: 10,
  minStockThreshold: 1,
  isActive: true,
  isLowStock: false,
  primaryImageUrl: null,
};

describe('CartStore', () => {
  it('reduces a manual discount when quantity is reduced and never yields a negative total', () => {
    const cart = new CartStore();
    expect(cart.add(product)).toBeNull();
    cart.setQuantity(product.id, 5);
    cart.setDiscount(product.id, 47);
    cart.setQuantity(product.id, 2);

    expect(cart.lines()[0].discountAmount).toBe(20);
    expect(cart.preview().total).toBe(0);
    expect(cart.preview().tax).toBe(0);
  });

  it('ignores non-finite numeric discounts', () => {
    const cart = new CartStore();
    cart.add(product);
    cart.setDiscount(product.id, Number.NaN);

    expect(cart.lines()[0].discountAmount).toBe(0);
    expect(cart.preview().total).toBe(10.75);
  });
});
