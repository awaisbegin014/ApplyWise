using Microsoft.AspNetCore.Mvc.Rendering;
using ApplyWise.Web.Services.Subscriptions;
using ApplyWise.Web.ViewModels.BestResumePicker;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class AnalyzerIndexViewModel
{
    public string Mode { get; set; } = "ats";
    public AtsResumeUploadViewModel Upload { get; set; } = new();
    public SavedAtsAnalysisViewModel SavedAts { get; set; } = new();
    public PastedRequirementsAnalysisViewModel Pasted { get; set; } = new();
    public SavedApplicationAnalysisViewModel Saved { get; set; } = new();
    public IReadOnlyList<SelectListItem> AvailableResumes { get; set; } = [];
    public IReadOnlyList<SelectListItem> AvailableJobApplications { get; set; } = [];
    public AnalysisResultViewModel? LatestResult { get; set; }
    public BestResumePickerResultViewModel? Comparison { get; set; }
    public SubscriptionSnapshot? Subscription { get; set; }
    public bool AiConfigured { get; set; }
}
