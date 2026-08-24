using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public sealed class GmailOAuthFlow
{
    public required string UserId { get; set; }
    public required string FlowId { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    public IdentityUser? User { get; set; }
}
