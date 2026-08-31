using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.Exceptions;
using AadhiCrackers.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AadhiCrackers.Domain.Tests;

public class OrderStateMachineTests
{
    [Fact]
    public void ValidTransitions_ShouldSucceedAndRecordHistory()
    {
        var order = new Order();
        order.OrderStatus.Should().Be(OrderStatus.Pending);

        // Pending -> Confirmed
        order.ChangeStatus(OrderStatus.Confirmed, "Payment authorized", "System");
        order.OrderStatus.Should().Be(OrderStatus.Confirmed);
        order.StatusHistories.Should().HaveCount(1);

        // Confirmed -> Processing
        order.ChangeStatus(OrderStatus.Processing, "Order assigned to Sivakasi warehouse", "Admin");
        order.OrderStatus.Should().Be(OrderStatus.Processing);

        // Processing -> Packed
        order.ChangeStatus(OrderStatus.Packed, "Packed in corrugated box with safety seals", "WarehouseStaff");
        order.OrderStatus.Should().Be(OrderStatus.Packed);
        order.FulfillmentStatus.Should().Be(FulfillmentStatus.Packed);

        // Packed -> Shipped
        order.ChangeStatus(OrderStatus.Shipped, "Dispatched via Express Courier", "Logistics");
        order.OrderStatus.Should().Be(OrderStatus.Shipped);
        order.FulfillmentStatus.Should().Be(FulfillmentStatus.Shipped);

        // Shipped -> OutForDelivery
        order.ChangeStatus(OrderStatus.OutForDelivery, "Out with local delivery agent", "DeliveryHub");
        order.OrderStatus.Should().Be(OrderStatus.OutForDelivery);

        // OutForDelivery -> Delivered
        order.ChangeStatus(OrderStatus.Delivered, "Handed over to customer", "DeliveryAgent");
        order.OrderStatus.Should().Be(OrderStatus.Delivered);
        order.FulfillmentStatus.Should().Be(FulfillmentStatus.Delivered);

        order.StatusHistories.Should().HaveCount(6);
    }

    [Fact]
    public void InvalidTransitions_ShouldThrowInvalidOrderStateTransitionException()
    {
        var order = new Order();

        // Trying to go directly from Pending to Delivered should throw
        var act = () => order.ChangeStatus(OrderStatus.Delivered);
        act.Should().Throw<InvalidOrderStateTransitionException>();

        // Transition to Confirmed first
        order.ChangeStatus(OrderStatus.Confirmed);

        // Trying to transition from Confirmed directly to Delivered should throw
        var act2 = () => order.ChangeStatus(OrderStatus.Delivered);
        act2.Should().Throw<InvalidOrderStateTransitionException>();
    }

    [Fact]
    public void CancelledOrder_CannotTransitionToAnyState()
    {
        var order = new Order();
        order.ChangeStatus(OrderStatus.Cancelled, "Customer requested cancellation", "Customer");

        var act = () => order.ChangeStatus(OrderStatus.Processing);
        act.Should().Throw<InvalidOrderStateTransitionException>();
    }
}

public class PromotionTests
{
    [Fact]
    public void PercentageDiscount_ShouldCalculateCorrectlyWithCap()
    {
        var promo = new Promotion
        {
            Code = "FESTIVE20",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m, // 20%
            MinimumOrderAmount = Money.FromDecimal(1000m),
            MaximumDiscount = Money.FromDecimal(500m),
            IsActive = true
        };

        // Subtotal below minimum
        var below = promo.CalculateDiscount(Money.FromDecimal(800m));
        below.ToDecimal().Should().Be(0m);

        // Subtotal ₹2,000 -> 20% is ₹400 (under ₹500 cap)
        var valid = promo.CalculateDiscount(Money.FromDecimal(2000m));
        valid.ToDecimal().Should().Be(400m);

        // Subtotal ₹4,000 -> 20% is ₹800 (exceeds ₹500 cap -> capped at ₹500)
        var capped = promo.CalculateDiscount(Money.FromDecimal(4000m));
        capped.ToDecimal().Should().Be(500m);
    }
}
