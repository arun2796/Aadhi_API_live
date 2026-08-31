using AadhiCrackers.Contracts.Auth;
using AadhiCrackers.Contracts.Catalog;
using AadhiCrackers.Contracts.Common;
using AadhiCrackers.Contracts.Orders;
using FluentAssertions;
using Xunit;

namespace AadhiCrackers.Api.Tests;

public class ApiContractTests
{
    [Fact]
    public void ApiResponse_Ok_ShouldHaveSuccessTrue()
    {
        var response = ApiResponse<string>.Ok("Success Data", "Operation Successful", "corr-123");
        response.Success.Should().BeTrue();
        response.Data.Should().Be("Success Data");
        response.Message.Should().Be("Operation Successful");
        response.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void ApiResponse_Fail_ShouldHaveSuccessFalse()
    {
        var response = ApiResponse<string>.Fail("Error occurred", "corr-123");
        response.Success.Should().BeFalse();
        response.Message.Should().Be("Error occurred");
        response.Data.Should().BeNull();
    }

    [Fact]
    public void PagedResult_Calculation_ShouldBeAccurate()
    {
        var items = new List<string> { "A", "B", "C" };
        var paged = new PagedResult<string>(items, 25, 2, 10);

        paged.TotalCount.Should().Be(25);
        paged.PageNumber.Should().Be(2);
        paged.PageSize.Should().Be(10);
        paged.TotalPages.Should().Be(3);
        paged.HasPreviousPage.Should().BeTrue();
        paged.HasNextPage.Should().BeTrue();
    }
}
