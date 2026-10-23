/** Allocate a promotion in cents across eligible line capacities.
 * Cumulative rounded targets keep every allocation bounded and preserve the exact total,
 * including zero-value final lines. Matches Domain.Services.DiscountAllocator.
 */
export function allocateDiscount(capacities: readonly number[], discount: number): number[] {
  const toCents = (amount: number): bigint => {
    const cents = Math.round((amount + Number.EPSILON) * 100);
    if (!Number.isFinite(amount) || amount < 0 || !Number.isSafeInteger(cents)) {
      throw new Error('Discount allocation requires nonnegative amounts within supported precision.');
    }
    return BigInt(cents);
  };
  const capacityCents = capacities.map(toCents);
  const totalCapacity = capacityCents.reduce((sum, capacity) => sum + capacity, 0n);
  const discountCents = toCents(discount);
  if (discountCents > totalCapacity) throw new Error('Discount exceeds the eligible subtotal.');
  if (totalCapacity === 0n) return capacities.map(() => 0);

  let prefix = 0n;
  let previousTarget = 0n;
  return capacityCents.map((capacity) => {
    prefix += capacity;
    // Positive rational round-to-nearest with midpoint away from zero.
    const numerator = discountCents * prefix;
    const target = (2n * numerator + totalCapacity) / (2n * totalCapacity);
    const allocated = target - previousTarget;
    previousTarget = target;
    return Number(allocated) / 100;
  });
}
