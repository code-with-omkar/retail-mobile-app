namespace QuickCommerce.Application.Interfaces;

/// <summary>The organization a platform administrator asked to look at (header X-Organization-Id); null when none was asked for.</summary>
public interface IActingOrganization
{
    Guid? Requested { get; }
}

public sealed record OrganizationInfo(Guid Id, string Name, string? Code);

public interface IOrganizationDirectory
{
    /// <summary>Active organizations, by name.</summary>
    Task<IReadOnlyList<OrganizationInfo>> ListAsync(CancellationToken cancellationToken = default);
}
