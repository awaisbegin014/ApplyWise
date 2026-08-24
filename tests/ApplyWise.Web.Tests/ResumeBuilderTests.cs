using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Models.ResumeBuilder;
using ApplyWise.Web.ViewModels.ResumeBuilder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ResumeBuilderTests
{
    [Fact]
    public void Sample_factory_covers_the_complete_versioned_schema()
    {
        var sample = ResumeSampleFactory.Create();

        Assert.Equal(5, ResumeDocument.CurrentSchemaVersion);
        Assert.Equal(ResumeDocument.CurrentSchemaVersion, sample.SchemaVersion);
        Assert.Equal(ResumeDocument.DefaultTemplateId, sample.TemplateId);
        Assert.True(sample.TemplateSelectionConfirmed);
        Assert.Equal(string.Empty, sample.PersonalInformation.ProfilePhotoDataUrl);
        Assert.All(
            new[]
            {
                sample.PersonalInformation.FullName,
                sample.PersonalInformation.ProfessionalTitle,
                sample.PersonalInformation.PhoneNumber,
                sample.PersonalInformation.EmailAddress,
                sample.PersonalInformation.Location,
                sample.PersonalInformation.LinkedInUrl,
                sample.PersonalInformation.GitHubUrl,
                sample.PersonalInformation.PortfolioUrl,
                sample.ProfessionalSummary
            },
            value => Assert.False(string.IsNullOrWhiteSpace(value)));

        Assert.NotEmpty(sample.Education);
        Assert.Equal(2, sample.Education.Count);
        Assert.All(sample.Education, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id));
            Assert.False(string.IsNullOrWhiteSpace(entry.InstitutionName));
            Assert.False(string.IsNullOrWhiteSpace(entry.Degree));
            Assert.False(string.IsNullOrWhiteSpace(entry.FieldOfStudy));
            Assert.False(string.IsNullOrWhiteSpace(entry.StartDate));
        });

        Assert.NotEmpty(sample.Experience);
        Assert.Equal(3, sample.Experience.Count);
        Assert.All(sample.Experience, entry =>
        {
            Assert.NotEmpty(entry.BulletPoints);
            Assert.False(string.IsNullOrWhiteSpace(entry.TechnologySkills));
        });
        Assert.NotEmpty(sample.Projects);
        Assert.Equal(3, sample.Projects.Count);
        Assert.All(sample.Projects, entry =>
        {
            Assert.NotEmpty(entry.TechnologiesUsed);
            Assert.NotEmpty(entry.BulletPoints);
        });
        Assert.NotEmpty(sample.Skills);
        Assert.All(sample.Skills, category =>
        {
            Assert.NotEmpty(category.Skills);
            Assert.All(category.Skills, skill =>
            {
                Assert.False(string.IsNullOrWhiteSpace(skill.Id));
                Assert.False(string.IsNullOrWhiteSpace(skill.Name));
                Assert.InRange(skill.Level ?? 0, 1, 5);
            });
        });
        Assert.NotEmpty(sample.AchievementsAndCertifications);
        Assert.Equal(3, sample.AchievementsAndCertifications.Count);
        Assert.Contains("[b]", sample.ProfessionalSummary, StringComparison.Ordinal);
        Assert.NotEmpty(sample.Languages);
        Assert.All(sample.Languages, language => Assert.InRange(language.Level ?? 0, 1, 5));
        Assert.NotEmpty(sample.VolunteerExperience);
        Assert.All(sample.VolunteerExperience, entry => Assert.NotEmpty(entry.BulletPoints));
        Assert.Equal(2, sample.References.Count);
        Assert.All(sample.References, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id));
            Assert.False(string.IsNullOrWhiteSpace(entry.FullName));
            Assert.False(string.IsNullOrWhiteSpace(entry.JobTitle));
            Assert.False(string.IsNullOrWhiteSpace(entry.Company));
            Assert.False(string.IsNullOrWhiteSpace(entry.EmailAddress));
            Assert.False(string.IsNullOrWhiteSpace(entry.PhoneNumber));
        });
        Assert.NotEmpty(sample.Interests);
        Assert.NotEmpty(sample.CustomSections);
        Assert.All(sample.CustomSections, section => Assert.NotEmpty(section.Entries));
    }

    [Fact]
    public void Sample_sections_are_unique_in_catalog_order_and_keep_optional_sections_hidden()
    {
        var sample = ResumeSampleFactory.Create();
        var expectedOrder = Enum.GetValues<ResumeSectionKind>();

        Assert.Equal(expectedOrder, sample.Sections.Select(section => section.Key));
        Assert.Equal(sample.Sections.Count, sample.Sections.Select(section => section.Key).Distinct().Count());
        Assert.All(sample.Sections, section => Assert.False(string.IsNullOrWhiteSpace(section.Title)));

        var defaults = ResumeSectionCatalog.CreateDefault();
        Assert.Equal(expectedOrder, defaults.Select(section => section.Key));
        Assert.Equal(defaults.Select(section => section.IsVisible), sample.Sections.Select(section => section.IsVisible));
        Assert.All(
            sample.Sections.Where(section => section.Key is ResumeSectionKind.ProfessionalSummary
                or ResumeSectionKind.Education
                or ResumeSectionKind.Skills
                or ResumeSectionKind.Experience
                or ResumeSectionKind.Projects
                or ResumeSectionKind.AchievementsAndCertifications),
            section => Assert.True(section.IsVisible));
        Assert.All(
            defaults.Where(section => section.Key is ResumeSectionKind.Languages
                or ResumeSectionKind.VolunteerExperience
                or ResumeSectionKind.References
                or ResumeSectionKind.Interests
                or ResumeSectionKind.CustomSections),
            section => Assert.False(section.IsVisible));
    }

    [Fact]
    public void Sample_factory_returns_independent_mutable_collections()
    {
        var first = ResumeSampleFactory.Create();
        var second = ResumeSampleFactory.Create();

        first.Sections.RemoveAt(0);
        first.Education.Clear();
        first.References.Clear();

        Assert.Equal(Enum.GetValues<ResumeSectionKind>().Length, second.Sections.Count);
        Assert.NotEmpty(second.Education);
        Assert.NotEmpty(second.References);
    }

    [Fact]
    public void Page_model_serializes_browser_friendly_sample_json()
    {
        var model = ResumeBuilderPageViewModel.CreateForAccount("json-test-account");
        using var json = JsonDocument.Parse(model.SampleResumeJson);
        var root = json.RootElement;

        Assert.Equal(ResumeDocument.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(ResumeDocument.DefaultTemplateId, root.GetProperty("templateId").GetString());
        Assert.True(root.GetProperty("templateSelectionConfirmed").GetBoolean());
        var personalInformation = root.GetProperty("personalInformation");
        Assert.Equal("Jordan Lee", personalInformation.GetProperty("fullName").GetString());
        Assert.Equal(string.Empty, personalInformation.GetProperty("profilePhotoDataUrl").GetString());
        Assert.Equal(
            "professionalSummary",
            root.GetProperty("sections")[0].GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("customSections").ValueKind);
        Assert.Equal(2, root.GetProperty("references").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("experience")[0].GetProperty("technologySkills").GetString()));
        var firstSkill = root.GetProperty("skills")[0].GetProperty("skills")[0];
        Assert.Equal(JsonValueKind.Object, firstSkill.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(firstSkill.GetProperty("name").GetString()));
        Assert.InRange(firstSkill.GetProperty("level").GetInt32(), 1, 5);
    }

    [Theory]
    [InlineData("personalInformation", "personalInformation")]
    [InlineData(" PROFESSIONALSUMMARY ", "professionalSummary")]
    [InlineData("experience", "experience")]
    [InlineData("projects", "projects")]
    [InlineData("education", "education")]
    [InlineData("skills", "skills")]
    [InlineData("references", "references")]
    [InlineData("achievements", "achievementsAndCertifications")]
    [InlineData("certifications", "achievementsAndCertifications")]
    [InlineData("achievementsAndCertifications", "achievementsAndCertifications")]
    [InlineData("", null)]
    [InlineData("experience.other", null)]
    [InlineData("<script>alert(1)</script>", null)]
    public void Initial_section_is_normalized_to_a_fixed_editor_target(string? requested, string? expected)
    {
        var model = ResumeBuilderPageViewModel.CreateForAccount("section-test-account", requested);

        Assert.Equal(expected, model.InitialSection);
        Assert.Equal(expected, ResumeBuilderPageViewModel.NormalizeInitialSection(requested));
    }

    [Fact]
    public void Initial_section_rejects_oversized_values()
    {
        var requested = new string('a', 65);

        Assert.Null(ResumeBuilderPageViewModel.NormalizeInitialSection(requested));
        Assert.Null(ResumeBuilderPageViewModel.CreateForAccount("section-test-account", requested).InitialSection);
    }

    [Fact]
    public void Draft_storage_keys_are_deterministic_safe_and_account_isolated()
    {
        const string firstAccount = "identity-user/one@example.test";
        const string secondAccount = "identity-user/two@example.test";

        var firstKey = ResumeBuilderPageViewModel.CreateDraftStorageKey(firstAccount);
        var repeatedKey = ResumeBuilderPageViewModel.CreateDraftStorageKey(firstAccount);
        var secondKey = ResumeBuilderPageViewModel.CreateDraftStorageKey(secondAccount);

        Assert.Equal(firstKey, repeatedKey);
        Assert.NotEqual(firstKey, secondKey);
        Assert.StartsWith(ResumeBuilderPageViewModel.DraftStorageKeyPrefix, firstKey);
        Assert.Matches("^[a-z0-9:-]+$", firstKey);
        Assert.False(firstKey.Contains(firstAccount, StringComparison.OrdinalIgnoreCase));
        Assert.False(secondKey.Contains(secondAccount, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Controller_is_authorized_attribute_routed_and_get_only()
    {
        var controllerType = typeof(ResumeBuilderController);
        Assert.NotNull(controllerType.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("resume-builder", controllerType.GetCustomAttribute<RouteAttribute>()?.Template);

        var action = Assert.Single(controllerType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(ResumeBuilderController.Index), action.Name);
        var httpMethod = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());
        var get = Assert.IsType<HttpGetAttribute>(httpMethod);
        Assert.Equal(string.Empty, get.Template);
        Assert.Equal(["GET"], get.HttpMethods);
        var parameter = Assert.Single(action.GetParameters());
        Assert.Equal("section", parameter.Name);
        Assert.True(parameter.HasDefaultValue);
        Assert.Null(parameter.DefaultValue);
        Assert.NotNull(parameter.GetCustomAttribute<FromQueryAttribute>());

        var constructor = Assert.Single(controllerType.GetConstructors());
        Assert.Empty(constructor.GetParameters());
    }

    [Fact]
    public void Authenticated_controller_returns_account_scoped_page_model_without_storage_services()
    {
        const string accountId = "controller-test-account";
        var controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, accountId)],
            authenticationType: "Test")));

        var result = Assert.IsType<ViewResult>(controller.Index());
        var model = Assert.IsType<ResumeBuilderPageViewModel>(result.Model);

        Assert.Equal(ResumeBuilderPageViewModel.CreateDraftStorageKey(accountId), model.DraftStorageKey);
        Assert.False(string.IsNullOrWhiteSpace(model.SampleResumeJson));

        var targetedResult = Assert.IsType<ViewResult>(controller.Index("certifications"));
        var targetedModel = Assert.IsType<ResumeBuilderPageViewModel>(targetedResult.Model);
        Assert.Equal("achievementsAndCertifications", targetedModel.InitialSection);

        var invalidResult = Assert.IsType<ViewResult>(controller.Index("experience\"] [data-action=\"clear"));
        var invalidModel = Assert.IsType<ResumeBuilderPageViewModel>(invalidResult.Model);
        Assert.Null(invalidModel.InitialSection);
    }

    [Fact]
    public void Controller_challenges_a_principal_without_an_account_identifier()
    {
        var controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.IsType<ChallengeResult>(controller.Index());
    }

    private static ResumeBuilderController CreateController(ClaimsPrincipal user) => new()
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        }
    };
}
