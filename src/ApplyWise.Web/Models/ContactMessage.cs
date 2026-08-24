using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public enum ContactTopic
{
    [Display(Name = "Product question")]
    ProductQuestion = 1,

    [Display(Name = "Technical support")]
    TechnicalSupport = 2,

    [Display(Name = "Account access")]
    AccountAccess = 3,

    [Display(Name = "Privacy or data request")]
    PrivacyRequest = 4,

    [Display(Name = "Feedback or suggestion")]
    Feedback = 5,

    [Display(Name = "Something else")]
    Other = 6
}

public sealed class ContactMessage
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public ContactTopic Topic { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    public IdentityUser? User { get; set; }
}
