using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;

namespace QuickCommerce.Application.DependencyInjection;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>();
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
