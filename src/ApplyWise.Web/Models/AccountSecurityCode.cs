using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public sealed class AccountSecurityCode
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public AccountSecurityAction Action { get; set; }
    public required byte[] ProtectedCode { get; set; }
    public int FailedAttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public IdentityUser? User { get; set; }
}

public enum AccountSecurityAction
{
    ChangePassword,
    DeleteAccount,
    ConfirmEmail,
    ResetPassword
}
