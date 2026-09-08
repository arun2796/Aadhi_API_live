using AadhiCrackers.Application.Validators;
using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Inventory;
using AadhiCrackers.Contracts.Orders;
using FluentAssertions;
using Xunit;

namespace AadhiCrackers.Application.Tests;

public class ValidationTests
{
    [Fact]
    public void LoginValidator_WithValidEmailAndPassword_ShouldPass()
    {
        var validator = new LoginRequestValidator();
        var request = new LoginRequest { Email = "test@aadhicrackers.com", Password = "Password@123" };
        var result = validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void LoginValidator_WithInvalidEmail_ShouldFail()
    {
        var validator = new LoginRequestValidator();
        var request = new LoginRequest { Email = "not-an-email", Password = "Pass" };
        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void CreateProductValidator_WithNegativePrice_ShouldFail()
    {
        var validator = new CreateProductRequestValidator();
        var req = new CreateProductRequest
        {
            SKU = "TEST-001",
            Name = "Test Sparkler",
            Description = "Test desc",
            CategoryId = Guid.NewGuid(),
            Price = -50m
        };
        var result = validator.Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Price");
    }

    [Fact]
    public void CreateOrderValidator_WithEmptyItems_ShouldFail()
    {
        var validator = new CreateOrderRequestValidator();
        var req = new CreateOrderRequest
        {
            ShippingAddress = new Domain.ValueObjects.Address("Arun Kumar", "9876543210", "123 Main St", null, "Coimbatore", "TN", "641012"),
            Items = new List<CreateOrderItemRequest>()
        };
        var result = validator.Validate(req);
        result.IsValid.Should().BeFalse();
    }


}
