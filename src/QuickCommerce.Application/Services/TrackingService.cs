using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>The maths of "how far, how long": straight-line distance and a steady city speed.</summary>
public static class TrackingMath
{
    public const double SpeedKmPerHour = 20;

    /// <summary>A position older than this is not shown (the partner's phone may be off or offline).</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);

    public static int EtaMinutes(double distanceKm) => Math.Max(1, (int)Math.Ceiling(distanceKm / SpeedKmPerHour * 60));
}

/// <summary>
/// Delivery tracking. The partner shares a position; the customer reads distance and ETA only. The latest position is all that is kept and
/// it is erased as soon as the order is no longer out for delivery.
/// </summary>
public sealed class TrackingService(
    ITrackingStore store,
    ICurrentUserContextResolver contextResolver,
    IDeliveryLookup? partners = null,
    TimeProvider? timeProvider = null) : ITrackingService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<TrackingResult<bool>> ShareAsync(Guid orderId, ShareLocationRequest request, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.DeliveryPartner)
        {
            return new(TrackingStatus.Forbidden, Message: "Only a delivery partner can share a position.");
        }

        if (!double.IsFinite(request.Latitude) || !double.IsFinite(request.Longitude) || request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
        {
            return new(TrackingStatus.Invalid, Message: "That position is not valid.");
        }

        var order = await store.GetOrderAsync(orderId, cancellationToken);
        // Someone else's order looks like there is none.
        if (order is null || order.PartnerId != context.UserId)
        {
            return new(TrackingStatus.NotFound, Message: "Delivery not found.");
        }

        if (order.Status != OrderStatus.OutForDelivery)
        {
            await store.DeleteLocationAsync(orderId, cancellationToken);
            return new(TrackingStatus.WrongState, Message: "This delivery is not out for delivery, so there is nothing to share.");
        }

        await store.SaveLocationAsync(orderId, context.UserId, request.Latitude, request.Longitude, clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(TrackingStatus.Succeeded, true);
    }

    public async Task<TrackingResult<TrackingResponse>> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return new(TrackingStatus.Forbidden, Message: "Sign in to track your order.");
        }

        var order = await store.GetOrderAsync(orderId, cancellationToken);
        if (order is null || order.UserId != context.UserId)
        {
            return new(TrackingStatus.NotFound, Message: "Order not found.");
        }

        var none = new TrackingResponse(false, null, null, null, null);
        if (order.Status != OrderStatus.OutForDelivery)
        {
            await store.DeleteLocationAsync(orderId, cancellationToken);
            return new(TrackingStatus.Succeeded, none);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var location = await store.GetLocationAsync(orderId, cancellationToken);
        if (location is null || now - location.UpdatedAt > TrackingMath.Fresh)
        {
            return new(TrackingStatus.Succeeded, none);
        }

        var distance = Math.Round(StoreSelectionService.DistanceKm(location.Latitude, location.Longitude, order.Latitude, order.Longitude), 1);
        var partner = order.PartnerId.HasValue && partners is not null ? await partners.GetPartnerAsync(order.PartnerId.Value, cancellationToken) : null;
        var first = string.IsNullOrWhiteSpace(partner?.FirstName) ? partner?.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : partner!.FirstName;
        return new(TrackingStatus.Succeeded, new TrackingResponse(true, distance, TrackingMath.EtaMinutes(distance), DateTime.SpecifyKind(location.UpdatedAt, DateTimeKind.Utc), first));
    }
}
