using System.Security.Claims;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.ViewModels.Profile;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class OnboardingProfileTests
{
    private const string UserId = "onboarding-profile-user";

    [Fact]
    public void Demographic_labels_explicitly_mark_fields_as_optional()
    {
        var genderLabel = typeof(ProfileEditViewModel)
            .GetProperty(nameof(ProfileEditViewModel.Gender))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), inherit: true)
            .Cast<System.ComponentModel.DataAnnotations.DisplayAttribute>()
            .Single()
            .GetName();
        var dateOfBirthLabel = typeof(ProfileEditViewModel)
            .GetProperty(nameof(ProfileEditViewModel.DateOfBirth))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), inherit: true)
            .Cast<System.ComponentModel.DataAnnotations.DisplayAttribute>()
            .Single()
            .GetName();

        Assert.Contains("optional", genderLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optional", dateOfBirthLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Onboarding_accepts_omitted_optional_demographics()
    {
        await using var scope = await CreateScopeAsync();

        var result = await scope.Controller.Index(new OnboardingViewModel
        {
            FullName = "Candidate Name"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Dashboard", redirect.ControllerName);

        var profile = await scope.Db.CareerProfiles.SingleAsync();
        Assert.Null(profile.Gender);
        Assert.Null(profile.DateOfBirth);
    }

    [Fact]
    public async Task Onboarding_persists_optional_demographics_when_provided()
    {
        await using var scope = await CreateScopeAsync();
        var dateOfBirth = new DateOnly(2000, 1, 2);

        var result = await scope.Controller.Index(new OnboardingViewModel
        {
            FullName = "Candidate Name",
            Gender = ProfileGender.NonBinary,
            DateOfBirth = dateOfBirth
        });

        Assert.IsType<RedirectToActionResult>(result);
        var profile = await scope.Db.CareerProfiles.SingleAsync();
        Assert.Equal(ProfileGender.NonBinary, profile.Gender);
        Assert.Equal(dateOfBirth, profile.DateOfBirth);
    }

    [Fact]
    public async Task Onboarding_rejects_an_invalid_optional_date_of_birth()
    {
        await using var scope = await CreateScopeAsync();

        var result = await scope.Controller.Index(new OnboardingViewModel
        {
            FullName = "Candidate Name",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(
            nameof(OnboardingViewModel.DateOfBirth),
            scope.Controller.ModelState.Keys);
        Assert.Empty(scope.Db.CareerProfiles);
    }

    private static async Task<ControllerScope> CreateScopeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(
                "onboarding-profile-" + Guid.NewGuid().ToString("N")));
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new IdentityUser
        {
            Id = UserId,
            UserName = "candidate@example.test",
            Email = "candidate@example.test"
        });
        await db.SaveChangesAsync();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId)],
                authenticationType: "Test"))
        };
        var controller = new OnboardingController(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            new NoOpProductEventRecorder())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            }
        };

        return new ControllerScope(provider, db, controller);
    }

    private sealed class NoOpProductEventRecorder : IProductEventRecorder
    {
        public Task RecordAsync(
            string name,
            string source,
            string? userId = null,
            bool succeeded = true,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordLoginAsync(
            string userId,
            string source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ControllerScope(
        ServiceProvider provider,
        ApplicationDbContext db,
        OnboardingController controller) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public OnboardingController Controller { get; } = controller;

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
