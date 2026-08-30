using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ApplicationEmailParserTests
{
    private readonly ApplicationEmailParser _parser = new();

    [Fact]
    public void Parse_LinkedInConfirmation_ExtractsApplicationDetails()
    {
        var message = Message(
            subject: "Your application was sent to Contoso",
            from: "jobs-noreply@linkedin.com",
            body: "Your application for Software Engineer at Contoso",
            labels: ["INBOX"],
            authenticationResults:
                "mx.google.com; dkim=pass header.i=@linkedin.com; dmarc=pass (p=REJECT) header.from=linkedin.com");

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.LinkedIn, result.Source);
        Assert.Equal(ApplicationImportDirection.Incoming, result.Direction);
        Assert.Equal("Contoso", result.CompanyName);
        Assert.Equal("Software Engineer", result.JobTitle);
        Assert.True(result.Confidence >= 80);
    }

    [Fact]
    public void Parse_IndeedConfirmation_UsesSubjectAndBody()
    {
        var message = Message(
            subject: "Application received - Data Analyst",
            from: "application-noreply@indeed.com",
            body: "Thank you for applying to Acme.",
            labels: ["INBOX"],
            authenticationResults:
                "mx.google.com; spf=pass smtp.mailfrom=indeed.com; dmarc=pass header.from=indeed.com");

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Indeed, result.Source);
        Assert.Equal("Acme", result.CompanyName);
        Assert.Equal("Data Analyst", result.JobTitle);
    }

    [Fact]
    public void Parse_IndeedApplySubject_ExtractsTitleAndCompany()
    {
        var message = Message(
            subject: "Indeed Application: Job Title: IT Intern – Internship",
            from: "indeedapply@indeed.com",
            body:
                "Application submitted. Job Title: IT Intern – Internship. "
                + "NKC SMC PVT LTD - Karachi. "
                + "The following items were sent to NKC SMC PVT LTD. Good luck!",
            labels: ["INBOX"],
            authenticationResults:
                "mx.google.com; dkim=pass header.i=@indeed.com; dmarc=pass header.from=indeed.com");

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Indeed, result.Source);
        Assert.Equal("NKC SMC PVT LTD", result.CompanyName);
        Assert.Equal("IT Intern – Internship", result.JobTitle);
        Assert.True(
            result.Confidence >= ApplicationImportPolicy.HighConfidenceThreshold);
    }

    [Theory]
    [InlineData("indeed.com")]
    [InlineData("indeedemail.com")]
    [InlineData("indeedmail.com")]
    [InlineData("notifications.indeedemail.com")]
    public void Parse_OfficialIndeedDomains_AreEligibleForAutomaticAdd(
        string senderDomain)
    {
        var message = Message(
            subject: "Indeed Application: Software Engineer",
            from: $"Indeed Apply <applications@{senderDomain}>",
            body: "The following items were sent to Contoso.",
            labels: ["INBOX"],
            authenticationResults:
                $"mx.google.com; dkim=pass header.i=@{senderDomain}; "
                + $"spf=pass smtp.mailfrom={senderDomain}; "
                + $"dmarc=pass (p=REJECT) header.from={senderDomain}");

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Indeed, result.Source);
        Assert.Equal("Contoso", result.CompanyName);
        Assert.Equal("Software Engineer", result.JobTitle);
        Assert.True(
            result.Confidence >= ApplicationImportPolicy.HighConfidenceThreshold);
    }

    [Fact]
    public void Parse_ModernIndeedSubject_SeparatesJobTitleAndCompany()
    {
        var message = Message(
            subject: "Application submitted: Software Engineer at Contoso",
            from: "Indeed Apply <applications@indeedmail.com>",
            body: "Your application has been submitted.",
            labels: ["INBOX"],
            authenticationResults:
                "mx.google.com; dkim=pass header.i=@indeedmail.com; "
                + "spf=pass smtp.mailfrom=indeedmail.com; "
                + "dmarc=pass (p=REJECT) header.from=indeedmail.com");

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Indeed, result.Source);
        Assert.Equal("Contoso", result.CompanyName);
        Assert.Equal("Software Engineer", result.JobTitle);
        Assert.True(
            result.Confidence >= ApplicationImportPolicy.HighConfidenceThreshold);
    }

    [Fact]
    public void Parse_OfficialIndeedSender_WinsOverLinksMentionedInFooter()
    {
        var message = Message(
            subject: "Indeed Application: Software Engineer",
            from: "Indeed Apply <applications@indeedemail.com>",
            body:
                "The following items were sent to Contoso. "
                + "Follow the employer on LinkedIn.",
            labels: ["INBOX"],
            authenticationResults:
                "mx.google.com; dmarc=pass header.from=indeedemail.com");

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Indeed, result.Source);
    }

    [Fact]
    public void Parse_IndeedApplySubjectFromUntrustedDomain_ReturnsNull()
    {
        var message = Message(
            subject: "Indeed Application: Full Stack Software Developer (MERN) – Remote",
            from: "sender@example.test",
            body: "We'll help you get started.",
            labels: ["INBOX"]);

        Assert.Null(_parser.Parse(message));
    }

    [Fact]
    public void Parse_SentResumeApplication_UsesRecipientDomainAndAttachment()
    {
        var message = Message(
            subject: "Application for Backend Engineer",
            from: "candidate@gmail.com",
            to: "jobs@northwind.com",
            body: "Please find my resume attached for the Backend Engineer position.",
            labels: ["SENT"],
            attachments: ["Awais-Resume.pdf"]);

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Email, result.Source);
        Assert.Equal(ApplicationImportDirection.Outgoing, result.Direction);
        Assert.Equal("Northwind", result.CompanyName);
        Assert.Equal("Awais-Resume.pdf", result.ResumeFileName);
    }

    [Fact]
    public void Parse_UnrelatedEmail_ReturnsNull()
    {
        var message = Message(
            subject: "Weekly team update",
            from: "manager@example.com",
            body: "Here are this week's project notes.",
            labels: ["INBOX"]);

        Assert.Null(_parser.Parse(message));
    }

    [Fact]
    public void Parse_UntrustedHighScoringConfirmation_RequiresReview()
    {
        var message = Message(
            subject: "LinkedIn application confirmation for Software Engineer",
            from: "attacker@example.test",
            body: "Thank you for applying for Software Engineer at Contoso. View job: https://example.test/job/phish",
            labels: ["INBOX"]);

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.True(result.Confidence < ApplicationImportPolicy.HighConfidenceThreshold);
    }

    [Theory]
    [InlineData("jobs-noreply@indeed.com, attacker@example.test")]
    [InlineData("jobs-noreply@indeed.com\r\nBcc: attacker@example.test")]
    [InlineData("not an email address")]
    public void Parse_IncomingMessageWithAmbiguousOrInvalidFrom_ReturnsNull(string from)
    {
        var message = Message(
            subject: "Indeed Application: Software Engineer",
            from: from,
            body: "Thank you for applying to Contoso.",
            labels: ["INBOX"],
            authenticationResults:
                "mx.google.com; dmarc=pass header.from=indeed.com");

        Assert.Null(_parser.Parse(message));
    }

    [Fact]
    public void Parse_Trusted_domain_in_quoted_display_name_is_not_trusted()
    {
        var message = Message(
            subject: "Indeed Application: Software Engineer",
            from: "\"jobs@indeed.com\" <attacker@example.test>",
            body: "Thank you for applying to Contoso.",
            labels: ["INBOX"]);

        var result = _parser.Parse(message);

        Assert.True(
            result is null
            || result.Confidence < ApplicationImportPolicy.HighConfidenceThreshold);
    }

    [Fact]
    public void Parse_TrustedFromWithoutGoogleAuthentication_RequiresReview()
    {
        var message = Message(
            subject: "Indeed Application: Software Engineer",
            from: "jobs-noreply@indeed.com",
            body: "Thank you for applying to Contoso.",
            labels: ["INBOX"]);

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.True(result.Confidence < ApplicationImportPolicy.HighConfidenceThreshold);
    }

    [Theory]
    [InlineData("attacker.example; dmarc=pass header.from=indeed.com")]
    [InlineData("mx.google.com; dmarc=fail header.from=indeed.com")]
    [InlineData("mx.google.com; dmarc=pass header.from=attacker.example")]
    public void Parse_UntrustedAuthenticationResult_RequiresReview(
        string authenticationResults)
    {
        var message = Message(
            subject: "Indeed Application: Software Engineer",
            from: "jobs-noreply@indeed.com",
            body: "Thank you for applying to Contoso.",
            labels: ["INBOX"],
            authenticationResults: authenticationResults);

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.True(result.Confidence < ApplicationImportPolicy.HighConfidenceThreshold);
    }

    [Fact]
    public void Parse_SentMessageWithoutResume_ReturnsNull()
    {
        var message = Message(
            subject: "Question about the open position",
            from: "candidate@gmail.com",
            to: "jobs@example.com",
            body: "Could you share more information about this role?",
            labels: ["SENT"]);

        Assert.Null(_parser.Parse(message));
    }

    private static GmailMessageEnvelope Message(
        string subject,
        string from,
        string body,
        IReadOnlyCollection<string> labels,
        string to = "candidate@example.com",
        IReadOnlyCollection<string>? attachments = null,
        string authenticationResults = "") =>
        new(
            "message-1",
            "thread-1",
            subject,
            from,
            to,
            body,
            body,
            labels,
            attachments ?? [],
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero),
            authenticationResults);
}
