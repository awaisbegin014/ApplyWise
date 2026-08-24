using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Contact;

public sealed class ContactFormViewModel
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "Your name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(320)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [EnumDataType(typeof(ContactTopic))]
    [Display(Name = "What can we help with?")]
    public ContactTopic? Topic { get; set; }

    [Required]
    [StringLength(160, MinimumLength = 3)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [StringLength(4000, MinimumLength = 10)]
    [Display(Name = "How can we help?")]
    public string Message { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Website { get; set; }
}
