using System.Net.Mail;

namespace ApplyWise.Web.Services.Security;

public static class MailboxAddressNormalizer
{
    private static readonly char[] DisallowedUriCharacters = ['?', '&', '#'];

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var candidate = value.Trim();
        if (candidate.IndexOfAny(DisallowedUriCharacters) >= 0
            || candidate.Any(char.IsControl)
            || !MailAddress.TryCreate(candidate, out var mailbox)
            || !string.IsNullOrEmpty(mailbox.DisplayName)
            || !string.Equals(mailbox.Address, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = mailbox.Address;
        return normalized.Length <= 320;
    }
}
