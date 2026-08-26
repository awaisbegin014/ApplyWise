using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Admin;

public sealed class AdminProRequestsViewModel
{
    public string Status { get; init; } = "pending";
    public int PendingCount { get; init; }
    public IReadOnlyList<AdminProRequestRowViewModel> Requests { get; init; } = [];
}

public sealed record AdminProRequestRowViewModel(
    long Id,
    string UserId,
    string Email,
    string DisplayName,
    string PaymentMethod,
    string TransactionReference,
    string PayerName,
    decimal Amount,
    string Currency,
    string? UserNote,
    ProUpgradeRequestStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? AdminNote);

public sealed class AdminProReviewInputViewModel
{
    public long RequestId { get; set; }
    public string? AdminNote { get; set; }
}
