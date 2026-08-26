namespace ApplyWise.Web.ViewModels.BestResumePicker;

public sealed record ComparedResumeViewModel(
    int ResumeId,
    int? AnalysisId,
    string VersionName,
    string OriginalFileName,
    int? MatchScore,
    int? Rank,
    bool IsRecommended,
    IReadOnlyList<string> MatchedKeywords,
    IReadOnlyList<string> MissingKeywords,
    IReadOnlyList<string> Suggestions,
    string? AnalysisError);
