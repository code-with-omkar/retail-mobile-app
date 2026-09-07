using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record UserContext(
    Guid UserId,
    Guid OrganizationId,
    Guid? StoreId,
    Role Role);