using PosSystem.Domain.Services;

namespace PosSystem.Tests;

public class PricingCalculatorTests
{
    [Fact]
    public void Tax_is_charged_after_discount()
    {
        var (gross, tax, total) = PricingCalculator.CalculateLine(100m, 2, 20m, 10m);
        Assert.Equal(200m, gross);
        Assert.Equal(18m, tax);
        Assert.Equal(198m, total);
    }

    [Fact]
    public void Tax_is_rounded_to_two_decimals()
    {
        var (gross, tax, total) = PricingCalculator.CalculateLine(9.99m, 3, 0m, 17m);
        Assert.Equal(29.97m, gross);
        Assert.Equal(5.09m, tax);
        Assert.Equal(35.06m, total);
    }

    [Fact]
    public void Discount_larger_than_line_amount_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PricingCalculator.CalculateLine(10m, 1, 11m, 0m));
    }

    [Fact]
    public void Zero_quantity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PricingCalculator.CalculateLine(10m, 0, 0m, 0m));
    }
}
