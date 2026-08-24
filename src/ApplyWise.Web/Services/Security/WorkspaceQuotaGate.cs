using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ApplyWise.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ApplyWise.Web.Services.Security;

public static class WorkspaceQuotaResources
{
    public const string AccountSecurityCodes = "account-security-codes";
    public const string Applications = "applications";
    public const string Interviews = "interviews";
    public const string ResumeAnalyses = "resume-analyses";
    public const string ApplicationImports = "application-imports";
    public const string ContactMessages = "contact-messages";
    public const string GmailConnections = "gmail-connections";
    public const string PendingRegistrations = "pending-registrations";
    public const string Resumes = "resumes";
    public const string ScamChecks = "scam-checks";
}

public interface IWorkspaceQuotaGate
{
    Task<TResult> RunAsync<TResult>(
        string resource,
        string userId,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Serializes each user's quota check and write as one database transaction.
/// SQL Server uses a transaction-owned application lock on the same connection
/// as the protected EF work. Other providers use a process-wide lock; relational
/// providers also receive a serializable transaction.
/// </summary>
public sealed class WorkspaceQuotaGate(ApplicationDbContext dbContext) : IWorkspaceQuotaGate
{
    private static readonly TimeSpan AdmissionTimeout = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim[] ProcessLocks = Enumerable.Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task<TResult> RunAsync<TResult>(
        string resource,
        string userId,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(action);

        var lockName = BuildLockName(resource, userId);
        // Admit at most one operation per resource and process before leasing a
        // pooled SQL connection. Cross-instance serialization still happens in
        // SQL, but local waiters can no longer exhaust the ADO.NET pool.
        var semaphore = GetProcessLock(lockName);
        if (!await semaphore.WaitAsync(AdmissionTimeout, cancellationToken))
        {
            throw new ResourceLockUnavailableException(
                "The requested workspace operation is busy. Try again shortly.");
        }

        try
        {
            if (dbContext.Database.IsSqlServer())
            {
                return await RunInTransactionAsync(
                    lockName,
                    acquireSqlApplicationLock: true,
                    action,
                    cancellationToken);
            }

            return dbContext.Database.IsRelational()
                ? await RunInTransactionAsync(
                    lockName,
                    acquireSqlApplicationLock: false,
                    action,
                    cancellationToken)
                : await action(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<TResult> RunInTransactionAsync<TResult>(
        string lockName,
        bool acquireSqlApplicationLock,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        // Quota writes intentionally do not retry a transaction after an
        // ambiguous commit. A retry could repeat a store-generated insert and
        // exceed a quota, so the original failure is surfaced for reconciliation.
        var strategy = new SingleAttemptExecutionStrategy(dbContext);
        return await strategy.ExecuteAsync(
            async operationCancellationToken =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    operationCancellationToken);
                if (acquireSqlApplicationLock)
                {
                    await AcquireSqlApplicationLockAsync(
                        transaction,
                        lockName,
                        operationCancellationToken);
                }

                var result = await action(operationCancellationToken);
                await transaction.CommitAsync(operationCancellationToken);
                return result;
            },
            cancellationToken);
    }

    private async Task AcquireSqlApplicationLockAsync(
        IDbContextTransaction transaction,
        string lockName,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandTimeout = 10;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            SELECT @result;
            """;
        command.Parameters.Add(
            new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = lockName });

        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new ResourceLockUnavailableException(
                $"The workspace quota lock is busy (SQL result {result}).");
        }
    }

    private static string BuildLockName(string resource, string userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{resource}:{userId}"));
        return $"ApplyWise:Quota:{Convert.ToHexString(hash)}";
    }

    private static SemaphoreSlim GetProcessLock(string lockName) =>
        ProcessLocks[(uint)StringComparer.Ordinal.GetHashCode(lockName)
            % (uint)ProcessLocks.Length];

}
