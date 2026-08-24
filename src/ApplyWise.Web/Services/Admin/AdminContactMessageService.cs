using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.Admin;

public interface IAdminContactMessageService
{
    Task<AdminContactInboxViewModel> LoadInboxAsync(
        string? status,
        string? search,
        int page,
        CancellationToken cancellationToken = default);

    Task<AdminContactMessageViewModel?> LoadDetailsAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<bool> SetReadStateAsync(
        long id,
        bool isRead,
        CancellationToken cancellationToken = default);
}

public sealed class AdminContactMessageService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : IAdminContactMessageService
{
    private const int PageSize = AdminContactInboxViewModel.DefaultPageSize;

    public async Task<AdminContactInboxViewModel> LoadInboxAsync(
        string? status,
        string? search,
        int page,
        CancellationToken cancellationToken = default)
    {
        status = NormalizeStatus(status);
        search = NormalizeSearch(search);

        var query = dbContext.ContactMessages.AsNoTracking();
        query = status switch
        {
            "unread" => query.Where(message => message.ReadAt == null),
            "read" => query.Where(message => message.ReadAt != null),
            _ => query
        };

        if (search is not null)
        {
            var normalizedSearch = search.ToUpperInvariant();
            query = query.Where(message =>
                message.FullName.ToUpper().Contains(normalizedSearch)
                || message.Email.ToUpper().Contains(normalizedSearch)
                || message.Subject.ToUpper().Contains(normalizedSearch));
        }

        var totalMatchingMessages = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalMatchingMessages / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var rows = await query
            .OrderBy(message => message.ReadAt != null)
            .ThenByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(message => new
            {
                message.Id,
                message.FullName,
                message.Email,
                message.Topic,
                message.Subject,
                message.Body,
                message.CreatedAt,
                message.ReadAt
            })
            .ToListAsync(cancellationToken);

        return new AdminContactInboxViewModel
        {
            GeneratedAt = timeProvider.GetUtcNow(),
            Status = status,
            Search = search,
            Page = page,
            PageSize = PageSize,
            TotalPages = totalPages,
            TotalMatchingMessages = totalMatchingMessages,
            TotalMessages = await dbContext.ContactMessages.CountAsync(cancellationToken),
            UnreadMessages = await dbContext.ContactMessages.CountAsync(
                message => message.ReadAt == null,
                cancellationToken),
            Messages = rows.Select(message => new AdminContactMessageRowViewModel(
                message.Id,
                message.FullName,
                message.Email,
                message.Topic.GetDisplayName(),
                message.Subject,
                CreatePreview(message.Body),
                message.CreatedAt,
                message.ReadAt)).ToArray()
        };
    }

    public async Task<AdminContactMessageViewModel?> LoadDetailsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var message = await dbContext.ContactMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return message is null
            ? null
            : new AdminContactMessageViewModel(
                message.Id,
                message.UserId,
                message.FullName,
                message.Email,
                message.Topic.GetDisplayName(),
                message.Subject,
                message.Body,
                message.CreatedAt,
                message.ReadAt);
    }

    public async Task<bool> SetReadStateAsync(
        long id,
        bool isRead,
        CancellationToken cancellationToken = default)
    {
        var message = await dbContext.ContactMessages
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (message is null)
        {
            return false;
        }

        DateTimeOffset? readAt = isRead
            ? message.ReadAt ?? timeProvider.GetUtcNow()
            : null;
        if (message.ReadAt != readAt)
        {
            message.ReadAt = readAt;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static string NormalizeStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "unread" => "unread",
            "read" => "read",
            _ => "all"
        };

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var normalized = search.Trim();
        return normalized.Length <= 320 ? normalized : normalized[..320];
    }

    private static string CreatePreview(string body)
    {
        var preview = string.Join(
            ' ',
            body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return preview.Length <= 160 ? preview : $"{preview[..157]}...";
    }
}
