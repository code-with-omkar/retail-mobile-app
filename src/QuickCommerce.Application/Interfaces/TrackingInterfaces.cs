using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>What the partner app sends while it is delivering.</summary>
public sealed record ShareLocationRequest(double Latitude, double Longitude);

/// <summary>What the customer sees: how far the delivery partner is and about how long. Never the coordinates.</summary>
public sealed record TrackingResponse(bool Available, double? DistanceKm, int? EtaMinutes, DateTime? UpdatedAt, string? PartnerName);

public sealed record TrackedOrder(Guid OrderId, Guid UserId, Guid? PartnerId, OrderStatus Status, double Latitude, double Longitude);

public interface ITrackingStore
{
    Task<TrackedOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the order's latest position. Positions older than a day (left by deliveries that never finished) are removed at the same time.</summary>
    Task SaveLocationAsync(Guid orderId, Guid partnerId, double latitude, double longitude, DateTime now, CancellationToken cancellationToken = default);

    Task<DeliveryLocation?> GetLocationAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task DeleteLocationAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public enum TrackingStatus { Succeeded, Forbidden, NotFound, Invalid, WrongState }

public sealed record TrackingResult<T>(TrackingStatus Status, T? Value = default, string? Message = null);

public interface ITrackingService
{
    /// <summary>The assigned partner shares their position while the order is out for delivery (about every 20 seconds). WrongState tells the app to stop.</summary>
    Task<TrackingResult<bool>> ShareAsync(Guid orderId, ShareLocationRequest request, CancellationToken cancellationToken = default);

    /// <summary>The customer's own order: distance and ETA while it is out for delivery and a recent position exists; otherwise "not available".</summary>
    Task<TrackingResult<TrackingResponse>> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
}
