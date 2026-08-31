using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Finance;
using AadhiCrackers.Contracts.Inventory;
using AadhiCrackers.Contracts.Orders;
using FluentValidation;

namespace AadhiCrackers.Application.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("A valid email address is required.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).WithMessage("Password must be at least 6 characters.");
    }
}

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^[6-9]\d{9}$").WithMessage("Enter a valid 10-digit Indian phone number.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).WithMessage("Passwords do not match.");
    }
}

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.SKU).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Price must be greater than 0.");
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinOrderQuantity).GreaterThan(0);
        RuleFor(x => x.MaxOrderQuantity).GreaterThanOrEqualTo(x => x.MinOrderQuantity);
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0);
    }
}

public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.ShippingAddress).NotNull();
        RuleFor(x => x.ShippingAddress.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShippingAddress.Phone).NotEmpty().Matches(@"^[6-9]\d{9}$").WithMessage("A valid 10-digit phone number is required.");
        RuleFor(x => x.ShippingAddress.AddressLine1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ShippingAddress.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShippingAddress.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShippingAddress.PostalCode).NotEmpty().Matches(@"^\d{6}$").WithMessage("A valid 6-digit Indian PIN code is required.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must have at least one product.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}

public class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("A clear reason for stock adjustment is required.");
    }
}

public class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.UnitPrice).GreaterThan(0);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}

public class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
