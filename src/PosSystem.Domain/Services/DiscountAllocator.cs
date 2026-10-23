namespace PosSystem.Domain.Services;

/// <summary>Allocates a monetary discount proportionally without creating negative or oversized line discounts.</summary>
public static class DiscountAllocator
{
    public static IReadOnlyList<decimal> Allocate(decimal discount, IReadOnlyList<decimal> eligibleAmounts)
    {
        if (discount < 0 || decimal.Round(discount, 2) != discount)
            throw new ArgumentOutOfRangeException(nameof(discount), "Discount must be a nonnegative monetary amount.");
        if (eligibleAmounts.Any(amount => amount < 0 || decimal.Round(amount, 2) != amount))
            throw new ArgumentOutOfRangeException(nameof(eligibleAmounts), "Eligible amounts must be nonnegative monetary amounts.");

        var total = eligibleAmounts.Sum();
        if (discount > total)
            throw new ArgumentOutOfRangeException(nameof(discount), "Discount cannot exceed the eligible amount.");
        var allocations = new decimal[eligibleAmounts.Count];
        if (total == 0 || discount == 0) return allocations;

        decimal cumulativeEligible = 0, cumulativeAllocated = 0;
        for (var index = 0; index < eligibleAmounts.Count; index++)
        {
            cumulativeEligible += eligibleAmounts[index];
            // Round cumulative targets rather than individual shares, so the remainder
            // never becomes negative. Consecutive rounded targets differ by at most
            // this line's cent-valued capacity when discount <= total.
            var target = Math.Round(discount * (cumulativeEligible / total), 2, MidpointRounding.AwayFromZero);
            allocations[index] = target - cumulativeAllocated;
            cumulativeAllocated = target;
        }
        return allocations;
    }
}
