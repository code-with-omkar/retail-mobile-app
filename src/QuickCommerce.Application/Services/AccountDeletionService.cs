using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Application.Services;

public sealed class AccountDeletionService(
    IAccountStore accounts,
    IAccountDeletionStore deletion,
    IPasswordHashing hashing,
    ICurrentUser currentUser,
    TimeProvider clock) : IAccountDeletionService
{
    public async Task<AccountResult<bool>> DeleteAsync(DeleteAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out var userId) || !currentUser.Roles.Contains("Customer"))
        {
            return AccountResult<bool>.Unauthorized("Authentication is required.");
        }

        if (!string.Equals(request.Confirmation?.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase))
        {
            return AccountResult<bool>.Invalid("Type DELETE to confirm.", ["Type DELETE to confirm."]);
        }

        if (await accounts.FindActiveCustomerByIdAsync(userId, cancellationToken) is null)
        {
            return AccountResult<bool>.NotFound("Account not found.");
        }

        // An account with a password must prove it; one without (phone or Google) has no password to ask for.
        var hash = await accounts.GetPasswordHashAsync(userId, cancellationToken);
        if (hash is not null && (string.IsNullOrEmpty(request.Password) || !hashing.Verify(hash, request.Password)))
        {
            return AccountResult<bool>.Invalid("Your password is incorrect.", ["Your password is incorrect."]);
        }

        return await deletion.DeleteCustomerAsync(userId, clock.GetUtcNow().UtcDateTime, cancellationToken) switch
        {
            DeleteOutcome.Deleted => AccountResult<bool>.Ok(true),
            DeleteOutcome.HasOpenOrders => AccountResult<bool>.Conflict("Finish or cancel your open orders first. An order that is still being prepared, delivered or refunded keeps the account open."),
            _ => AccountResult<bool>.NotFound("Account not found.")
        };
    }
}
