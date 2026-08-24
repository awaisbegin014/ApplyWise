namespace ApplyWise.Web.ViewModels.Admin;

public sealed class AdminContactInboxViewModel
{
    public const int DefaultPageSize = 25;

    public required DateTimeOffset GeneratedAt { get; init; }
    public required string Status { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
    public int TotalPages { get; init; } = 1;
    public int TotalMatchingMessages { get; init; }
    public int TotalMessages { get; init; }
    public int UnreadMessages { get; init; }
    public IReadOnlyList<AdminContactMessageRowViewModel> Messages { get; init; } = [];

    public int ReadMessages => TotalMessages - UnreadMessages;
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public sealed record AdminContactMessageRowViewModel(
    long Id,
    string FullName,
    string Email,
    string Topic,
    string Subject,
    string Preview,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt)
{
    public bool IsRead => ReadAt.HasValue;
}

public sealed record AdminContactMessageViewModel(
    long Id,
    string? UserId,
    string FullName,
    string Email,
    string Topic,
    string Subject,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt)
{
    public bool IsRead => ReadAt.HasValue;
}
