namespace QuickCommerce.Application.Interfaces;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    Guid? OrganizationId { get; }
    Guid? StoreId { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<string> Permissions { get; }
}