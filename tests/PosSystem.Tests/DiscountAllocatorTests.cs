using PosSystem.Domain.Services;

namespace PosSystem.Tests;

public class DiscountAllocatorTests
{
    [Fact]
    public void Half_off_four_penny_lines_never_creates_negative_remainder()
    {
        var amounts = new[] { .01m, .01m, .01m, .01m };
        var result = DiscountAllocator.Allocate(.02m, amounts);

        Assert.Equal(.02m, result.Sum());
        Assert.All(result, amount => Assert.InRange(amount, 0m, .01m));
    }

    [Fact]
    public void Zero_value_last_line_cannot_receive_discount()
    {
        var result = DiscountAllocator.Allocate(.01m, [.01m, .01m, 0m]);

        Assert.Equal(.01m, result.Sum());
        Assert.Equal(0m, result[2]);
    }

    [Fact]
    public void Full_discount_exhausts_each_lines_capacity()
    {
        decimal[] capacities = [10m, 0m, .01m, 3.14m];

        Assert.Equal(capacities, DiscountAllocator.Allocate(capacities.Sum(), capacities));
    }

    [Fact]
    public void Zero_discount_and_empty_basket_are_safe()
    {
        Assert.Empty(DiscountAllocator.Allocate(0m, []));
        Assert.Equal(new[] { 0m, 0m }, DiscountAllocator.Allocate(0m, [0m, 10m]));
    }

    [Fact]
    public void Allocations_preserve_total_precision_and_line_bounds_across_baskets()
    {
        var random = new Random(7023);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var capacities = Enumerable.Range(0, random.Next(1, 40))
                .Select(_ => random.Next(0, 10_001) / 100m).ToArray();
            var discount = random.Next(0, checked((int)(capacities.Sum() * 100)) + 1) / 100m;

            var result = DiscountAllocator.Allocate(discount, capacities);

            Assert.Equal(discount, result.Sum());
            for (var index = 0; index < capacities.Length; index++)
            {
                Assert.InRange(result[index], 0m, capacities[index]);
                Assert.Equal(result[index], decimal.Round(result[index], 2));
            }
        }
    }

    [Theory]
    [InlineData(-.01, .10)]
    [InlineData(.11, .10)]
    [InlineData(.005, .10)]
    [InlineData(.01, -.10)]
    [InlineData(.01, .105)]
    public void Invalid_amounts_are_rejected(double discount, double capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiscountAllocator.Allocate((decimal)discount, [(decimal)capacity]));
    }
}
