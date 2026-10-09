using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;

namespace QuickCommerce.Application.DependencyInjection;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>();
        // The API registers validated settings from configuration first; these are the fallback defaults.
        services.TryAddSingleton(new DeliverySettings());
        services.TryAddSingleton<IDeliveryEstimator, DeliveryEstimator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new AddressSettings());
        services.TryAddSingleton(new PricingSettings());
        services.TryAddSingleton(new PaymentSettings());
        services.AddScoped<ICustomerPaymentService, PaymentService>();
        services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        services.AddScoped<IPaymentMaintenance, PaymentMaintenance>();
        services.AddScoped<ICampaignAdminService, CampaignAdminService>();
        services.AddScoped<IServiceabilityService, ServiceabilityService>();
        services.AddScoped<IAddressService, AddressService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IOrderOperationsService, OrderOperationsService>();
        services.AddScoped<ICustomerOrderService, CustomerOrderService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IStoreSelectionService, StoreSelectionService>();
        return services;
    }
}
