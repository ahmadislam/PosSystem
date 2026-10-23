namespace PosSystem.Domain.Services;

public record ReturnAmounts(decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal TotalAmount);

public static class ReturnCalculator
{
    public static ReturnAmounts Calculate(
        decimal unitPrice,
        int originalQuantity,
        decimal originalDiscount,
        decimal originalTax,
        decimal originalTotal,
        int quantity,
        int previouslyReturnedQuantity,
        decimal previouslyReturnedSubtotal,
        decimal previouslyReturnedDiscount,
        decimal previouslyReturnedTax,
        decimal previouslyReturnedTotal)
    {
        if (originalQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(originalQuantity));
        if (previouslyReturnedQuantity < 0 || previouslyReturnedQuantity > originalQuantity)
            throw new ArgumentOutOfRangeException(nameof(previouslyReturnedQuantity));
        if (quantity <= 0 || quantity > originalQuantity - previouslyReturnedQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Return quantity exceeds the remaining quantity.");

        var remainingQuantity = originalQuantity - previouslyReturnedQuantity;
        var returnsLastQuantity = quantity == remainingQuantity;
        var remainingSubtotal = unitPrice * originalQuantity - previouslyReturnedSubtotal;
        var remainingDiscount = originalDiscount - previouslyReturnedDiscount;
        var remainingTax = originalTax - previouslyReturnedTax;
        var subtotal = returnsLastQuantity
            ? remainingSubtotal
            : Allocate(remainingSubtotal, quantity, remainingQuantity);
        var discount = returnsLastQuantity
            ? remainingDiscount
            : Allocate(remainingDiscount, quantity, remainingQuantity);
        var tax = returnsLastQuantity
            ? remainingTax
            : Allocate(remainingTax, quantity, remainingQuantity);
        var total = returnsLastQuantity
            ? originalTotal - previouslyReturnedTotal
            : subtotal - discount + tax;

        return new ReturnAmounts(subtotal, discount, tax, total);
    }

    private static decimal Allocate(decimal remainingAmount, int quantity, int remainingQuantity) =>
        decimal.Round(remainingAmount * quantity / remainingQuantity, 2, MidpointRounding.AwayFromZero);
}
