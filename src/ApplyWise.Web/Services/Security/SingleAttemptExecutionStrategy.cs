using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ApplyWise.Web.Services.Security;

/// <summary>
/// Establishes an EF execution-strategy boundary for an explicit transaction
/// without replaying state-changing work after an ambiguous failure. A positive
/// retry count establishes the nested-operation boundary; ShouldRetryOn always
/// returns false, so the operation still has exactly one attempt.
/// </summary>
internal sealed class SingleAttemptExecutionStrategy(DbContext context)
    : ExecutionStrategy(context, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception) => false;
}
