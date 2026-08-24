using System.Data;
using System.Security.Cryptography;
using System.Text;
using ApplyWise.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.Security;

public interface IApplicationLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates sensitive per-resource operations across application instances.
/// SQL Server uses a session-owned application lock on a dedicated connection;
/// non-SQL providers use the process-wide fallback used by the test suite.
/// </summary>
public sealed class ApplicationLockProvider(ApplicationDbContext dbContext) : IApplicationLockProvider
{
    private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim[] ProcessLocks = Enumerable.Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();
    private static readonly SemaphoreSlim SqlLeaseAdmission = new(16, 16);

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var lockName = BuildLockName(resource);
        var effectiveTimeout = timeout == Timeout.InfiniteTimeSpan || timeout > MaximumWait
            ? MaximumWait
            : timeout;
        var semaphore = GetProcessLock(lockName);
        var admitted = await semaphore.WaitAsync(effectiveTimeout, cancellationToken);
        if (!admitted) return null;

        if (!dbContext.Database.IsSqlServer())
        {
            return new SemaphoreLease(semaphore);
        }

        if (!await SqlLeaseAdmission.WaitAsync(effectiveTimeout, cancellationToken))
        {
            semaphore.Release();
            return null;
        }

        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            SqlLeaseAdmission.Release();
            semaphore.Release();
            throw new InvalidOperationException("The SQL Server connection string is unavailable.");
        }
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 10;
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = @resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = @timeout;
                SELECT @result;
                """;
            command.Parameters.Add(
                new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = lockName });
            command.Parameters.Add(
                new SqlParameter("@timeout", SqlDbType.Int)
                {
                    Value = timeout == Timeout.InfiniteTimeSpan
                        ? checked((int)MaximumWait.TotalMilliseconds)
                        : checked((int)Math.Min(effectiveTimeout.TotalMilliseconds, int.MaxValue))
                });
            var result = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            if (result < 0)
            {
                await connection.DisposeAsync();
                SqlLeaseAdmission.Release();
                semaphore.Release();
                return null;
            }

            return new SqlApplicationLockLease(
                connection,
                lockName,
                semaphore,
                SqlLeaseAdmission);
        }
        catch
        {
            await connection.DisposeAsync();
            SqlLeaseAdmission.Release();
            semaphore.Release();
            throw;
        }
    }

    private static string BuildLockName(string resource)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resource));
        return $"ApplyWise:{Convert.ToHexString(hash)}";
    }

    private static SemaphoreSlim GetProcessLock(string lockName) =>
        ProcessLocks[(uint)StringComparer.Ordinal.GetHashCode(lockName)
            % (uint)ProcessLocks.Length];

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class SqlApplicationLockLease(
        SqlConnection connection,
        string lockName,
        SemaphoreSlim semaphore,
        SemaphoreSlim globalAdmission) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;

            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandTimeout = 5;
                    command.CommandText = """
                        EXEC sys.sp_releaseapplock
                            @Resource = @resource,
                            @LockOwner = 'Session';
                        """;
                    command.Parameters.Add(
                        new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = lockName });
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            finally
            {
                await connection.DisposeAsync();
                globalAdmission.Release();
                semaphore.Release();
            }
        }
    }
}
