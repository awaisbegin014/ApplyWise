using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Subscriptions;
using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.Subscriptions;

public sealed class SubscriptionPageViewModel
{
    public required SubscriptionSnapshot Subscription { get; init; }
    public required SubscriptionOptions Options { get; init; }
    public ProUpgradeRequest? LatestRequest { get; init; }
}

public sealed class ProUpgradeInputViewModel
{
    [Required]
    [StringLength(50)]
    [Display(Name = "Payment method")]
    public string PaymentMethod { get; set; } = string.Empty;

    [Required]
    [StringLength(120, MinimumLength = 4)]
    [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9._/#\- ]+$", ErrorMessage = "Use only letters, numbers, spaces, dots, slashes, dashes, # or underscores.")]
    [Display(Name = "Transaction reference")]
    public string TransactionReference { get; set; } = string.Empty;

    [Required]
    [StringLength(120, MinimumLength = 2)]
    [Display(Name = "Name used for payment")]
    public string PayerName { get; set; } = string.Empty;

    [Range(typeof(decimal), "1", "10000000")]
    [Display(Name = "Amount paid")]
    public decimal Amount { get; set; }

    [StringLength(1000)]
    [Display(Name = "Note for the owner (optional)")]
    public string? UserNote { get; set; }

    public SubscriptionOptions Options { get; set; } = new();
}
