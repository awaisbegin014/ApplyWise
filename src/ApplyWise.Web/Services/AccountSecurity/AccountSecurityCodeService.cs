using System.Security.Cryptography;
using System.Text;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Email;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.AccountSecurity;

public sealed class AccountSecurityCodeService(
    ApplicationDbContext db,
    IApplicationEmailSender emailSender,
    IDataProtectionProvider dataProtectionProvider,
    IWorkspaceQuotaGate quotaGate,
    TimeProvider timeProvider) : IAccountSecurityCodeService
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AttemptBudgetWindow = TimeSpan.FromMinutes(15);

    public async Task<SecurityCodeIssueResult> IssueAsync(string userId, string email, AccountSecurityAction action,
        CancellationToken cancellationToken = default)
    {
        var prepared = await quotaGate.RunAsync(
            GetQuotaResource(action),
            userId,
            operationCancellationToken => PrepareIssueAsync(
                userId,
                action,
                operationCancellationToken),
            cancellationToken);
        if (!prepared.Result.Succeeded)
        {
            return prepared.Result;
        }

        try
        {
            await emailSender.SendAccountSecurityCodeAsync(email, action, prepared.Value!);
        }
        catch
        {
            await quotaGate.RunAsync(
                GetQuotaResource(action),
                userId,
                async operationCancellationToken =>
                {
                    var failedRecord = await db.AccountSecurityCodes.SingleOrDefaultAsync(
                        code => code.Id == prepared.CodeId,
                        operationCancellationToken);
                    if (failedRecord is not null)
                    {
                        db.AccountSecurityCodes.Remove(failedRecord);
                        await db.SaveChangesAsync(operationCancellationToken);
                    }

                    return true;
                },
                cancellationToken);
            throw;
        }

        return prepared.Result;
    }

    private async Task<PreparedSecurityCode> PrepareIssueAsync(
        string userId,
        AccountSecurityAction action,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var recent = await db.AccountSecurityCodes
            .Where(code => code.UserId == userId && code.Action == action)
            .OrderByDescending(code => code.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var attemptBudgetIsActive = recent is
        {
            FailedAttemptCount: > 0
        } && recent.ExpiresAt > now;

        if (attemptBudgetIsActive && recent!.FailedAttemptCount >= MaximumAttempts)
        {
            return PreparedSecurityCode.Failed(
                "Too many incorrect attempts. Wait 15 minutes before requesting another code.");
        }

        if (recent is not null && recent.CreatedAt > now.AddMinutes(-1))
        {
            return PreparedSecurityCode.Failed(
                "A code was sent recently. Please wait one minute before requesting another.");
        }

        var value = RandomNumberGenerator.GetInt32(100_000, 1_000_000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var activeRecords = await db.AccountSecurityCodes
            .Where(code =>
                code.UserId == userId
                && code.Action == action
                && code.ConsumedAt == null
                && code.CreatedAt > now.Subtract(Lifetime))
            .ToListAsync(cancellationToken);
        foreach (var activeRecord in activeRecords)
        {
            activeRecord.ConsumedAt = now;
        }

        var record = new AccountSecurityCode
        {
            UserId = userId,
            Action = action,
            ProtectedCode = GetProtector(userId, action).Protect(Hash(value)),
            FailedAttemptCount = attemptBudgetIsActive ? recent!.FailedAttemptCount : 0,
            CreatedAt = now,
            ExpiresAt = attemptBudgetIsActive && recent!.ExpiresAt > now.Add(Lifetime)
                ? recent.ExpiresAt
                : now.Add(Lifetime)
        };
        db.AccountSecurityCodes.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        var message = action switch
        {
            AccountSecurityAction.ConfirmEmail => "A six-digit verification code was sent to your email. It expires in 10 minutes.",
            AccountSecurityAction.ResetPassword => "A six-digit password reset code was sent to your email. It expires in 10 minutes.",
            _ => "A six-digit confirmation code was sent to your email. It expires in 10 minutes."
        };
        return new PreparedSecurityCode(
            new SecurityCodeIssueResult(true, message),
            record.Id,
            value);
    }

    public async Task<SecurityCodeVerificationResult> VerifyAsync(string userId, AccountSecurityAction action, string? code,
        CancellationToken cancellationToken = default) =>
        await quotaGate.RunAsync(
            GetQuotaResource(action),
            userId,
            operationCancellationToken => VerifyCoreAsync(
                userId,
                action,
                code,
                operationCancellationToken),
            cancellationToken);

    private async Task<SecurityCodeVerificationResult> VerifyCoreAsync(
        string userId,
        AccountSecurityAction action,
        string? code,
        CancellationToken cancellationToken)
    {
        var normalizedCode = (code ?? string.Empty).Trim();
        var record = await db.AccountSecurityCodes
            .Where(item => item.UserId == userId && item.Action == action && item.ConsumedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (record is null || record.CreatedAt.Add(Lifetime) <= now)
        {
            return new SecurityCodeVerificationResult(false, null, "That code has expired. Request a new code and try again.");
        }

        if (record.FailedAttemptCount >= MaximumAttempts && record.ExpiresAt > now)
        {
            return new SecurityCodeVerificationResult(false, null, "Too many incorrect attempts. Wait 15 minutes before trying again.");
        }

        if (!IsSixAsciiDigits(normalizedCode) || !Matches(record, normalizedCode))
        {
            record.FailedAttemptCount++;
            var attemptBudgetExpiresAt = now.Add(AttemptBudgetWindow);
            if (attemptBudgetExpiresAt > record.ExpiresAt)
            {
                record.ExpiresAt = attemptBudgetExpiresAt;
            }

            await db.SaveChangesAsync(cancellationToken);
            return new SecurityCodeVerificationResult(
                false,
                null,
                "That code is not correct. Request a new code if you reach the attempt limit.");
        }

        record.FailedAttemptCount = 0;
        record.ConsumedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return new SecurityCodeVerificationResult(true, record.Id, string.Empty);
    }

    public async Task ConsumeAsync(int codeId, CancellationToken cancellationToken = default)
    {
        var record = await db.AccountSecurityCodes.SingleOrDefaultAsync(code => code.Id == codeId, cancellationToken);
        if (record is null) return;
        record.ConsumedAt ??= timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    private bool Matches(AccountSecurityCode record, string value)
    {
        try
        {
            var expected = GetProtector(record.UserId, record.Action).Unprotect(record.ProtectedCode);
            return CryptographicOperations.FixedTimeEquals(Hash(value), expected);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private IDataProtector GetProtector(string userId, AccountSecurityAction action) =>
        dataProtectionProvider
            .CreateProtector("ApplyWise.AccountSecurityCode.v1")
            .CreateProtector(userId)
            .CreateProtector(action.ToString());

    private static bool IsSixAsciiDigits(string value) =>
        value.Length == 6 && value.All(char.IsAsciiDigit);

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.ASCII.GetBytes(value));

    private static string GetQuotaResource(AccountSecurityAction action) =>
        $"{WorkspaceQuotaResources.AccountSecurityCodes}:{action}";

    private sealed record PreparedSecurityCode(
        SecurityCodeIssueResult Result,
        int CodeId,
        string? Value)
    {
        public static PreparedSecurityCode Failed(string message) =>
            new(new SecurityCodeIssueResult(false, message), 0, null);
    }
}
