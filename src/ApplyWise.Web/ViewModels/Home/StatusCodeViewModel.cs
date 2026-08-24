namespace ApplyWise.Web.ViewModels.Home;

public sealed class StatusCodeViewModel
{
    public int StatusCode { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
}
