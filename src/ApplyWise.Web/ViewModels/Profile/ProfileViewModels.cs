using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Profile;

public class ProfileEditViewModel
{
    [Required, StringLength(100, MinimumLength = 2), Display(Name = "Full name")] public string FullName { get; set; } = string.Empty;
    [Display(Name = "Gender (optional)")] public ProfileGender? Gender { get; set; }
    [DataType(DataType.Date), Display(Name = "Date of birth (optional)")] public DateOnly? DateOfBirth { get; set; }
    [Display(Name = "Career stage")] public CareerStage? CareerStage { get; set; }
    [StringLength(180)] public string? Institution { get; set; }
    [StringLength(150), Display(Name = "Degree program")] public string? DegreeProgram { get; set; }
    [StringLength(150), Display(Name = "Field of study")] public string? FieldOfStudy { get; set; }
    [Range(1950, 2200), Display(Name = "Graduation year")] public int? GraduationYear { get; set; }
    [StringLength(80), Display(Name = "Current semester")] public string? CurrentSemester { get; set; }
    [StringLength(500), Display(Name = "Preferred locations")] public string? PreferredLocations { get; set; }
    [StringLength(100), Display(Name = "Preferred work modes")] public string? PreferredWorkModes { get; set; }
    [StringLength(2000)] public string? Skills { get; set; }
    [StringLength(1000), Display(Name = "Career interests")] public string? CareerInterests { get; set; }
    [StringLength(2000), Display(Name = "Academic highlights")] public string? AcademicHighlights { get; set; }
    public string Initials { get; set; } = "AW";
    public string? CurrentAvatarUrl { get; set; }
    public string CurrentAvatarLabel { get; set; } = "Profile picture";
    public IReadOnlyList<ProfileAvatarOption> AvatarOptions { get; set; } = [];
}

public sealed record ProfileAvatarOption(
    string Id,
    string Category,
    string Label,
    string ImageUrl,
    bool IsSelected,
    bool IsRecommended);

public sealed class OnboardingViewModel : ProfileEditViewModel
{
    public bool Skip { get; set; }
}
