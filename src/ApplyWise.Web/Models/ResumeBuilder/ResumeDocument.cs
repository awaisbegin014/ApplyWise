namespace ApplyWise.Web.Models.ResumeBuilder;

/// <summary>
/// Versioned, client-serializable state for a locally stored resume draft.
/// Dates use the HTML month-input format (yyyy-MM) so the browser can edit
/// them without culture-dependent conversions.
/// </summary>
public sealed class ResumeDocument
{
    public const int CurrentSchemaVersion = 5;
    public const string DefaultTemplateId = "classic";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string TemplateId { get; init; } = DefaultTemplateId;
    public bool TemplateSelectionConfirmed { get; init; }
    public PersonalInformation PersonalInformation { get; init; } = new();
    public string ProfessionalSummary { get; init; } = string.Empty;
    public List<EducationEntry> Education { get; init; } = [];
    public List<ExperienceEntry> Experience { get; init; } = [];
    public List<ProjectEntry> Projects { get; init; } = [];
    public List<SkillCategory> Skills { get; init; } = [];
    public List<AchievementCertificationEntry> AchievementsAndCertifications { get; init; } = [];
    public List<LanguageEntry> Languages { get; init; } = [];
    public List<VolunteerExperienceEntry> VolunteerExperience { get; init; } = [];
    public List<ReferenceEntry> References { get; init; } = [];
    public List<string> Interests { get; init; } = [];
    public List<CustomSection> CustomSections { get; init; } = [];
    public List<ResumeSectionDescriptor> Sections { get; init; } = ResumeSectionCatalog.CreateDefault();
}

public sealed class PersonalInformation
{
    public string FullName { get; init; } = string.Empty;
    public string ProfessionalTitle { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string EmailAddress { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string LinkedInUrl { get; init; } = string.Empty;
    public string GitHubUrl { get; init; } = string.Empty;
    public string PortfolioUrl { get; init; } = string.Empty;
    public string ProfilePhotoDataUrl { get; init; } = string.Empty;
}

public sealed class EducationEntry
{
    public string Id { get; init; } = string.Empty;
    public string InstitutionName { get; init; } = string.Empty;
    public string Degree { get; init; } = string.Empty;
    public string FieldOfStudy { get; init; } = string.Empty;
    public string StartDate { get; init; } = string.Empty;
    public string EndDate { get; init; } = string.Empty;
    public bool IsCurrentlyStudying { get; init; }
    public string Location { get; init; } = string.Empty;
    public string Grade { get; init; } = string.Empty;
    public string DescriptionOrCoursework { get; init; } = string.Empty;
}

public sealed class ExperienceEntry
{
    public string Id { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;
    public string EmploymentType { get; init; } = string.Empty;
    public string TechnologySkills { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string StartDate { get; init; } = string.Empty;
    public string EndDate { get; init; } = string.Empty;
    public bool IsCurrentlyWorking { get; init; }
    public List<string> BulletPoints { get; init; } = [];
}

public sealed class ProjectEntry
{
    public string Id { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectUrl { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;
    public List<string> TechnologiesUsed { get; init; } = [];
    public string StartDate { get; init; } = string.Empty;
    public string EndDate { get; init; } = string.Empty;
    public bool IsOngoing { get; init; }
    public List<string> BulletPoints { get; init; } = [];
}

public sealed class SkillCategory
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<SkillItem> Skills { get; init; } = [];
}

public sealed class SkillItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int? Level { get; init; }
}

public sealed class AchievementCertificationEntry
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string IssuingOrganization { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string CredentialUrl { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed class LanguageEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Proficiency { get; init; } = string.Empty;
    public int? Level { get; init; }
}

public sealed class VolunteerExperienceEntry
{
    public string Id { get; init; } = string.Empty;
    public string OrganizationName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string StartDate { get; init; } = string.Empty;
    public string EndDate { get; init; } = string.Empty;
    public bool IsCurrentlyVolunteering { get; init; }
    public List<string> BulletPoints { get; init; } = [];
}

public sealed class ReferenceEntry
{
    public string Id { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string EmailAddress { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
}

public sealed class CustomSection
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public List<CustomSectionEntry> Entries { get; init; } = [];
}

public sealed class CustomSectionEntry
{
    public string Id { get; init; } = string.Empty;
    public string Heading { get; init; } = string.Empty;
    public string Subheading { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string StartDate { get; init; } = string.Empty;
    public string EndDate { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public string Url { get; init; } = string.Empty;
    public List<string> BulletPoints { get; init; } = [];
}
