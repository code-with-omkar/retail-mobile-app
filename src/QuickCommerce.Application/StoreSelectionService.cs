using QuickCommerce.Domain;

namespace QuickCommerce.Application;

public interface IStoreSelectionService
{
    Store? FindNearest(double latitude, double longitude, IReadOnlyCollection<Guid> variantIds, IReadOnlyCollection<Store> stores, IReadOnlyCollection<StoreVariantInventory> inventory);
}

public sealed class StoreSelectionService : IStoreSelectionService
{
    public Store? FindNearest(double latitude, double longitude, IReadOnlyCollection<Guid> variantIds, IReadOnlyCollection<Store> stores, IReadOnlyCollection<StoreVariantInventory> inventory)
    {
        return stores
            .Where(store => store.IsActive && DistanceKm(latitude, longitude, store.Latitude, store.Longitude) <= store.ServiceRadiusKm)
            .Where(store => variantIds.All(variantId => inventory.Any(stock => stock.StoreId == store.Id && stock.VariantId == variantId && stock.AvailableQuantity > 0)))
            .OrderBy(store => DistanceKm(latitude, longitude, store.Latitude, store.Longitude))
            .FirstOrDefault();
    }

    public static double DistanceKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusKm = 6371;
        var latitudeDelta = DegreesToRadians(latitude2 - latitude1);
        var longitudeDelta = DegreesToRadians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2) + Math.Cos(DegreesToRadians(latitude1)) * Math.Cos(DegreesToRadians(latitude2)) * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
