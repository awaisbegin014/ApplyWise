using System.Text.RegularExpressions;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class PublicUiContractTests
{
    [Fact]
    public void Gmail_import_status_uses_browser_local_time_and_explains_the_polling_interval()
    {
        var imports = ReadSource(
            "src",
            "ApplyWise.Web",
            "Views",
            "ApplicationImports",
            "Index.cshtml");
        var siteScript = ReadSource(
            "src",
            "ApplyWise.Web",
            "wwwroot",
            "js",
            "site.js");

        Assert.Contains("data-local-date-time", imports, StringComparison.Ordinal);
        Assert.Contains("ToUniversalTime().ToString(\"O\")", imports, StringComparison.Ordinal);
        Assert.Contains(
            "New confirmations are checked automatically about every @Model.AutomaticSyncIntervalMinutes minutes.",
            imports,
            StringComparison.Ordinal);
        Assert.Contains("time[data-local-date-time]", siteScript, StringComparison.Ordinal);
        Assert.Contains("new Intl.DateTimeFormat", siteScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Contact_form_exposes_visible_and_native_required_field_semantics()
    {
        var contact = ReadSource("src", "ApplyWise.Web", "Views", "Contact", "Index.cshtml");

        Assert.Equal(5, Regex.Matches(contact, "aw-required-marker").Count);
        Assert.Equal(5, Regex.Matches(contact, "required aria-required=\"true\"").Count);
    }

    [Fact]
    public void Homepage_uses_semantic_ordered_navigation_and_a_dedicated_tracking_cta()
    {
        var layout = ReadSource("src", "ApplyWise.Web", "Views", "Shared", "_Layout.cshtml");

        Assert.Contains(
            "<nav class=\"aw-home-nav\" id=\"public-navigation\" aria-label=\"Primary navigation\" data-home-nav>",
            layout,
            StringComparison.Ordinal);
        AssertBefore(layout, "asp-action=\"JobTracker\"", "asp-action=\"ResumeBuilder\"");
        AssertBefore(layout, "asp-action=\"ResumeBuilder\"", "asp-action=\"HowItWorks\"");
        AssertBefore(layout, "asp-action=\"HowItWorks\"", "asp-controller=\"Contact\" asp-action=\"Index\"");

        var trackingCta = Regex.Match(
            layout,
            "<a\\s+class=\"(?<classes>[^\"]*\\baw-home-nav-cta\\b[^\"]*)\"[^>]*>Start tracking</a>",
            RegexOptions.CultureInvariant);

        Assert.True(trackingCta.Success, "The homepage navigation must expose its Start tracking CTA.");
        Assert.DoesNotContain("btn-primary", trackingCta.Groups["classes"].Value, StringComparison.Ordinal);
        Assert.Contains(">Sign in</a>", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Homepage_navigation_shell_uses_the_product_palette_and_is_fully_rounded()
    {
        var homeStyles = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "home.css");
        var shellRule = Regex.Match(
            homeStyles,
            @"\.aw-home-nav-shell\s*\{(?<declarations>[^}]*)\}",
            RegexOptions.CultureInvariant);

        Assert.True(shellRule.Success, "The homepage must define its floating navigation shell.");
        Assert.Matches(
            @"(?m)^\s*border-radius\s*:\s*999px\s*;",
            shellRule.Groups["declarations"].Value);
        Assert.Matches(
            @"(?m)^\s*background\s*:\s*var\(--aw-home-nav-bg\)\s*;",
            shellRule.Groups["declarations"].Value);
        Assert.Contains(
            "--aw-home-nav-bg: linear-gradient(110deg, rgba(255, 255, 255, .98) 0%, #eff6ff 70%, #ecfdf5 100%);",
            homeStyles,
            StringComparison.Ordinal);
        Assert.Contains("--aw-home-nav-cta-bg: var(--aw-primary, #2563eb);", homeStyles, StringComparison.Ordinal);
        Assert.Contains("min-height: 88px;", shellRule.Groups["declarations"].Value, StringComparison.Ordinal);
        Assert.Contains("font-size: 1.125rem;", homeStyles, StringComparison.Ordinal);
        Assert.Contains("font-weight: 500;", homeStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void Mobile_navigation_uses_the_1080px_boundary_with_progressive_enhancement()
    {
        var homeStyles = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "home.css");
        var homeScript = ReadSource("src", "ApplyWise.Web", "wwwroot", "js", "home.js");

        Assert.Contains("@media (max-width: 1080px)", homeStyles, StringComparison.Ordinal);
        Assert.Contains(
            ".aw-public-header.is-nav-ready .aw-home-nav { display: none; }",
            homeStyles,
            StringComparison.Ordinal);
        Assert.Contains(
            ".aw-public-header.is-nav-ready.is-open .aw-home-nav { display: flex; }",
            homeStyles,
            StringComparison.Ordinal);
        Assert.Contains("window.matchMedia('(max-width: 1080px)')", homeScript, StringComparison.Ordinal);
        AssertBefore(homeScript, "toggle.hidden = false;", "header.classList.add('is-nav-ready');");
    }

    [Fact]
    public void Product_navigation_opens_three_distinct_explanatory_pages()
    {
        var controller = ReadSource("src", "ApplyWise.Web", "Controllers", "HomeController.cs");
        var layout = ReadSource("src", "ApplyWise.Web", "Views", "Shared", "_Layout.cshtml");
        var homepage = ReadSource("src", "ApplyWise.Web", "Views", "Home", "Index.cshtml");
        var productPage = ReadSource("src", "ApplyWise.Web", "Views", "Home", "Product.cshtml");
        var productContent = ReadSource("src", "ApplyWise.Web", "ViewModels", "Home", "MarketingProductPageViewModel.cs");

        Assert.Contains("[HttpGet(\"/product/job-tracker\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"/product/resume-builder\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"/product/how-it-works\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("/product/resume-match", controller, StringComparison.Ordinal);

        Assert.Contains("asp-action=\"JobTracker\"", layout, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"ResumeBuilder\"", layout, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"HowItWorks\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-action=\"ResumeMatch\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"#job-tracker\">Job tracker", layout, StringComparison.Ordinal);

        Assert.Contains("asp-action=\"JobTracker\"", homepage, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"ResumeAnalyzer\" asp-action=\"Index\"", homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-action=\"ResumeMatch\"", homepage, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"ResumeBuilder\"", homepage, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"HowItWorks\"", homepage, StringComparison.Ordinal);

        Assert.Contains(">What it is</span>", productPage, StringComparison.Ordinal);
        Assert.Contains("<section class=\"aw-product-brief\"", productPage, StringComparison.Ordinal);
        Assert.Contains("<dl class=\"aw-product-audience\">", productPage, StringComparison.Ordinal);
        Assert.DoesNotContain("aw-product-definition", productPage, StringComparison.Ordinal);
        Assert.Contains(">What you can do</span>", productPage, StringComparison.Ordinal);
        Assert.Contains(">How it works</span>", productPage, StringComparison.Ordinal);
        Assert.DoesNotContain("What it is not", productPage, StringComparison.Ordinal);
        Assert.DoesNotContain("BoundaryTitle", productContent, StringComparison.Ordinal);
        Assert.DoesNotContain("BoundaryDescription", productContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Homepage_leads_with_tracking_and_orders_the_workflow_track_match_apply()
    {
        var homepage = ReadSource("src", "ApplyWise.Web", "Views", "Home", "Index.cshtml");

        Assert.Contains(
            "<h1 id=\"home-hero-title\">Track every opportunity.",
            homepage,
            StringComparison.Ordinal);
        Assert.Contains(
            ">Start tracking free</a>",
            homepage,
            StringComparison.Ordinal);
        Assert.DoesNotContain("aw-home-hero-path", homepage, StringComparison.Ordinal);
        AssertBefore(
            homepage,
            "Track every opportunity.",
            "Apply with the right resume.");
        AssertBefore(homepage, "<h3>Capture the role</h3>", "<h3>Choose your evidence</h3>");
        AssertBefore(homepage, "<h3>Choose your evidence</h3>", "<h3>Keep momentum</h3>");
    }

    [Fact]
    public void Homepage_hero_uses_wiso_without_an_image_card()
    {
        var homepage = ReadSource("src", "ApplyWise.Web", "Views", "Home", "Index.cshtml");

        Assert.Contains(
            "<figure class=\"aw-home-wiso-visual\">",
            homepage,
            StringComparison.Ordinal);
        Assert.Contains("<div class=\"aw-home-wiso-stage\">", homepage, StringComparison.Ordinal);
        Assert.Contains("src=\"~/images/wiso-standalone.png\"", homepage, StringComparison.Ordinal);
        Assert.Contains("width=\"1254\"", homepage, StringComparison.Ordinal);
        Assert.Contains("height=\"1254\"", homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("aw-home-wiso-orbit", homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("aw-home-wiso-halo", homepage, StringComparison.Ordinal);
        Assert.Contains("decoding=\"async\"", homepage, StringComparison.Ordinal);
        Assert.Contains("fetchpriority=\"high\"", homepage, StringComparison.Ordinal);
        Assert.Contains(
            "alt=\"Wiso, the ApplyWise career guide, holding a resume checklist\"",
            homepage,
            StringComparison.Ordinal);
        Assert.DoesNotContain("home-career-workspace.webp", homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("aw-home-editorial", homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("aw-home-preview", homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("unsplash.com", homepage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Homepage_places_clear_privacy_context_next_to_both_conversion_points()
    {
        var homepage = ReadSource("src", "ApplyWise.Web", "Views", "Home", "Index.cshtml");

        AssertBefore(homepage, "Start tracking free</a>", "<p class=\"aw-home-assurance\">");
        Assert.Contains("Resume analysis uses local, explainable rules", homepage, StringComparison.Ordinal);
        Assert.Contains("does not send your resume text to an external AI service", homepage, StringComparison.Ordinal);
        Assert.Contains("Read our privacy approach", homepage, StringComparison.Ordinal);
        Assert.Contains("Create your free workspace</a>", homepage, StringComparison.Ordinal);
        Assert.Contains("Your files stay private to your account.", homepage, StringComparison.Ordinal);
        Assert.Contains("estimates never promise an employer outcome", homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("Create Your Free Workspace", homepage, StringComparison.Ordinal);
    }

    [Fact]
    public void Homepage_faq_uses_native_disclosures_and_accurate_product_limits()
    {
        var homepage = ReadSource("src", "ApplyWise.Web", "Views", "Home", "Index.cshtml");

        Assert.Contains(
            "<section class=\"aw-home-section aw-home-faq\" id=\"faq\" aria-labelledby=\"faq-title\">",
            homepage,
            StringComparison.Ordinal);
        Assert.Equal(6, Regex.Matches(homepage, "<details class=\"aw-home-faq-item\"", RegexOptions.CultureInvariant).Count);
        Assert.Equal(6, Regex.Matches(homepage, "<summary>", RegexOptions.CultureInvariant).Count);
        Assert.Contains("local, deterministic rules", homepage, StringComparison.Ordinal);
        Assert.Contains("not an employer&rsquo;s actual ATS score", homepage, StringComparison.Ordinal);
        Assert.Contains("outside the public web root", homepage, StringComparison.Ordinal);
        Assert.Contains("text-based, selectable PDF", homepage, StringComparison.Ordinal);
        Assert.Contains("cannot predict or guarantee an interview", homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("data-home-faq", homepage, StringComparison.Ordinal);
    }

    [Fact]
    public void Mobile_navigation_adds_a_backdrop_scroll_lock_and_keyboard_containment()
    {
        var homeStyles = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "home.css");
        var homeScript = ReadSource("src", "ApplyWise.Web", "wwwroot", "js", "home.js");

        Assert.Contains(".home-body.home-nav-open", homeStyles, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", homeStyles, StringComparison.Ordinal);
        Assert.Contains(".aw-home-nav-backdrop", homeStyles, StringComparison.Ordinal);
        Assert.Contains("backdrop-filter: blur(3px);", homeStyles, StringComparison.Ordinal);
        Assert.Contains("backdrop.className = 'aw-home-nav-backdrop';", homeScript, StringComparison.Ordinal);
        Assert.Contains("document.body.classList.add('home-nav-open');", homeScript, StringComparison.Ordinal);
        Assert.Contains("region.inert = true;", homeScript, StringComparison.Ordinal);
        Assert.Contains("event.key !== 'Tab'", homeScript, StringComparison.Ordinal);
        Assert.Contains("backdrop.addEventListener('click'", homeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Homepage_footer_and_conversion_links_keep_accessible_touch_targets()
    {
        var homeStyles = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "home.css");

        Assert.Contains(".aw-home-footer nav a,", homeStyles, StringComparison.Ordinal);
        Assert.Contains(".aw-home-footer-bottom a", homeStyles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", homeStyles, StringComparison.Ordinal);
        Assert.Contains(".aw-home-sign-in-link", homeStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_visual_keeps_the_tracking_message_concise()
    {
        var registration = ReadSource(
            "src",
            "ApplyWise.Web",
            "Areas",
            "Identity",
            "Pages",
            "Account",
            "Register.cshtml");

        Assert.Contains("Start tracking with Wiso", registration, StringComparison.Ordinal);
        Assert.Contains(
            "Your next opportunity, always in view.",
            registration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Track every opportunity, choose or build the right resume, and keep your next move clear.",
            registration,
            StringComparison.Ordinal);

        var login = ReadSource(
            "src",
            "ApplyWise.Web",
            "Areas",
            "Identity",
            "Pages",
            "Account",
            "Login.cshtml");

        Assert.DoesNotContain(
            "Wiso keeps opportunities, deadlines, resumes, interviews, and next actions in one clear view.",
            login,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_is_minimal_and_password_guidance_is_consistent()
    {
        var registration = ReadSource(
            "src", "ApplyWise.Web", "Areas", "Identity", "Pages", "Account", "Register.cshtml");
        var resetPassword = ReadSource(
            "src", "ApplyWise.Web", "Areas", "Identity", "Pages", "Account", "ResetPassword.cshtml");
        var settings = ReadSource(
            "src", "ApplyWise.Web", "Views", "Dashboard", "Settings.cshtml");

        Assert.DoesNotContain("Input.Gender", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.DateOfBirth", registration, StringComparison.Ordinal);
        Assert.Contains("PasswordRequirements.UserFacingSummary", registration, StringComparison.Ordinal);
        Assert.Contains("PasswordRequirements.UserFacingSummary", resetPassword, StringComparison.Ordinal);
        Assert.Contains("PasswordRequirements.UserFacingSummary", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("at least 6 characters", registration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at least 6 characters", resetPassword, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Demographics_are_clearly_optional_during_onboarding_and_profile_editing()
    {
        var onboarding = ReadSource(
            "src", "ApplyWise.Web", "Views", "Onboarding", "Index.cshtml");
        var profile = ReadSource(
            "src", "ApplyWise.Web", "Views", "Profile", "Index.cshtml");

        Assert.Contains("Optional personal details", onboarding, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Gender\"", onboarding, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"DateOfBirth\"", onboarding, StringComparison.Ordinal);
        Assert.Contains("Leave either field blank", onboarding, StringComparison.Ordinal);
        Assert.Contains("Gender and date of birth can be left blank", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_visual_scales_wiso_to_fill_the_desktop_card()
    {
        var theme = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "theme.css");

        Assert.Contains("@media (min-width: 801px)", theme, StringComparison.Ordinal);
        Assert.Contains(
            ".identity-body .aw-auth-shell-register .aw-auth-avatar-wrap img",
            theme,
            StringComparison.Ordinal);
        Assert.Contains(
            "transform: translateY(-8px) scale(1.32);",
            theme,
            StringComparison.Ordinal);
        Assert.Contains("transform-origin: 50% 100%;", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void Final_theme_releases_the_auth_shell_from_the_legacy_fixed_height()
    {
        var layout = ReadSource("src", "ApplyWise.Web", "Views", "Shared", "_Layout.cshtml");
        var theme = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "theme.css");

        AssertBefore(layout, "~/css/release.css", "~/css/theme.css");

        var authShellRule = Regex.Match(
            theme,
            @"\.identity-body\s+\.aw-auth-shell\s*\{(?<declarations>[^}]*)\}",
            RegexOptions.CultureInvariant);

        Assert.True(authShellRule.Success, "The final theme must define the identity auth-shell rule.");
        Assert.Matches(
            @"(?m)^\s*height\s*:\s*auto\s*;",
            authShellRule.Groups["declarations"].Value);

        var loginCardRule = Regex.Match(
            theme,
            @"\.identity-body\s+\.aw-login-card\s*,\s*\.identity-body\s+main\s+\.aw-auth-status-card\s*\{(?<declarations>[^}]*)\}",
            RegexOptions.CultureInvariant);

        Assert.True(loginCardRule.Success, "The final theme must define the identity login-card rule.");
        Assert.Matches(
            @"(?m)^\s*margin\s*:\s*0\s*;",
            loginCardRule.Groups["declarations"].Value);
    }

    [Fact]
    public void Public_product_and_contact_pages_adapt_to_short_laptops_and_narrow_phones()
    {
        var productStyles = ReadSource(
            "src", "ApplyWise.Web", "wwwroot", "css", "product-pages.css");
        var contactStyles = ReadSource(
            "src", "ApplyWise.Web", "wwwroot", "css", "contact.css");
        var theme = ReadSource(
            "src", "ApplyWise.Web", "wwwroot", "css", "theme.css");

        Assert.Contains("@media (min-width: 961px) and (max-height: 760px)", productStyles, StringComparison.Ordinal);
        Assert.Contains("min-height: calc(100svh - 84px);", productStyles, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", productStyles, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 961px) and (max-height: 760px)", contactStyles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, .9fr) minmax(460px, 1.1fr);", contactStyles, StringComparison.Ordinal);
        Assert.Contains("--aw-content-gutter: clamp(12px, 2.35vw, 32px);", theme, StringComparison.Ordinal);
        Assert.Contains("button { min-width: 0; }", theme, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. relativePath]));

    private static void AssertBefore(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Expected to find '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected to find '{second}'.");
        Assert.True(
            firstIndex < secondIndex,
            $"Expected '{first}' to appear before '{second}'.");
    }

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
}
