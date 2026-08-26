using ApplyWise.Web.Services.Ai;
using ApplyWise.Web.Services.Subscriptions;
using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.AiCoach;

public sealed class AiCoachViewModel
{
    [Required]
    [StringLength(700, MinimumLength = 20)]
    [Display(Name = "Resume bullet")]
    public string Bullet { get; set; } = string.Empty;

    [StringLength(4000)]
    [Display(Name = "Job description or requirement (optional)")]
    public string? JobContext { get; set; }

    [Display(Name = "I understand that this text will be processed by Google Gemini to generate the requested suggestions.")]
    public bool AiProcessingConsent { get; set; }

    public SubscriptionSnapshot? Subscription { get; set; }
    public AiBulletCoachResult? Result { get; set; }
    public bool AiConfigured { get; set; }
}
