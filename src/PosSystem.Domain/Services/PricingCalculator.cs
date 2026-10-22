namespace PosSystem.Domain.Services;

public static class PricingCalculator
{
    /// <summary>
    /// Gross = unit price x quantity. Tax is charged on (gross - discount).
    /// taxRatePercent is a percentage (17 = 17%).
    /// </summary>
    public static (decimal Gross, decimal Tax, decimal Total) CalculateLine(
        decimal unitPrice, int quantity, decimal discountAmount, decimal taxRatePercent)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        if (unitPrice < 0 || discountAmount < 0 || taxRatePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Amounts cannot be negative.");

        var gross = unitPrice * quantity;
        var net = gross - discountAmount;
        if (net < 0) throw new ArgumentException("Discount cannot exceed the line amount.", nameof(discountAmount));

        var tax = Math.Round(net * taxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return (gross, tax, net + tax);
    }
}
