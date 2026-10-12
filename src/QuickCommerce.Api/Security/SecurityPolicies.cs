namespace QuickCommerce.Api.Security;

public static class SecurityPolicies
{
    public const string Orders = "Orders";
    public const string StoreOrderOperations = "StoreOrderOperations";
    public const string AdminManagement = "AdminManagement";
    public const string AdminDataRead = "AdminDataRead";

    /// <summary>Offers for customers: administrators, and store staff for their own store.</summary>
    public const string OfferManagement = "OfferManagement";

    /// <summary>The delivery partner's own deliveries.</summary>
    public const string DeliveryPartner = "DeliveryPartner";
}
