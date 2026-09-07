namespace QuickCommerce.Application.DTOs;

public sealed record CategoryResponse(Guid Id, string Name, Guid? ParentCategoryId, bool IsActive);

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string UnitOfMeasure,
    Guid CategoryId,
    string? ImageUrl,
    bool IsActive);

public sealed record StoreResponse(
    Guid Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    double ServiceRadiusKm,
    bool IsActive);
