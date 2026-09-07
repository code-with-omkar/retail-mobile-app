using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICurrentUserContextResolver
{
    Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default);
}