using ApplyWise.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Security;

public sealed class WorkspaceQuotaOptions
{
    public const string SectionName = "WorkspaceQuotas";

    public int MaxApplicationsPerUser { get; set; } = 1_000;
    public int MaxInterviewsPerUser { get; set; } = 1_000;
    public int MaxAnalysesPerUser { get; set; } = 2_000;
    public int MaxApplicationImportsPerUser { get; set; } = 2_000;
    public int MaxScamChecksPerUser { get; set; } = 2_000;
    public long MaxAnalysisSnapshotBytesPerUser { get; set; } = 50L * 1024 * 1024;
}

public interface IWorkspaceQuotaService
{
    Task<bool> CanCreateApplicationAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> CanCreateInterviewAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> CanCreateAnalysisAsync(string userId, long incomingSnapshotBytes, CancellationToken cancellationToken = default);
    Task<bool> CanCreateAnalysesAsync(string userId, int incomingCount, long incomingSnapshotBytes, CancellationToken cancellationToken = default);
    Task<bool> CanCreateApplicationImportAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> CanCreateScamCheckAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceQuotaService(
    ApplicationDbContext db,
    IOptions<WorkspaceQuotaOptions> options) : IWorkspaceQuotaService
{
    private WorkspaceQuotaOptions Limits => options.Value;

    public async Task<bool> CanCreateApplicationAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.JobApplications.CountAsync(item => item.UserId == userId, cancellationToken)
            < Limits.MaxApplicationsPerUser;

    public async Task<bool> CanCreateInterviewAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.Interviews.CountAsync(item => item.UserId == userId, cancellationToken)
            < Limits.MaxInterviewsPerUser;

    public async Task<bool> CanCreateAnalysisAsync(
        string userId,
        long incomingSnapshotBytes,
        CancellationToken cancellationToken = default) =>
        await CanCreateAnalysesAsync(userId, 1, incomingSnapshotBytes, cancellationToken);

    public async Task<bool> CanCreateAnalysesAsync(
        string userId,
        int incomingCount,
        long incomingSnapshotBytes,
        CancellationToken cancellationToken = default)
    {
        if (incomingCount < 0 || incomingSnapshotBytes < 0) return false;
        if (incomingCount == 0) return incomingSnapshotBytes == 0;
        var count = await db.ResumeAnalyses.CountAsync(
            item => item.UserId == userId,
            cancellationToken);
        if (incomingCount > Limits.MaxAnalysesPerUser - count) return false;

        var usedBytes = await db.ResumeAnalyses
            .Where(item => item.UserId == userId)
            .SumAsync(item => (long?)item.SnapshotSizeBytes, cancellationToken)
            ?? 0;
        return incomingSnapshotBytes <= Limits.MaxAnalysisSnapshotBytesPerUser - usedBytes;
    }

    public async Task<bool> CanCreateApplicationImportAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.ApplicationImports.CountAsync(item => item.UserId == userId, cancellationToken)
            < Limits.MaxApplicationImportsPerUser;

    public async Task<bool> CanCreateScamCheckAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.JobScamChecks.CountAsync(item => item.UserId == userId, cancellationToken)
            < Limits.MaxScamChecksPerUser;
}
