using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.Gmail;

public sealed record GmailMessageEnvelope(
    string MessageId,
    string? ThreadId,
    string Subject,
    string From,
    string To,
    string Body,
    string Snippet,
    IReadOnlyCollection<string> LabelIds,
    IReadOnlyCollection<string> AttachmentFileNames,
    DateTimeOffset SentAt,
    string AuthenticationResults = "");

public sealed record ApplicationImportSuggestion(
    ApplicationImportDirection Direction,
    JobSource Source,
    int Confidence,
    string CompanyName,
    string JobTitle,
    string? JobLocation,
    string? JobUrl,
    DateOnly AppliedDate,
    string? ResumeFileName,
    string? SenderDomain);

public interface IApplicationEmailParser
{
    ApplicationImportSuggestion? Parse(GmailMessageEnvelope message);
}

public sealed partial class ApplicationEmailParser : IApplicationEmailParser
{
    private static readonly HashSet<string> GenericDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail", "googlemail", "outlook", "hotmail", "yahoo", "linkedin", "indeed",
        "indeedemail", "indeedmail",
        "greenhouse", "lever", "workday", "smartrecruiters", "icims", "jobvite", "ashbyhq"
    };

    public ApplicationImportSuggestion? Parse(GmailMessageEnvelope message)
    {
        var direction = message.LabelIds.Contains("SENT", StringComparer.OrdinalIgnoreCase)
            ? ApplicationImportDirection.Outgoing
            : ApplicationImportDirection.Incoming;
        var body = WebUtility.HtmlDecode(message.Body);
        var searchable = $"{message.Subject}\n{message.Snippet}\n{body}";
        var hasResumeAttachment = message.AttachmentFileNames.Any(IsResumeFile);
        var senderDomain = GetMailboxDomain(
            direction == ApplicationImportDirection.Outgoing
                ? message.To
                : message.From,
            requireSingle: direction == ApplicationImportDirection.Incoming);
        if (senderDomain is null)
        {
            return null;
        }
        var isVerifiedIndeedApplication =
            direction == ApplicationImportDirection.Incoming
            && IsIndeedDomain(senderDomain)
            && IndeedApplicationSubjectRegex().IsMatch(message.Subject);

        if (direction == ApplicationImportDirection.Incoming)
        {
            if (!isVerifiedIndeedApplication
                && !ApplicationLanguageRegex().IsMatch(searchable))
            {
                return null;
            }
        }
        else if (!hasResumeAttachment || !OutgoingApplicationLanguageRegex().IsMatch(searchable))
        {
            return null;
        }

        var source = DetectSource(searchable, message.From, senderDomain, direction);
        var trustedIncomingDomain = direction == ApplicationImportDirection.Incoming
            && IsTrustedAutoAddDomain(senderDomain)
            && HasGoogleAuthenticatedFrom(message.AuthenticationResults, senderDomain);
        var (company, jobTitle) = ExtractCompanyAndTitle(message.Subject, body);
        company ??= CompanyFromDomain(senderDomain);
        var jobUrl = ExtractJobUrl(body);
        var confidence = CalculateConfidence(
            direction,
            source,
            company,
            jobTitle,
            hasResumeAttachment,
            message.Subject,
            trustedIncomingDomain);

        return new ApplicationImportSuggestion(
            direction,
            source,
            confidence,
            Truncate(company ?? string.Empty, 150),
            Truncate(jobTitle ?? string.Empty, 150),
            null,
            TruncateNullable(jobUrl, 2048),
            DateOnly.FromDateTime(message.SentAt.UtcDateTime),
            TruncateNullable(message.AttachmentFileNames.FirstOrDefault(IsResumeFile), 255),
            TruncateNullable(senderDomain, 255));
    }

    private static JobSource DetectSource(
        string searchable,
        string from,
        string? senderDomain,
        ApplicationImportDirection direction)
    {
        if (direction == ApplicationImportDirection.Incoming)
        {
            if (IsIndeedDomain(senderDomain)) return JobSource.Indeed;
            if (IsDomainOrSubdomain(senderDomain, "linkedin.com"))
            {
                return JobSource.LinkedIn;
            }
        }

        var sourceText = $"{from}\n{searchable}";
        if (sourceText.Contains("linkedin", StringComparison.OrdinalIgnoreCase)) return JobSource.LinkedIn;
        if (sourceText.Contains("indeed", StringComparison.OrdinalIgnoreCase)) return JobSource.Indeed;
        if (sourceText.Contains("rozee", StringComparison.OrdinalIgnoreCase)) return JobSource.Rozee;
        return direction == ApplicationImportDirection.Outgoing
            ? JobSource.Email
            : JobSource.CompanyWebsite;
    }

    private static (string? Company, string? JobTitle) ExtractCompanyAndTitle(
        string subject,
        string body)
    {
        var subjectJobAtCompanyMatch = SubjectJobAtCompanyRegex().Match(subject);
        if (subjectJobAtCompanyMatch.Success)
        {
            return (
                CleanCandidate(subjectJobAtCompanyMatch.Groups["company"].Value),
                CleanCandidate(subjectJobAtCompanyMatch.Groups["job"].Value));
        }

        var subjectMatch = JobAtCompanyRegex().Match(subject);
        if (subjectMatch.Success)
        {
            return (
                CleanCandidate(subjectMatch.Groups["company"].Value),
                CleanCandidate(subjectMatch.Groups["job"].Value));
        }

        var bodyMatch = JobAtCompanyRegex().Match(StripHtml(body));
        if (bodyMatch.Success)
        {
            return (
                CleanCandidate(bodyMatch.Groups["company"].Value),
                CleanCandidate(bodyMatch.Groups["job"].Value));
        }

        var companyMatch = CompanySubjectRegex().Match(subject);
        if (!companyMatch.Success)
        {
            companyMatch = CompanySubjectRegex().Match(StripHtml(body));
        }
        var titleMatch = JobTitleSubjectRegex().Match(subject);
        var company = companyMatch.Success
            ? CleanCandidate(companyMatch.Groups["company"].Value)
            : null;
        var title = titleMatch.Success
            ? CleanCandidate(titleMatch.Groups["job"].Value)
            : null;

        if (title is null)
        {
            var indeedTitleMatch = IndeedApplicationSubjectRegex().Match(subject);
            if (indeedTitleMatch.Success)
            {
                title = CleanCandidate(indeedTitleMatch.Groups["job"].Value);
            }
        }

        if (title is null)
        {
            var bodyTitleMatch = JobTitleBodyRegex().Match(StripHtml(body));
            if (bodyTitleMatch.Success)
            {
                title = CleanCandidate(bodyTitleMatch.Groups["job"].Value);
            }
        }

        return (company, title);
    }

    private static string? ExtractJobUrl(string body)
    {
        foreach (Match match in UrlRegex().Matches(body))
        {
            var candidate = WebUtility.HtmlDecode(match.Value)
                .TrimEnd('.', ',', ')', ']', ';');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) continue;
            var combined = $"{uri.Host}{uri.AbsolutePath}";
            if (combined.Contains("unsubscribe", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("privacy", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("accounts.google", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (combined.Contains("job", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("career", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("view", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static int CalculateConfidence(
        ApplicationImportDirection direction,
        JobSource source,
        string? company,
        string? jobTitle,
        bool hasResumeAttachment,
        string subject,
        bool trustedIncomingDomain)
    {
        var confidence = direction == ApplicationImportDirection.Incoming ? 58 : 45;
        if (source is JobSource.LinkedIn or JobSource.Indeed or JobSource.Rozee) confidence += 12;
        if (!string.IsNullOrWhiteSpace(company)) confidence += 10;
        if (!string.IsNullOrWhiteSpace(jobTitle)) confidence += 10;
        if (hasResumeAttachment) confidence += 5;
        if (ConfirmationSubjectRegex().IsMatch(subject)) confidence += 7;
        var maximum = direction == ApplicationImportDirection.Incoming && !trustedIncomingDomain
            ? ApplicationImportPolicy.HighConfidenceThreshold - 1
            : 95;
        return Math.Clamp(confidence, 0, maximum);
    }

    private static string? GetMailboxDomain(string headerValue, bool requireSingle)
    {
        if (string.IsNullOrWhiteSpace(headerValue)
            || headerValue.Contains('\r')
            || headerValue.Contains('\n'))
        {
            return null;
        }

        try
        {
            var addresses = new MailAddressCollection();
            addresses.Add(headerValue);
            if (addresses.Count == 0 || (requireSingle && addresses.Count != 1))
            {
                return null;
            }

            var address = addresses[0].Address;
            var separator = address.LastIndexOf('@');
            return separator > 0 && separator < address.Length - 1
                ? address[(separator + 1)..].ToLowerInvariant()
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsIndeedDomain(string? domain) =>
        !string.IsNullOrWhiteSpace(domain)
        && (IsDomainOrSubdomain(domain, "indeed.com")
            || IsDomainOrSubdomain(domain, "indeedemail.com")
            || IsDomainOrSubdomain(domain, "indeedmail.com"));

    private static bool IsTrustedAutoAddDomain(string? domain) =>
        IsIndeedDomain(domain)
        || IsDomainOrSubdomain(domain, "linkedin.com")
        || IsDomainOrSubdomain(domain, "greenhouse.io")
        || IsDomainOrSubdomain(domain, "lever.co")
        || IsDomainOrSubdomain(domain, "myworkday.com")
        || IsDomainOrSubdomain(domain, "smartrecruiters.com")
        || IsDomainOrSubdomain(domain, "icims.com")
        || IsDomainOrSubdomain(domain, "jobvite.com")
        || IsDomainOrSubdomain(domain, "ashbyhq.com");

    private static bool HasGoogleAuthenticatedFrom(
        string authenticationResults,
        string senderDomain)
    {
        if (string.IsNullOrWhiteSpace(authenticationResults)
            || authenticationResults.Contains('\r')
            || authenticationResults.Contains('\n'))
        {
            return false;
        }

        // Only trust the receiving Gmail boundary's result. A sender can add an
        // Authentication-Results header of its own, so the authserv-id matters.
        var segments = authenticationResults.Split(';', StringSplitOptions.TrimEntries);
        if (segments.Length < 2
            || !segments[0].Equals("mx.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var segment in segments.Skip(1))
        {
            if (!DmarcPassRegex().IsMatch(segment)) continue;
            var authenticatedFrom = HeaderFromRegex().Match(segment);
            if (authenticatedFrom.Success
                && authenticatedFrom.Groups["domain"].Value
                    .TrimEnd('.')
                    .Equals(senderDomain.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDomainOrSubdomain(string? domain, string expected) =>
        !string.IsNullOrWhiteSpace(domain)
        && (domain.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith('.' + expected, StringComparison.OrdinalIgnoreCase));

    private static string? CompanyFromDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return null;
        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2) return null;
        var candidate = labels[^2];
        if (GenericDomains.Contains(candidate) || candidate.Length < 2) return null;
        return string.Concat(
            char.ToUpperInvariant(candidate[0]),
            candidate[1..].Replace('-', ' '));
    }

    private static bool IsResumeFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripHtml(string value) =>
        WhitespaceRegex().Replace(HtmlTagRegex().Replace(value, " "), " ").Trim();

    private static string? CleanCandidate(string value)
    {
        var cleaned = WhitespaceRegex().Replace(
                WebUtility.HtmlDecode(value),
                " ")
            .Trim(' ', '-', '–', '—', ':', '|', '.', '!');
        if (string.IsNullOrWhiteSpace(cleaned)) return null;
        return cleaned.Length <= 150 ? cleaned : cleaned[..150].Trim();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];

    [GeneratedRegex(
        @"(?ix)\b(application|applied|applying)\b.{0,80}\b(sent|submitted|received|confirmation|success|complete|thank|thanks)\b|\b(thank|thanks).{0,50}\bapplying\b|\bwe.{0,30}received.{0,30}application\b")]
    private static partial Regex ApplicationLanguageRegex();

    [GeneratedRegex(
        @"(?ix)\b(apply|application|job|position|role|resume|résumé|cv)\b")]
    private static partial Regex OutgoingApplicationLanguageRegex();

    [GeneratedRegex(
        @"(?ix)(?:application|applied)\s+(?:for|to)\s+(?:the\s+)?(?<job>[^|\r\n]{2,100}?)\s+(?:position\s+)?at\s+(?<company>[^|\r\n]{2,100}?)(?=\s+(?:(?:has\s+been|was)\s+(?:sent|submitted|received)|is\s+complete)\b|[.|\r\n]|$)")]
    private static partial Regex JobAtCompanyRegex();

    [GeneratedRegex(
        @"(?ix)^\s*(?:indeed\s+application|application\s+(?:received|submitted|confirmation))\s*[:\-–—]+\s*(?:job\s+title\s*:\s*)?(?<job>[^|\r\n]{2,120}?)\s+at\s+(?<company>[^|\r\n]{2,100})\s*$")]
    private static partial Regex SubjectJobAtCompanyRegex();

    [GeneratedRegex(
        @"(?ix)(?:application\s+(?:(?:was|has\s+been)\s+)?(?:sent|submitted)\s+to|we(?:'ve|\s+have)?\s+(?:sent|submitted)\s+your\s+application\s+to|the\s+following\s+items\s+were\s+sent\s+to|thank\s+you\s+for\s+applying\s+(?:to|with)|thanks\s+for\s+applying\s+(?:to|with))\s+(?<company>[^.|\r\n]{2,100})")]
    private static partial Regex CompanySubjectRegex();

    [GeneratedRegex(
        @"(?ix)(?:application\s+(?:received|submitted|confirmation)|applied)[:\s\-–—]+(?<job>[^|\r\n]{2,120})")]
    private static partial Regex JobTitleSubjectRegex();

    [GeneratedRegex(
        @"(?ix)^\s*indeed\s+application\s*:\s*(?:job\s+title\s*:\s*)?(?<job>[^|\r\n]{2,150})\s*$")]
    private static partial Regex IndeedApplicationSubjectRegex();

    [GeneratedRegex(
        @"(?ix)(?:position|role)\s+(?:of|for|as)?\s*(?<job>[A-Za-z0-9][^.,;|\r\n]{2,100})")]
    private static partial Regex JobTitleBodyRegex();

    [GeneratedRegex(
        @"(?ix)\b(application\s+(?:received|submitted|confirmation|complete)|thank\s+you\s+for\s+applying|application\s+was\s+sent|indeed\s+application)\b")]
    private static partial Regex ConfirmationSubjectRegex();

    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?i)\bdmarc\s*=\s*pass\b")]
    private static partial Regex DmarcPassRegex();

    [GeneratedRegex(@"(?i)\bheader\.from\s*=\s*(?<domain>[a-z0-9](?:[a-z0-9.-]{0,251}[a-z0-9])?)")]
    private static partial Regex HeaderFromRegex();
}
