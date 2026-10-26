using System.Security.Cryptography;
using System.Text;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>What happens to an order at each delivery step: pure rules, shared by the SQL and in-memory stores so both behave the same.</summary>
public static class DeliveryRules
{
    public const int MaxOtpFailures = 5;
    public static readonly TimeSpan OtpLockout = TimeSpan.FromMinutes(15);

    public static string NewOtp() => RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");

    /// <summary>What the partner must collect: the order total for cash on delivery, nothing for an online order.</summary>
    public static decimal CashToCollect(Order order) => order.PaymentMethod == PaymentMethods.CashOnDelivery ? order.TotalAmount : 0;

    public static DeliveryChangeStatus Assign(Order order, Guid partnerId, string otp, DateTime now)
    {
        if (order.DeliveryPartnerId == partnerId && order.Status is OrderStatus.Ready or OrderStatus.OutForDelivery)
        {
            return DeliveryChangeStatus.Unchanged;
        }

        if (order.Status != OrderStatus.Ready)
        {
            return DeliveryChangeStatus.WrongState;
        }

        order.DeliveryPartnerId = partnerId;
        order.AssignedAt = now;
        order.DeliveryOtp ??= otp;
        order.OtpFailures = 0;
        order.OtpLockedUntil = null;
        return DeliveryChangeStatus.Succeeded;
    }

    public static DeliveryChangeStatus PickUp(Order order, Guid partnerId, DateTime now)
    {
        if (order.DeliveryPartnerId != partnerId)
        {
            return DeliveryChangeStatus.NotFound;
        }

        if (order.Status == OrderStatus.OutForDelivery)
        {
            return DeliveryChangeStatus.Unchanged;
        }

        if (order.Status != OrderStatus.Ready)
        {
            return DeliveryChangeStatus.WrongState;
        }

        order.Status = OrderStatus.OutForDelivery;
        order.PickedUpAt = now;
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.OutForDelivery, ChangedAt = now });
        return DeliveryChangeStatus.Succeeded;
    }

    /// <summary>A wrong code or amount is a change too (failures are counted), so the caller saves in every case except Unchanged.</summary>
    public static DeliveryChangeStatus Deliver(Order order, Guid partnerId, string otp, decimal cashCollected, DateTime now)
    {
        if (order.DeliveryPartnerId != partnerId)
        {
            return DeliveryChangeStatus.NotFound;
        }

        if (order.Status == OrderStatus.Delivered)
        {
            return DeliveryChangeStatus.Unchanged;
        }

        if (order.Status != OrderStatus.OutForDelivery)
        {
            return DeliveryChangeStatus.WrongState;
        }

        if (order.OtpLockedUntil.HasValue && order.OtpLockedUntil.Value > now)
        {
            return DeliveryChangeStatus.Locked;
        }

        var expected = Encoding.UTF8.GetBytes(order.DeliveryOtp ?? string.Empty);
        var given = Encoding.UTF8.GetBytes(otp ?? string.Empty);
        if (expected.Length == 0 || expected.Length != given.Length || !CryptographicOperations.FixedTimeEquals(expected, given))
        {
            order.OtpFailures++;
            if (order.OtpFailures >= MaxOtpFailures)
            {
                order.OtpFailures = 0;
                order.OtpLockedUntil = now + OtpLockout;
                return DeliveryChangeStatus.Locked;
            }

            return DeliveryChangeStatus.WrongOtp;
        }

        if (cashCollected != CashToCollect(order))
        {
            return DeliveryChangeStatus.WrongCash;
        }

        order.Status = OrderStatus.Delivered;
        order.DeliveredAt = now;
        order.CashCollected = cashCollected;
        order.OtpFailures = 0;
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Delivered, ChangedAt = now });
        return DeliveryChangeStatus.Succeeded;
    }
}

