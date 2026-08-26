using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public sealed class ResumeBuildUsageRecord
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? TemplateId { get; set; }
    public IdentityUser? User { get; set; }
}
