using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApplyWise.Web.Models;

public enum SubscriptionTier
{
    Free,
    Pro
}

public enum ProUpgradeRequestStatus
{
    Pending,
    Approved,
    Rejected
}

public enum AiUsageStatus
{
    Reserved,
    Succeeded,
    Failed
}

public sealed class UserSubscription
{
    public required string UserId { get; set; }
    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Free;
    public DateTimeOffset? ProStartedAt { get; set; }
    public DateTimeOffset? ProExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    [ForeignKey(nameof(UserId))]
    public IdentityUser? User { get; set; }
}

public sealed class ProUpgradeRequest
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public string PlanCode { get; set; } = "pro-monthly";
    public required string PaymentMethod { get; set; }
    public required string TransactionReference { get; set; }
    public required string PayerName { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PKR";
    public string? UserNote { get; set; }
    public ProUpgradeRequestStatus Status { get; set; } = ProUpgradeRequestStatus.Pending;
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? AdminNote { get; set; }
    public byte[] RowVersion { get; set; } = [];

    [ForeignKey(nameof(UserId))]
    public IdentityUser? User { get; set; }
}

public sealed class AiUsageRecord
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public required string Feature { get; set; }
    public AiUsageStatus Status { get; set; } = AiUsageStatus.Reserved;
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int PromptCharacters { get; set; }
    public int ResponseCharacters { get; set; }
    public string? Model { get; set; }

    [ForeignKey(nameof(UserId))]
    public IdentityUser? User { get; set; }
}
