namespace QuickCommerce.Application.DTOs;

/// <param name="Label">Home, Work or a name the customer chose.</param>
/// <param name="Line">The formatted address from the map or search.</param>
public sealed record CustomerAddressRequest(
    string Label,
    string Line,
    string? FlatOrBuilding,
    string? Landmark,
    double Latitude,
    double Longitude,
    string ReceiverName,
    string ReceiverPhone);

/// <param name="Serviceable">Computed on every read from today's stores, so a change in coverage shows at once.</param>
/// <param name="ServiceabilityReason">See <see cref="ServiceabilityReasons"/>.</param>
public sealed record CustomerAddressResponse(
    Guid Id,
    string Label,
    string Line,
    string FlatOrBuilding,
    string Landmark,
    double Latitude,
    double Longitude,
    string ReceiverName,
    string ReceiverPhone,
    bool IsDefault,
    bool Serviceable,
    string ServiceabilityReason);

/// <summary>Whether anyone can deliver to a point, and if not, why.</summary>
/// <param name="NearestDistanceKm">Distance to the nearest store that delivers there, or to the nearest store at all when none does; null when there are no stores.</param>
public sealed record ServiceabilityResponse(bool Serviceable, string Reason, double? NearestDistanceKm);

public static class ServiceabilityReasons
{
    public const string Serviceable = "Serviceable";

    /// <summary>No store's delivery radius covers the point.</summary>
    public const string OutsideServiceArea = "OutsideServiceArea";

    /// <summary>A store's radius covers the point, but no covering store carries any product.</summary>
    public const string NoStoreAvailable = "NoStoreAvailable";
}

public sealed record GeoPoint(double Latitude, double Longitude);
