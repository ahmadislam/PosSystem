import { allocateDiscount } from './discount-allocation';

describe('promotion allocation', () => {
  it('allocates a two-cent coupon over four one-cent lines without a negative remainder', () => {
    expect(allocateDiscount([0.01, 0.01, 0.01, 0.01], 0.02)).toEqual([0.01, 0, 0.01, 0]);
  });

  it('does not move a rounded remainder onto a zero-value final line', () => {
    expect(allocateDiscount([0.01, 0.01, 0.01, 0], 0.02)).toEqual([0.01, 0, 0.01, 0]);
  });

  it('respects discounted capacities and reconciles full and zero discounts', () => {
    expect(allocateDiscount([0, 1.75, 2.25], 4)).toEqual([0, 1.75, 2.25]);
    expect(allocateDiscount([0, 0], 0)).toEqual([0, 0]);
    expect(allocateDiscount([1, 2], 0)).toEqual([0, 0]);
    expect(() => allocateDiscount([1, 2], 3.01)).toThrow();
  });

  it('keeps all capacities bounded and reconciles discounts across penny baskets', () => {
    for (let count = 1; count <= 20; count++) {
      const capacities = Array.from({ length: count }, (_, index) => (index % 4) / 100);
      const totalCents = capacities.reduce((sum, value) => sum + Math.round(value * 100), 0);
      for (let discountCents = 0; discountCents <= totalCents; discountCents++) {
        const allocations = allocateDiscount(capacities, discountCents / 100);
        expect(allocations.reduce((sum, value) => sum + Math.round(value * 100), 0)).toBe(discountCents);
        allocations.forEach((value, index) => {
          expect(value).toBeGreaterThanOrEqual(0);
          expect(value).toBeLessThanOrEqual(capacities[index]);
        });
      }
    }
  });
});
