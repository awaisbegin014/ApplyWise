namespace ApplyWise.Web.ViewModels.Home;

public sealed record MarketingProductFeature(
    string Icon,
    string Title,
    string Description);

public sealed record MarketingProductStep(
    string Title,
    string Description);

public sealed record MarketingProductPageViewModel(
    string Accent,
    string MetaTitle,
    string MetaDescription,
    string Eyebrow,
    string Title,
    string Lead,
    string DefinitionTitle,
    string Definition,
    string DesignedFor,
    IReadOnlyList<MarketingProductFeature> Features,
    IReadOnlyList<MarketingProductStep> Steps,
    string CtaTitle,
    string CtaDescription,
    string ToolController,
    string ToolAction,
    string ToolCtaLabel);

public static class MarketingProductPages
{
    public static MarketingProductPageViewModel JobTracker { get; } = new(
        Accent: "tracker",
        MetaTitle: "Job application tracker",
        MetaDescription: "Understand how ApplyWise keeps applications, deadlines, interviews, next actions, and sent-resume history connected.",
        Eyebrow: "Job tracker",
        Title: "One place for every application and next move.",
        Lead: "ApplyWise turns a scattered job search into a structured, private workspace where every role has context and a clear next action.",
        DefinitionTitle: "A structured record for every opportunity",
        Definition: "The job tracker keeps the company, role, source, status, deadline, notes, interviews, and chosen resume attached to the same application record.",
        DesignedFor: "People managing several active applications who want a reliable history instead of disconnected notes and spreadsheets.",
        Features: new[]
        {
            new MarketingProductFeature("i-grid", "A visible pipeline", "See saved, applied, interview, offer, and closed opportunities without reconstructing your search from memory."),
            new MarketingProductFeature("i-calendar", "Deadlines and next actions", "Keep important dates visible and use contextual prompts to decide what deserves attention next."),
            new MarketingProductFeature("i-file", "Connected application history", "Record the resume version you used and keep interviews, preparation, feedback, and outcomes tied to the role.")
        },
        Steps: new[]
        {
            new MarketingProductStep("Capture the opportunity", "Save the role, company, source, job description, deadline, and any notes you need."),
            new MarketingProductStep("Update the real status", "Move the application as it progresses and add interviews or follow-up information when they happen."),
            new MarketingProductStep("Act on the next move", "Review the suggested next action and open the full history before you follow up or prepare.")
        },
        CtaTitle: "Give every opportunity a dependable home.",
        CtaDescription: "Create a private workspace and start with the role that matters most today.",
        ToolController: "JobApplications",
        ToolAction: "Index",
        ToolCtaLabel: "Open job tracker");

    public static MarketingProductPageViewModel ResumeBuilder { get; } = new(
        Accent: "builder",
        MetaTitle: "Guided resume builder",
        MetaDescription: "See how ApplyWise helps you create a structured resume with live preview and a selectable-text PDF download.",
        Eyebrow: "Resume builder",
        Title: "Build a focused resume you can inspect before sending.",
        Lead: "ApplyWise gives you a guided editing flow, a live document preview, and a professional text-based PDF generated from the same content you review.",
        DefinitionTitle: "A guided editor with an exact PDF preview",
        Definition: "The resume builder organizes your details into conventional sections, shows the resulting document as you work, and downloads the same text-based layout as a PDF.",
        DesignedFor: "People who want a clear, parser-friendly structure without giving up control over wording, evidence, or final review.",
        Features: new[]
        {
            new MarketingProductFeature("i-edit-file", "Guided resume sections", "Work through contact details, summary, experience, education, projects, and skills in a deliberate order."),
            new MarketingProductFeature("i-file", "Live document preview", "Inspect page flow and content before downloading instead of discovering layout problems after export."),
            new MarketingProductFeature("i-arrow", "Selectable-text PDF", "Download a text-based PDF from the same definition used for preview, with your content remaining selectable.")
        },
        Steps: new[]
        {
            new MarketingProductStep("Choose a starting template", "Pick a clear layout that suits the amount and type of evidence you need to present."),
            new MarketingProductStep("Add and review your details", "Write truthful content section by section while the browser keeps a local draft and updates the preview."),
            new MarketingProductStep("Inspect and download", "Review the exact PDF preview, correct any weak or crowded sections, then download the final file.")
        },
        CtaTitle: "Turn your evidence into a usable document.",
        CtaDescription: "Build, preview, and download a focused resume from one guided flow.",
        ToolController: "ResumeBuilder",
        ToolAction: "Index",
        ToolCtaLabel: "Open resume builder");

    public static MarketingProductPageViewModel HowItWorks { get; } = new(
        Accent: "workflow",
        MetaTitle: "How ApplyWise works",
        MetaDescription: "See how ApplyWise connects opportunity tracking, resume evidence, document building, interviews, and next actions.",
        Eyebrow: "How it works",
        Title: "A connected workflow from opportunity to interview.",
        Lead: "ApplyWise keeps the role, the resume decision, and the next action in one continuous record so useful context does not disappear between tools.",
        DefinitionTitle: "A private job-search operating system",
        Definition: "ApplyWise combines an application tracker, explainable resume comparison, a guided resume builder, and interview records around the same opportunity.",
        DesignedFor: "Job seekers who want a repeatable process and a trustworthy history of what they considered, sent, and need to do next.",
        Features: new[]
        {
            new MarketingProductFeature("i-briefcase", "Opportunity context", "Start with the real role, deadline, source, description, and status."),
            new MarketingProductFeature("i-match", "Resume evidence", "Compare saved versions or build a stronger, truthful document for the opportunity."),
            new MarketingProductFeature("i-calendar", "Momentum and history", "Record what you sent, schedule interviews, capture outcomes, and keep the next action visible.")
        },
        Steps: new[]
        {
            new MarketingProductStep("Capture the role", "Create the application record before deadlines, links, and requirements get lost."),
            new MarketingProductStep("Choose your evidence", "Compare existing resumes or build a focused version, then record the one you plan to send."),
            new MarketingProductStep("Keep momentum", "Update the status, prepare for interviews, and follow the next useful action with the full history nearby.")
        },
        CtaTitle: "Create a calmer application process.",
        CtaDescription: "Bring the opportunity, resume, and next action into one private workspace.",
        ToolController: "Dashboard",
        ToolAction: "Index",
        ToolCtaLabel: "Open your workspace");
}
