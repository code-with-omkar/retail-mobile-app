using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>The one rule for "does a store deliver here", so saved addresses, the public check and checkout cannot disagree.</summary>
public static class ServiceabilityRules
{
    /// <summary>A store covers a point when the point is within its delivery radius.</summary>
    public static bool StoreCovers(Store store, double latitude, double longitude) =>
        StoreSelectionService.DistanceKm(latitude, longitude, store.Latitude, store.Longitude) <= store.ServiceRadiusKm;
}

public sealed class ServiceabilityService(ICommerceStore data) : IServiceabilityService
{
    public async Task<IReadOnlyList<ServiceabilityResponse>> CheckAsync(IReadOnlyList<GeoPoint> points, Guid? organizationId, CancellationToken cancellationToken = default)
    {
        if (points.Count == 0)
        {
            return [];
        }

        var stores = (await data.GetStoresAsync(cancellationToken))
            .Where(store => store.IsActive && (organizationId is null || store.OrganizationId == organizationId))
            .ToArray();
        // A store that carries nothing cannot deliver anything, so it does not make a point serviceable.
        var carrying = (await data.GetStoreIdsCarryingProductsAsync(cancellationToken)).ToHashSet();

        return points.Select(point => Check(point, stores, carrying)).ToArray();
    }

    private static ServiceabilityResponse Check(GeoPoint point, IReadOnlyList<Store> stores, IReadOnlySet<Guid> carrying)
    {
        var distances = stores
            .Select(store => (Store: store, Distance: StoreSelectionService.DistanceKm(point.Latitude, point.Longitude, store.Latitude, store.Longitude)))
            .ToArray();
        var covering = distances.Where(entry => entry.Distance <= entry.Store.ServiceRadiusKm).ToArray();

        if (covering.Length == 0)
        {
            return new ServiceabilityResponse(false, ServiceabilityReasons.OutsideServiceArea, distances.Length == 0 ? null : Math.Round(distances.Min(entry => entry.Distance), 1));
        }

        var serving = covering.Where(entry => carrying.Contains(entry.Store.Id)).ToArray();
        return serving.Length == 0
            ? new ServiceabilityResponse(false, ServiceabilityReasons.NoStoreAvailable, Math.Round(covering.Min(entry => entry.Distance), 1))
            : new ServiceabilityResponse(true, ServiceabilityReasons.Serviceable, Math.Round(serving.Min(entry => entry.Distance), 1));
    }
}
