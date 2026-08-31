using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AadhiCrackers.Infrastructure.Tests;

public class InfrastructureDbContextTests
{
    [Fact]
    public async Task CanAddAndRetrieveProductWithMoneyConverter()
    {
        var options = new DbContextOptionsBuilder<AadhiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new AadhiDbContext(options);

        var cat = new Category { Name = "Gift Boxes", Slug = "gift-boxes" };
        context.Categories.Add(cat);

        var product = new Product
        {
            SKU = "GB-TEST-01",
            Name = "Deluxe Sparkler Box",
            Slug = "deluxe-sparkler-box",
            Description = "Test Box",
            CategoryId = cat.Id,
            Price = Money.FromDecimal(1999.50m),
            CostPrice = Money.FromDecimal(1200m),
            StockQuantity = 100
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var retrieved = await context.Products.FirstOrDefaultAsync(p => p.SKU == "GB-TEST-01");
        retrieved.Should().NotBeNull();
        retrieved!.Price.ToDecimal().Should().Be(1999.50m);
        retrieved.Price.AmountMinor.Should().Be(199950);
    }
}
