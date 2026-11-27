using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

/// <param name="Confirmation">The word DELETE, typed by the customer.</param>
/// <param name="Password">Required when the account has a password.</param>
public sealed record DeleteAccountRequest(string Confirmation, string? Password = null);

public enum DeleteOutcome { Deleted, NotFound, HasOpenOrders }

/// <summary>Erases a customer's personal data and closes the account, keeping order and payment records without the personal fields.</summary>
public interface IAccountDeletionStore
{
    Task<DeleteOutcome> DeleteCustomerAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default);
}

public interface IAccountDeletionService
{
    Task<AccountResult<bool>> DeleteAsync(DeleteAccountRequest request, CancellationToken cancellationToken = default);
}
