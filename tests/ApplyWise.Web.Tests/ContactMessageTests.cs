using System.ComponentModel.DataAnnotations;
using System.Reflection;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Contact;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.Contact;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ContactMessageTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-24T07:00:00Z");

    [Fact]
    public void Contact_form_requires_bounded_sender_and_message_details()
    {
        var valid = new ContactFormViewModel
        {
            FullName = "Taylor Candidate",
            Email = "taylor@example.test",
            Topic = ContactTopic.TechnicalSupport,
            Subject = "Resume preview issue",
            Message = "The preview does not update after I save a project entry."
        };
        var invalid = new ContactFormViewModel
        {
            FullName = " ",
            Email = "not-an-email",
            Topic = null,
            Subject = "x",
            Message = "short"
        };

        Assert.Empty(Validate(valid));
        var errors = Validate(invalid);
        Assert.True(errors.Count >= 5, $"Expected at least five validation errors, found {errors.Count}.");
    }

    [Fact]
    public async Task Contact_post_trims_and_persists_once_then_redirects()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        var model = new ContactFormViewModel
        {
            FullName = "  Taylor Candidate  ",
            Email = "  taylor@example.test  ",
            Topic = ContactTopic.ProductQuestion,
            Subject = "  Comparing resume versions  ",
            Message = "  How should I compare two saved resume versions?  "
        };

        var result = await controller.Index(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ContactController.Sent), redirect.ActionName);
        var saved = await db.ContactMessages.SingleAsync();
        Assert.Equal("Taylor Candidate", saved.FullName);
        Assert.Equal("taylor@example.test", saved.Email);
        Assert.Equal("Comparing resume versions", saved.Subject);
        Assert.Equal("How should I compare two saved resume versions?", saved.Body);
        Assert.Equal(ContactTopic.ProductQuestion, saved.Topic);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Null(saved.ReadAt);
        Assert.Null(saved.UserId);
    }

    [Fact]
    public async Task Contact_store_expires_old_rows_and_evicts_read_rows_at_the_global_cap()
    {
        await using var db = CreateDbContext();
        db.ContactMessages.AddRange(
            Message(1, "Expired", "expired@example.test", "Expired", Now.AddDays(-31)),
            Message(2, "Read", "read@example.test", "Read", Now.AddDays(-2), Now.AddDays(-1)),
            Message(3, "Unread", "unread@example.test", "Unread", Now.AddDays(-1)));
        await db.SaveChangesAsync();
        var store = CreateStore(
            db,
            new ContactMessageStorageOptions
            {
                RetentionDays = 30,
                MaxStoredMessages = 2,
                ReservedAuthenticatedSlots = 0
            });
        var incoming = Message(
            0,
            "New",
            "new@example.test",
            "New question",
            Now);

        Assert.True(await store.TryStoreAsync(incoming));

        var remaining = await db.ContactMessages
            .OrderBy(message => message.Id)
            .ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, message => message.Subject == "Unread");
        Assert.Contains(remaining, message => message.Subject == "New question");
        Assert.DoesNotContain(remaining, message => message.Subject is "Expired" or "Read");
    }

    [Fact]
    public async Task Contact_store_does_not_evict_unread_messages_when_inbox_is_full()
    {
        await using var db = CreateDbContext();
        db.ContactMessages.AddRange(
            Message(1, "Unread one", "one@example.test", "Question one", Now.AddHours(-2)),
            Message(2, "Unread two", "two@example.test", "Question two", Now.AddHours(-1)));
        await db.SaveChangesAsync();
        var store = CreateStore(
            db,
            new ContactMessageStorageOptions
            {
                RetentionDays = 30,
                MaxStoredMessages = 2,
                ReservedAuthenticatedSlots = 0
            });

        var stored = await store.TryStoreAsync(
            Message(0, "Spam", "spam@example.test", "Replacement", Now));

        Assert.False(stored);
        var remaining = await db.ContactMessages.OrderBy(message => message.Id).ToListAsync();
        Assert.Equal([1, 2], remaining.Select(message => message.Id));
    }

    [Fact]
    public async Task Contact_store_reserves_capacity_for_authenticated_customers()
    {
        await using var db = CreateDbContext();
        db.ContactMessages.AddRange(
            Message(1, "Anonymous one", "one@example.test", "Question one", Now.AddHours(-2)),
            Message(2, "Anonymous two", "two@example.test", "Question two", Now.AddHours(-1)));
        await db.SaveChangesAsync();
        var store = CreateStore(
            db,
            new ContactMessageStorageOptions
            {
                RetentionDays = 30,
                MaxStoredMessages = 3,
                ReservedAuthenticatedSlots = 1
            });

        Assert.False(await store.TryStoreAsync(
            Message(0, "Anonymous three", "three@example.test", "Question three", Now)));

        var authenticated = Message(
            0,
            "Signed in",
            "signed-in@example.test",
            "Account support",
            Now);
        authenticated.UserId = "customer-1";
        Assert.True(await store.TryStoreAsync(authenticated));
        Assert.Equal(3, await db.ContactMessages.CountAsync());
    }

    [Fact]
    public async Task Contact_store_caps_unread_messages_per_authenticated_account()
    {
        await using var db = CreateDbContext();
        var first = Message(1, "Signed in", "user@example.test", "First", Now.AddMinutes(-2));
        first.UserId = "customer-1";
        var other = Message(2, "Another", "other@example.test", "Other", Now.AddMinutes(-1));
        other.UserId = "customer-2";
        db.ContactMessages.AddRange(first, other);
        await db.SaveChangesAsync();
        var store = CreateStore(
            db,
            new ContactMessageStorageOptions
            {
                RetentionDays = 30,
                MaxStoredMessages = 100,
                ReservedAuthenticatedSlots = 10,
                MaxUnreadPerAuthenticatedUser = 1
            });
        var blocked = Message(0, "Signed in", "user@example.test", "Second", Now);
        blocked.UserId = "customer-1";

        Assert.False(await store.TryStoreAsync(blocked));
        Assert.Equal(2, await db.ContactMessages.CountAsync());
    }

    [Fact]
    public async Task Contact_post_reports_a_busy_inbox_instead_of_claiming_success()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db, new RejectingContactMessageStore());

        var result = await controller.Index(ValidForm());

        Assert.IsType<ViewResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, controller.Response.StatusCode);
        Assert.Equal("60", controller.Response.Headers.RetryAfter);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(db.ContactMessages);
    }

    [Fact]
    public async Task Contact_post_requires_human_verification_for_anonymous_senders()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(
            db,
            humanChallenge: new StubHumanChallengeVerifier(allowed: false));

        var result = await controller.Index(ValidForm());

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(db.ContactMessages);
    }

    [Fact]
    public async Task Contact_post_rejects_invalid_model_and_silently_discards_honeypot()
    {
        await using var db = CreateDbContext();
        var invalidController = CreateController(db);
        var invalidForm = ValidForm();
        invalidForm.Email = "not-an-email";
        var invalidResult = await invalidController.Index(invalidForm);

        Assert.IsType<ViewResult>(invalidResult);
        Assert.Empty(db.ContactMessages);

        var botController = CreateController(db);
        var botForm = ValidForm();
        botForm.Website = "https://spam.example";
        var botResult = await botController.Index(botForm);

        Assert.IsType<RedirectToActionResult>(botResult);
        Assert.Empty(db.ContactMessages);
    }

    [Fact]
    public void Contact_post_is_antiforgery_protected_and_rate_limited()
    {
        var methods = typeof(ContactController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == nameof(ContactController.Index))
            .ToArray();
        var post = Assert.Single(methods, method =>
            method.GetCustomAttributes(typeof(HttpPostAttribute), true).Length == 1);
        var get = Assert.Single(methods, method =>
            method.GetCustomAttributes(typeof(HttpGetAttribute), true).Length == 1);

        Assert.Single(post.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        var rateLimit = Assert.IsType<EnableRateLimitingAttribute>(
            Assert.Single(post.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)));
        Assert.Equal("contact", rateLimit.PolicyName);
        var requestLimit = Assert.IsType<RequestSizeLimitAttribute>(
            Assert.Single(post.GetCustomAttributes(typeof(RequestSizeLimitAttribute), true)));
        Assert.Equal(
            64 * 1024,
            ((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)requestLimit)
            .MaxRequestBodySize);
        Assert.Empty(get.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.Empty(get.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true));
    }

    [Fact]
    public async Task Admin_contact_inbox_orders_unread_first_and_updates_read_state_idempotently()
    {
        await using var db = CreateDbContext();
        db.ContactMessages.AddRange(
            Message(1, "Alice", "alice@example.test", "Older unread", Now.AddHours(-3)),
            Message(2, "Beta", "beta@example.test", "Newest unread", Now.AddHours(-1)),
            Message(3, "Charlie", "charlie@example.test", "Read message", Now, Now.AddMinutes(-10)));
        await db.SaveChangesAsync();
        var service = new AdminContactMessageService(db, new FixedTimeProvider(Now));

        var inbox = await service.LoadInboxAsync("all", null, 1);

        Assert.Equal(3, inbox.TotalMessages);
        Assert.Equal(2, inbox.UnreadMessages);
        Assert.Equal(new long[] { 2, 1, 3 }, inbox.Messages.Select(message => message.Id));
        Assert.False(inbox.Messages[0].IsRead);

        Assert.True(await service.SetReadStateAsync(2, true));
        var firstReadAt = (await db.ContactMessages.SingleAsync(message => message.Id == 2)).ReadAt;
        Assert.Equal(Now, firstReadAt);
        Assert.True(await service.SetReadStateAsync(2, true));
        Assert.Equal(firstReadAt, (await db.ContactMessages.SingleAsync(message => message.Id == 2)).ReadAt);
        Assert.True(await service.SetReadStateAsync(2, false));
        Assert.Null((await db.ContactMessages.SingleAsync(message => message.Id == 2)).ReadAt);
        Assert.False(await service.SetReadStateAsync(999, true));

        var search = await service.LoadInboxAsync("unread", "  ALICE  ", 999);
        Assert.Equal("unread", search.Status);
        Assert.Equal("ALICE", search.Search);
        Assert.Equal(1, search.Page);
        Assert.Equal(1, Assert.Single(search.Messages).Id);
    }

    [Fact]
    public async Task Contact_schema_enforces_lengths_indexes_and_nullable_account_link()
    {
        await using var db = CreateDbContext();
        var entity = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType>(
            db.Model.FindEntityType(typeof(ContactMessage)));

        Assert.Equal(100, entity.FindProperty(nameof(ContactMessage.FullName))!.GetMaxLength());
        Assert.Equal(320, entity.FindProperty(nameof(ContactMessage.Email))!.GetMaxLength());
        Assert.Equal(40, entity.FindProperty(nameof(ContactMessage.Topic))!.GetMaxLength());
        Assert.Equal(160, entity.FindProperty(nameof(ContactMessage.Subject))!.GetMaxLength());
        Assert.Equal(4000, entity.FindProperty(nameof(ContactMessage.Body))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ContactMessage.ReadAt), nameof(ContactMessage.CreatedAt)]));
        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(DeleteBehavior.SetNull, foreignKey.DeleteBehavior);
        Assert.True(entity.FindProperty(nameof(ContactMessage.UserId))!.IsNullable);
    }

    [Fact]
    public void Contact_and_admin_views_keep_the_message_flow_visible_and_encoded()
    {
        var contact = ReadSource("src", "ApplyWise.Web", "Views", "Contact", "Index.cshtml");
        var product = ReadSource("src", "ApplyWise.Web", "Views", "Home", "Product.cshtml");
        var footer = ReadSource("src", "ApplyWise.Web", "Views", "Home", "_MarketingFooter.cshtml");
        var adminInbox = ReadSource("src", "ApplyWise.Web", "Views", "Admin", "ContactMessages.cshtml");
        var adminDetail = ReadSource("src", "ApplyWise.Web", "Views", "Admin", "ContactMessage.cshtml");

        Assert.Contains("asp-controller=\"Contact\"", footer, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", contact, StringComparison.Ordinal);
        Assert.Contains("asp-validation-summary=\"ModelOnly\"", contact, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"name\"", contact, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"email\"", contact, StringComparison.Ordinal);
        Assert.Contains("_ValidationScriptsPartial", contact, StringComparison.Ordinal);
        Assert.Contains("Contact messages", adminInbox, StringComparison.Ordinal);
        Assert.Contains("admin-contact-message", adminDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("Html.Raw", adminInbox, StringComparison.Ordinal);
        Assert.DoesNotContain("Html.Raw", adminDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("What it is not", product, StringComparison.Ordinal);
        Assert.DoesNotContain("aw-product-boundary", product, StringComparison.Ordinal);
    }

    private static IReadOnlyList<ValidationResult> Validate(ContactFormViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        return results;
    }

    private static ContactFormViewModel ValidForm() => new()
    {
        FullName = "Taylor Candidate",
        Email = "taylor@example.test",
        Topic = ContactTopic.Other,
        Subject = "A valid question",
        Message = "This message contains enough detail to be valid."
    };

    private static ContactMessage Message(
        long id,
        string fullName,
        string email,
        string subject,
        DateTimeOffset createdAt,
        DateTimeOffset? readAt = null) => new()
        {
            Id = id,
            FullName = fullName,
            Email = email,
            Topic = ContactTopic.TechnicalSupport,
            Subject = subject,
            Body = $"Details for {subject}.",
            CreatedAt = createdAt,
            ReadAt = readAt
        };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("contact-messages-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ContactController CreateController(
        ApplicationDbContext db,
        IContactMessageStore? store = null,
        IHumanChallengeVerifier? humanChallenge = null)
    {
        var controller = new ContactController(
            db,
            new FixedTimeProvider(Now),
            store ?? CreateStore(db),
            humanChallenge ?? new StubHumanChallengeVerifier(allowed: true));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static ContactMessageStore CreateStore(
        ApplicationDbContext db,
        ContactMessageStorageOptions? options = null) =>
        new(
            db,
            new WorkspaceQuotaGate(db),
            Options.Create(options ?? new ContactMessageStorageOptions()),
            new FixedTimeProvider(Now),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContactMessageStore>.Instance);

    private static string ReadSource(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. relativePath]));

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApplyWise.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate ApplyWise.sln above '{AppContext.BaseDirectory}'.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RejectingContactMessageStore : IContactMessageStore
    {
        public Task<bool> TryStoreAsync(
            ContactMessage message,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> CleanupAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class StubHumanChallengeVerifier(bool allowed) : IHumanChallengeVerifier
    {
        public bool Enabled => true;
        public string SiteKey => "test-site-key";

        public Task<bool> VerifyAsync(
            HttpContext context,
            string expectedAction,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(allowed);
    }
}
