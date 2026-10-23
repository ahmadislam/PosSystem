using PosSystem.Domain.Services;

namespace PosSystem.Tests;

public class ReturnCalculatorTests
{
    [Fact]
    public void Partial_return_allocates_discount_and_tax_proportionally()
    {
        var result = ReturnCalculator.Calculate(
            unitPrice: 100m,
            originalQuantity: 2,
            originalDiscount: 20m,
            originalTax: 18m,
            originalTotal: 198m,
            quantity: 1,
            previouslyReturnedQuantity: 0,
            previouslyReturnedSubtotal: 0m,
            previouslyReturnedDiscount: 0m,
            previouslyReturnedTax: 0m,
            previouslyReturnedTotal: 0m);

        Assert.Equal(new ReturnAmounts(100m, 10m, 9m, 99m), result);
    }

    [Fact]
    public void Final_returned_quantity_reconciles_rounding_remainders()
    {
        var first = ReturnCalculator.Calculate(0.10m, 3, 0.01m, 0.02m, 0.31m, 1, 0, 0m, 0m, 0m, 0m);
        var second = ReturnCalculator.Calculate(
            0.10m, 3, 0.01m, 0.02m, 0.31m, 1, 1,
            first.Subtotal, first.DiscountAmount, first.TaxAmount, first.TotalAmount);
        var final = ReturnCalculator.Calculate(
            0.10m, 3, 0.01m, 0.02m, 0.31m, 1, 2,
            first.Subtotal + second.Subtotal,
            first.DiscountAmount + second.DiscountAmount,
            first.TaxAmount + second.TaxAmount,
            first.TotalAmount + second.TotalAmount);

        Assert.Equal(0.30m, first.Subtotal + second.Subtotal + final.Subtotal);
        Assert.Equal(0.01m, first.DiscountAmount + second.DiscountAmount + final.DiscountAmount);
        Assert.Equal(0.02m, first.TaxAmount + second.TaxAmount + final.TaxAmount);
        Assert.Equal(0.31m, first.TotalAmount + second.TotalAmount + final.TotalAmount);
    }

    [Fact]
    public void Return_quantity_cannot_exceed_remaining_quantity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReturnCalculator.Calculate(10m, 3, 0m, 0m, 30m, 2, 2, 10m, 0m, 0m, 10m));
    }

    [Fact]
    public void Repeated_small_returns_never_allocate_more_than_the_original_refund()
    {
        var subtotal = 0m;
        var discount = 0m;
        var tax = 0m;
        var total = 0m;
        var returnedQuantity = 0;

        for (var i = 0; i < 5; i++)
        {
            var part = ReturnCalculator.Calculate(
                1m, 6, 5.97m, 0m, 0.03m, 1, returnedQuantity,
                subtotal, discount, tax, total);
            subtotal += part.Subtotal;
            discount += part.DiscountAmount;
            tax += part.TaxAmount;
            total += part.TotalAmount;
            returnedQuantity++;
        }

        var final = ReturnCalculator.Calculate(
            1m, 6, 5.97m, 0m, 0.03m, 1, returnedQuantity,
            subtotal, discount, tax, total);

        Assert.Equal(0.03m, total + final.TotalAmount);
        Assert.Equal(5.97m, discount + final.DiscountAmount);
    }
}
