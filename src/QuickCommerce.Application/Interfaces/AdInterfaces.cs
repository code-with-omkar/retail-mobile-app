using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>An ad as the portal shows it.</summary>
/// <param name="Status">Live, Scheduled (not started), Ended or Off.</param>
public sealed record AdResponse(
    Guid Id,
    string Placement,
    string? Title,
    string ImageUrl,
    string TargetType,
    string? Target,
    Guid? StoreId,
    DateTime StartsAt,
    DateTime? EndsAt,
    bool IsActive,
    int SortOrder,
    long Views,
    long Taps,
    string Status);

/// <summary>What an administrator sends to make or change an ad. Times are UTC.</summary>
public sealed record SaveAdRequest(
    string Placement,
    string ImageUrl,
    string? Title = null,
    string TargetType = AdTargets.None,
    string? Target = null,
    Guid? StoreId = null,
    DateTime? StartsAt = null,
    DateTime? EndsAt = null,
    bool IsActive = true,
    int SortOrder = 0);

/// <summary>What the customer app needs to draw and open an ad.</summary>
public sealed record AdPublic(Guid Id, string Placement, string? Title, string ImageUrl, string TargetType, string? Target);

public interface IAdStore
{
    Task<IReadOnlyList<Ad>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<Ad?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Ad ad, CancellationToken cancellationToken = default);
    Task SaveAsync(Ad ad, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Whether the store is one of the organisation's.</summary>
    Task<bool> StoreBelongsAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>The ads running now at a place, for everyone or for one store: ads for all stores plus that store's own.</summary>
    Task<IReadOnlyList<Ad>> LiveAsync(string placement, Guid? storeId, DateTime now, int take, CancellationToken cancellationToken = default);

    /// <summary>Adds one to the counter in a single statement, so concurrent views are not lost. False when the ad does not exist.</summary>
    Task<bool> CountViewAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CountTapAsync(Guid id, CancellationToken cancellationToken = default);
}

public enum AdServiceStatus { Succeeded, Forbidden, NotFound, Invalid }

public sealed record AdResult<T>(AdServiceStatus Status, T? Value = default, string? Message = null);

public interface IAdService
{
    /// <summary>Administrators only.</summary>
    Task<AdResult<IReadOnlyList<AdResponse>>> ListAsync(CancellationToken cancellationToken = default);

    Task<AdResult<AdResponse>> CreateAsync(SaveAdRequest request, CancellationToken cancellationToken = default);

    Task<AdResult<AdResponse>> UpdateAsync(Guid id, SaveAdRequest request, CancellationToken cancellationToken = default);

    Task<AdResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>What the app shows at a place. An unknown place gives an empty list. Open to everyone, signed in or not.</summary>
    Task<IReadOnlyList<AdPublic>> ForPlacementAsync(string? placement, Guid? storeId, CancellationToken cancellationToken = default);

    Task RecordViewAsync(Guid id, CancellationToken cancellationToken = default);

    Task RecordTapAsync(Guid id, CancellationToken cancellationToken = default);
}