public sealed class DeliveryService(
    IDeliveryStore store,
    ICurrentUserContextResolver contextResolver,
    TimeProvider time,
    ITrackingStore? tracking = null) : IDeliveryService
{
    private static readonly TimeSpan RecentlyDelivered = TimeSpan.FromHours(24);

    public async Task<DeliveryResult<IReadOnlyList<DeliveryPartnerResponse>>> ListPartnersAsync(Guid? storeId, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        var scope = Staff(context, storeId);
        if (scope is null)
        {
            return DeliveryResult<IReadOnlyList<DeliveryPartnerResponse>>.Fail(DeliveryResultStatus.Forbidden, "Only store staff or an administrator can list delivery partners.");
        }

        if (scope == Guid.Empty)
        {
            return DeliveryResult<IReadOnlyList<DeliveryPartnerResponse>>.Fail(DeliveryResultStatus.Invalid, "Choose a store.");
        }

        var partners = await store.ListPartnersAsync(context!.OrganizationId, scope.Value, cancellationToken);
        var counts = await store.CountActiveAsync(partners.Select(partner => partner.Id).ToArray(), cancellationToken);
        IReadOnlyList<DeliveryPartnerResponse> list = partners
            .OrderBy(partner => partner.DisplayName)
            .Select(partner => new DeliveryPartnerResponse(partner.Id, partner.DisplayName, partner.PhoneNumber, counts.GetValueOrDefault(partner.Id)))
            .ToArray();
        return DeliveryResult<IReadOnlyList<DeliveryPartnerResponse>>.Ok(list);
    }

    public async Task<DeliveryResult<OrderResponse>> AssignAsync(Guid orderId, AssignDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin) || (context.Role == Role.StoreStaff && !context.StoreId.HasValue))
        {
            return DeliveryResult<OrderResponse>.Fail(DeliveryResultStatus.Forbidden, "Only store staff or an administrator can assign deliveries.");
        }

        var scopeStore = context.Role == Role.StoreStaff ? context.StoreId : null;
        var change = await store.AssignAsync(orderId, context.OrganizationId, scopeStore, request.PartnerId, DeliveryRules.NewOtp(), time.GetUtcNow().UtcDateTime, cancellationToken);
        return change.Status switch
        {
            DeliveryChangeStatus.Succeeded or DeliveryChangeStatus.Unchanged => DeliveryResult<OrderResponse>.Ok(await Summary(change.Order!, cancellationToken)),
            DeliveryChangeStatus.NotFound => DeliveryResult<OrderResponse>.Fail(DeliveryResultStatus.NotFound, "Order not found in your store."),
            DeliveryChangeStatus.PartnerInvalid => DeliveryResult<OrderResponse>.Fail(DeliveryResultStatus.Invalid, "That person is not an active delivery partner of this store."),
            DeliveryChangeStatus.WrongState => DeliveryResult<OrderResponse>.Fail(DeliveryResultStatus.WrongState, "Only an order that is ready can be given to a delivery partner."),
            _ => DeliveryResult<OrderResponse>.Fail(DeliveryResultStatus.Conflict, "The order changed before this could be done. Try again.")
        };
    }

    public async Task<DeliveryResult<IReadOnlyList<PartnerDeliveryResponse>>> MineAsync(CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.DeliveryPartner)
        {
            return DeliveryResult<IReadOnlyList<PartnerDeliveryResponse>>.Fail(DeliveryResultStatus.Forbidden, "Only a delivery partner has deliveries.");
        }

        var rows = await store.ListForPartnerAsync(context.UserId, time.GetUtcNow().UtcDateTime - RecentlyDelivered, cancellationToken);
        IReadOnlyList<PartnerDeliveryResponse> list = rows.Select(row => ForPartner(row.Order, row.Store)).ToArray();
        return DeliveryResult<IReadOnlyList<PartnerDeliveryResponse>>.Ok(list);
    }

    public async Task<DeliveryResult<PartnerDeliveryResponse>> PickUpAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.DeliveryPartner)
        {
            return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.Forbidden, "Only a delivery partner can pick up a delivery.");
        }

        var change = await store.PickUpAsync(orderId, context.UserId, time.GetUtcNow().UtcDateTime, cancellationToken);
        return await Finish(change, context.UserId, cancellationToken);
    }

    public async Task<DeliveryResult<PartnerDeliveryResponse>> DeliverAsync(Guid orderId, DeliverRequest request, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.DeliveryPartner)
        {
            return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.Forbidden, "Only a delivery partner can complete a delivery.");
        }

        if (string.IsNullOrWhiteSpace(request.Otp) || request.Otp.Length != 4 || !request.Otp.All(char.IsAsciiDigit) || request.CashCollected < 0)
        {
            return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.Invalid, "Enter the four digits the customer gives you.");
        }

        var change = await store.DeliverAsync(orderId, context.UserId, request.Otp, request.CashCollected, time.GetUtcNow().UtcDateTime, cancellationToken);
        if (tracking is not null && change.Status is DeliveryChangeStatus.Succeeded or DeliveryChangeStatus.Unchanged)
        {
            // The delivery is over: the partner's last position is erased.
            await tracking.DeleteLocationAsync(orderId, cancellationToken);
        }

        return await Finish(change, context.UserId, cancellationToken);
    }

    private async Task<DeliveryResult<PartnerDeliveryResponse>> Finish(DeliveryChange change, Guid partnerId, CancellationToken cancellationToken)
    {
        switch (change.Status)
        {
            case DeliveryChangeStatus.Succeeded or DeliveryChangeStatus.Unchanged:
                var order = change.Order!;
                var storeRow = (await store.ListForPartnerAsync(partnerId, time.GetUtcNow().UtcDateTime - RecentlyDelivered, cancellationToken)).First(row => row.Order.Id == order.Id).Store;
                return DeliveryResult<PartnerDeliveryResponse>.Ok(ForPartner(order, storeRow));
            case DeliveryChangeStatus.NotFound:
                return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.NotFound, "Delivery not found.");
            case DeliveryChangeStatus.WrongState:
                return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.WrongState, "This delivery is not at that step.");
            case DeliveryChangeStatus.WrongOtp:
                return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.WrongOtp, "That code is not right. Ask the customer again.");
            case DeliveryChangeStatus.Locked:
                return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.Locked, "Too many wrong codes. Wait 15 minutes and try again.");
            case DeliveryChangeStatus.WrongCash:
                return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.Invalid, "The cash collected must equal the amount to collect.");
            default:
                return DeliveryResult<PartnerDeliveryResponse>.Fail(DeliveryResultStatus.Conflict, "The order changed before this could be done. Try again.");
        }
    }

    /// <summary>The store a staff member works in. Staff always use their own; an administrator must name one. Null = not allowed, empty id = no store given.</summary>
    private static Guid? Staff(UserContext? context, Guid? requested)
    {
        if (context is null)
        {
            return null;
        }

        if (context.Role == Role.StoreStaff)
        {
            return context.StoreId;
        }

        return context.Role is Role.Admin or Role.ApplicationAdmin ? requested ?? Guid.Empty : null;
    }

    private async Task<OrderResponse> Summary(Order order, CancellationToken cancellationToken)
    {
        var partner = order.DeliveryPartnerId.HasValue ? await store.GetPartnerAsync(order.DeliveryPartnerId.Value, cancellationToken) : null;
        return new OrderResponse(order.Id, order.OrderNumber, order.UserId, order.StoreId, order.TotalAmount, order.Status, order.DeliveryAddress, order.Latitude, order.Longitude, order.CreatedAt,
            [], [], order.SubtotalAmount, order.DeliveryFee, order.HandlingFee, order.PaymentMethod, order.ReceiverName, order.ReceiverPhone,
            DeliveryPartnerName: partner?.DisplayName, DeliveryPartnerPhone: partner?.PhoneNumber);
    }

    private static PartnerDeliveryResponse ForPartner(Order order, Store store) => new(
        order.Id,
        order.OrderNumber,
        order.Status,
        store.Name,
        store.PhoneNumber,
        order.DeliveryAddress,
        order.Latitude,
        order.Longitude,
        order.ReceiverName,
        order.ReceiverPhone,
        order.PaymentMethod,
        order.TotalAmount,
        DeliveryRules.CashToCollect(order),
        order.CashCollected,
        order.Items.Select(item => new OrderItemResponse(item.ProductId, item.ProductNameSnapshot, item.UnitPrice, item.Quantity, item.TotalPrice, item.VariantId, item.VariantLabelSnapshot)).ToArray(),
        order.CreatedAt,
        order.PickedUpAt,
        order.DeliveredAt);
}
