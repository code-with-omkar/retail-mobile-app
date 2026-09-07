using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Validators;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator validator = new();

    [Fact]
    public async Task Valid_order_request_passes_validation()
    {
        var result = await validator.ValidateAsync(new CreateOrderRequest(
            Guid.NewGuid(),
            19.076,
            72.8777,
            "12 Marine Drive",
            [new OrderLineRequest(Guid.NewGuid(), 1)]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Invalid_order_request_reports_request_errors()
    {
        var result = await validator.ValidateAsync(new CreateOrderRequest(
            Guid.Empty,
            91,
            181,
            string.Empty,
            [new OrderLineRequest(Guid.Empty, 0)]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.UserId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.DeliveryAddress));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.Latitude));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.Longitude));
        Assert.Contains(result.Errors, error => error.PropertyName.Contains(nameof(CreateOrderRequest.Items), StringComparison.Ordinal));
    }
}
