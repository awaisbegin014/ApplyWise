namespace ApplyWise.Web.Services.Subscriptions;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    public int FreeAiTrialLimit { get; set; } = 2;
    public int ProMonthlyAiLimit { get; set; } = 100;
    public int ProDurationDays { get; set; } = 30;
    public decimal ProPrice { get; set; } = 500m;
    public string Currency { get; set; } = "PKR";
    public string PaymentInstructions { get; set; } =
        "Pay using an available transfer method, then submit the transaction reference for verification.";
}
