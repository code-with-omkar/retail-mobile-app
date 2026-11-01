using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public sealed record DeliveryPartnerResponse(Guid Id, string Name, string? Phone, int ActiveDeliveries);

public sealed record AssignDeliveryRequest(Guid PartnerId);

/// <param name="Otp">The four digits the customer gives at the door.</param>
/// <param name="CashCollected">Rupees collected. Cash on delivery: the order total. Online orders: 0.</param>
public sealed record DeliverRequest(string Otp, decimal CashCollected = 0);

/// <summary>An order as the partner who holds it sees it. The customer's contact and address are shared only with the partner assigned to the order.</summary>
public sealed record PartnerDeliveryResponse(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    string StoreName,
    string? StorePhone,
    string DeliveryAddress,
    double Latitude,
    double Longitude,
    string? ReceiverName,
    string? ReceiverPhone,
    string PaymentMethod,
    decimal TotalAmount,
    decimal CashToCollect,
    decimal? CashCollected,
    IReadOnlyList<OrderItemResponse> Items,
    DateTime CreatedAt,
    DateTime? PickedUpAt,
    DateTime? DeliveredAt);

public enum DeliveryChangeStatus
{
    Succeeded,

    /// <summary>Already in the state asked for; nothing was written.</summary>
    Unchanged,
    NotFound,
    WrongState,
    PartnerInvalid,
    WrongOtp,
    Locked,
    WrongCash,
    Conflict
}

public sealed record DeliveryChange(DeliveryChangeStatus Status, Order? Order = null);

public enum DeliveryResultStatus { Succeeded, NotFound, Forbidden, Invalid, WrongState, WrongOtp, Locked, Conflict }

public sealed record DeliveryResult<T>(DeliveryResultStatus Status, T? Value = default, string? Message = null)
{
    public static DeliveryResult<T> Ok(T value) => new(DeliveryResultStatus.Succeeded, value);
    public static DeliveryResult<T> Fail(DeliveryResultStatus status, string message) => new(status, default, message);
}

public interface IDeliveryService
{
    /// <summary>Active delivery partners of the staff member's store (an administrator names the store).</summary>
    Task<DeliveryResult<IReadOnlyList<DeliveryPartnerResponse>>> ListPartnersAsync(Guid? storeId, CancellationToken cancellationToken = default);

    /// <summary>Gives a Ready order to a partner of the same store. Giving it to the same partner again changes nothing.</summary>
    Task<DeliveryResult<OrderResponse>> AssignAsync(Guid orderId, AssignDeliveryRequest request, CancellationToken cancellationToken = default);

    /// <summary>The partner's own deliveries: those to do, and those delivered recently.</summary>
    Task<DeliveryResult<IReadOnlyList<PartnerDeliveryResponse>>> MineAsync(CancellationToken cancellationToken = default);

    Task<DeliveryResult<PartnerDeliveryResponse>> PickUpAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<DeliveryResult<PartnerDeliveryResponse>> DeliverAsync(Guid orderId, DeliverRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Partner details for the customer's view of an order.</summary>
public interface IDeliveryLookup
{
    Task<User?> GetPartnerAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IDeliveryStore : IDeliveryLookup
{
    Task<IReadOnlyList<User>> ListPartnersAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, int>> CountActiveAsync(IReadOnlyCollection<Guid> partnerIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the order to the partner when the order is Ready, belongs to the caller's scope (<paramref name="scopeStoreId"/> null = any store of the
    /// organization) and the partner is an active delivery partner of the order's store. <paramref name="otp"/> is kept only if the order has none yet.
    /// </summary>
    Task<DeliveryChange> AssignAsync(Guid orderId, Guid organizationId, Guid? scopeStoreId, Guid partnerId, string otp, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The partner's orders that are Ready or out for delivery, and those delivered since <paramref name="deliveredSince"/>, newest first, with their store.</summary>
    Task<IReadOnlyList<(Order Order, Store Store)>> ListForPartnerAsync(Guid partnerId, DateTime deliveredSince, CancellationToken cancellationToken = default);

    Task<DeliveryChange> PickUpAsync(Guid orderId, Guid partnerId, DateTime now, CancellationToken cancellationToken = default);
    Task<DeliveryChange> DeliverAsync(Guid orderId, Guid partnerId, string otp, decimal cashCollected, DateTime now, CancellationToken cancellationToken = default);
}
