using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record UserContext(
    Guid UserId,
    Guid OrganizationId,
    Guid? StoreId,
    Role Role,
    StaffCategory? StaffCategory = null,
    IReadOnlyList<Guid>? AssignedStoreIds = null)
{
    /// <summary>
    /// The stores whose orders this member of staff sees and works on: a store manager has every store assigned to them (and the primary one),
    /// anyone else only the primary store. Empty without a primary store.
    /// </summary>
    public IReadOnlySet<Guid> OrderStoreIds
    {
        get
        {
            if (StoreId is not { } primary)
            {
                return new HashSet<Guid>();
            }

            var stores = new HashSet<Guid> { primary };
            if (Role == QuickCommerce.Domain.Role.StoreStaff && StaffCategory == QuickCommerce.Domain.StaffCategory.StoreManager)
            {
                stores.UnionWith(AssignedStoreIds ?? []);
            }

            return stores;
        }
    }
}