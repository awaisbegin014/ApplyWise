# ApplyWise

**Track every application. Choose the right resume. Apply smarter.**

ApplyWise is a portfolio-ready ASP.NET Core MVC job-search workspace. It helps job seekers keep applications and resume versions connected, compare a resume with a job description, plan follow-ups and interviews, understand job-search patterns, and review suspicious job posts—all inside a private per-user dashboard.

## The problem it solves

Job searches quickly become fragmented across job boards, company websites, email, and referrals. ApplyWise keeps the operational details in one place: where and when someone applied, which resume they used, what happens next, and which patterns may be limiting results.

## Features

- ASP.NET Core Identity registration, login, logout, and account management
- Optional Google sign-in with explicit account linking for existing users
- Optional read-only Gmail connection that detects likely application confirmations and sent resumes for user review
- Private PDF resume library with version names, notes, and a default resume
- Browser-local one-page resume builder with live A4 fit checks, section reordering, safe bold/italic/underline formatting, autosave, and selectable-text PDF download
- Job application CRUD with status, source, deadline, job link, notes, and submitted-resume memory
- Search, filters, sorting, responsive tables, polished empty states, and confirmation screens
- Deterministic Readiness, Job Match, and ApplyWise Fit estimates with evidence, document diagnostics, prioritized reviews, history comparison, and no per-analysis AI call
- Best-resume comparison and one-click assignment to a tracked application
- Interview scheduling, outcome tracking, application deadlines, and dashboard actions
- Application funnel, resume performance, platform response, and recurring skill-gap analytics
- Rule-based job-post quality and scam-risk checks with saved private history
- Per-user authorization across every product module and private resume downloads

The matching and scam-review features are deliberately explainable local heuristics in this release; they are not generative AI and do not send resume content to an external model.

The current resume-analysis model, score formula, privacy boundaries, evaluation set, and benchmark commands are documented in [docs/ats-analysis.md](docs/ats-analysis.md). Taxonomy importing and provenance are documented separately in [docs/ats-taxonomy.md](docs/ats-taxonomy.md).

## Technology

- .NET 10 / ASP.NET Core MVC
- C#, Razor Views, Bootstrap 5, and small vanilla JavaScript enhancements
- ASP.NET Core Identity
- Entity Framework Core 10 and SQL Server / LocalDB
- PdfPig for PDF text extraction
- pdfmake 0.3.11 for client-side, selectable-text resume PDFs

## Screenshots

The screenshot checklist and safe demo-data guidance are in [docs/screenshots/README.md](docs/screenshots/README.md). Capture these views before publishing the portfolio repository:

1. Public home and branded sign-in
2. Dashboard
3. Resume library
4. Resume Builder editor and live PDF preview
5. Applications list and details
6. Resume analysis result
7. Best resume picker
8. Interviews and deadlines
9. Analytics
10. Job-post review result

No personal resume, email address, real employer notes, or production data should appear in portfolio screenshots.

## Prerequisites

- .NET 10 SDK
- SQL Server Express/Developer or SQL Server LocalDB
- EF Core CLI: `dotnet tool install --global dotnet-ef`
- Visual Studio 2022 or VS Code (optional)

## Run locally

From the repository root:

```powershell
dotnet restore
dotnet build
dotnet ef database update --project src/ApplyWise.Web
dotnet run --project src/ApplyWise.Web
```

Open the HTTPS URL printed by ASP.NET Core, create an account, and add demo data. No seeded login is included; this avoids shipping a shared password or private sample resume.

LocalDB is the safe development default in `appsettings.json`. Override it without editing tracked files:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your SQL Server connection string>" --project src/ApplyWise.Web
```

## Database and migrations

The migration history builds Identity, resume management, application tracking, resume analysis, interviews, analytics, and job-post checks in order. Apply the existing migrations with:

```powershell
dotnet ef database update --project src/ApplyWise.Web
```

For a new schema change:

```powershell
dotnet ef migrations add <MigrationName> --project src/ApplyWise.Web
dotnet ef database update --project src/ApplyWise.Web
```

## Configuration

Production values should come from environment variables or the host's secret store:

| Setting | Environment variable | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Monster MSSQL / SQL Server connection |
| `SqlTransport:*` | `SqlTransport__AllowMonsterAspManagedCertificate`, `SqlTransport__MonsterAspHost` | Exact-host exception for MonsterASP's provider-managed SQL certificate; encryption remains mandatory |
| `PublicOrigin` | `PublicOrigin` | Canonical HTTPS public URL; required in Production |
| `AllowedHosts` | `AllowedHosts` | Exact public host names; wildcard values are rejected in Production |
| `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies__0` (and later indexes) | Exact trusted TLS-terminating proxy IPs; required in Production |
| `HumanChallenge:*` | `HumanChallenge__Enabled`, `HumanChallenge__SiteKey`, `HumanChallenge__SecretKey`, `HumanChallenge__ExpectedHostname` | Cloudflare Turnstile protection for public registration and anonymous contact; required in Production |
| `Email:*` | `Email__Host`, `Email__Port`, `Email__UserName`, `Email__Password`, `Email__From` | SMTP for confirmation and account recovery |
| `Google:ClientId`, `Google:ClientSecret` | `Google__ClientId`, `Google__ClientSecret` | Enables basic Google sign-in |
| `Google:Gmail*` | `Google__GmailImportEnabled`, `Google__GmailAutoSyncEnabled`, `Google__GmailSyncIntervalMinutes`, `Google__GmailInitialLookbackDays`, `Google__GmailMaxMessagesPerSync` | Separate fail-closed Gmail release switch, scheduling, and bounded sync limits |
| `ResumeStorage:RootPath` | `ResumeStorage__RootPath` | Absolute private resume storage path; required in Production |
| `DataProtection:*` | `DataProtection__KeysPath`, `DataProtection__CertificatePath`, `DataProtection__CertificatePassword`, indexed `DataProtection__PreviousCertificates` | Persistent key path, current PFX/encrypted PEM, and prior decryption certificates for safe rotation |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | Use `Production` on a deployed host |
| `Performance:SlowRequestThresholdMs` | `Performance__SlowRequestThresholdMs` | Warning-log threshold for slow requests; defaults to 500 ms |

The default private upload path is `App_Data/Uploads/Resumes`. It is configurable, canonicalized, and never mapped as a static web directory. Production requires resume storage, Data Protection keys, and certificates to be absolute private paths outside the application/Web Deploy directory. It also rejects wildcard hosts, a non-public/non-HTTPS origin, insecure SMTP, and missing human-verification credentials. Create a Turnstile widget restricted to the exact production hostname; keep its secret in the host's secret store.

### Google sign-in and Gmail imports

Create a Web application OAuth client in Google Cloud and configure these authorized redirect URIs for each deployed origin:

```text
https://your-host/signin-google
https://your-host/signin-google-gmail
```

For the local HTTPS launch profile, use:

```text
https://localhost:7075/signin-google
https://localhost:7075/signin-google-gmail
```

Keep the client secret outside tracked configuration:

```powershell
dotnet user-secrets set "Google:ClientId" "<client-id>" --project src/ApplyWise.Web
dotnet user-secrets set "Google:ClientSecret" "<client-secret>" --project src/ApplyWise.Web
```

Use the OAuth **Web application** client ID, which ends in
`.apps.googleusercontent.com`; do not use the project ID, API key, or the
placeholder text from this example.

Basic Google sign-in requests identity information only. Gmail is connected later from the Imports page and requests `gmail.readonly` separately. Refresh tokens are protected with ASP.NET Core Data Protection. Sync reads matching messages transiently, stores extracted application suggestions and minimal email evidence, and does not retain email bodies or attachment contents.

`gmail.readonly` is a Google restricted scope. Before offering Gmail imports publicly, configure the OAuth consent screen, privacy policy, authorized domains, Google verification, and any required independent security assessment. `Google:GmailImportEnabled` defaults to `false` and independently blocks the Gmail scheme, routes, UI, manual sync, and worker while leaving basic Google sign-in available. `Google:GmailAutoSyncEnabled` controls scheduling only after Gmail import is enabled.

The concrete backup, restoration, monitoring, owner-provisioning, and certificate-rotation procedures are in [docs/OPERATIONS.md](docs/OPERATIONS.md).

## Performance

- The dashboard is assembled from five narrow, tenant-scoped, no-tracking database projections rather than one query per card.
- Read-heavy dashboard filters have supporting composite SQL indexes; apply the exact release artifact's verified migration before binary deployment.
- Dynamic responses use Brotli or gzip, and publish output includes precompressed gzip static assets.
- Resume-builder fonts and template PDF thumbnails load on demand. Large artwork is rendered-size and format optimized.
- Requests slower than `Performance:SlowRequestThresholdMs` are logged without exposing public timing or request bodies/private resume data.

For reliable cloud performance, keep the web app and SQL database in the same region, enable the platform's always-on setting, use `/health/live` for liveness and `/health/ready` for deployment/traffic readiness, and verify `/health/release` against the released commit. Measure an authenticated dashboard request after deployment instead of judging only the public home page.

## Security notes

- Product controllers require authentication and query tenant-owned records with the current Identity user ID.
- Posted resume/application relationships are revalidated against the current user before persistence.
- Dedicated view models constrain binding and antiforgery validation protects state-changing forms.
- Resume uploads are limited to PDF extension/MIME/signature and 5 MB, receive generated storage names, and are rejected if text extraction fails.
- PDF extraction has global concurrency, page-count, and extracted-text limits to reduce parser resource exhaustion.
- Razor encoding is retained for stored user content; outbound job links use safe external-link attributes.
- Baseline response headers block MIME sniffing, framing, embedded objects, and unused browser permissions.
- Development settings, environment files, uploaded PDFs, build output, logs, and local databases are ignored by Git.
- Production uses the ASP.NET Core exception handler, HTTPS redirection, and HSTS.

This is application-level hardening, not a substitute for platform monitoring, backups, malware scanning, rate limiting, retention policy, and periodic dependency/security updates.

## Deployment

The documented hosted release path is **MonsterASP.NET + Monster MSSQL**. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) and [DEPLOYMENT.md](DEPLOYMENT.md) for configuration, migration, private storage, Web Deploy, and verification steps.

Create a local release artifact outside the repository with:

```powershell
dotnet publish src/ApplyWise.Web -c Release -o "$env:TEMP/ApplyWise-publish"
```

## Delivery roadmap

- Levels 1–3: repository foundation, Identity, responsive SaaS dashboard shell
- Level 4: private resume version management
- Level 5: application tracking and resume-used memory
- Levels 6–7: resume/job analysis and best-resume selection
- Level 8: interviews, deadlines, and next actions
- Level 9: analytics and rule-based job-post review
- Level 10: product polish, accessibility, security review, documentation, and deployment readiness

## Interview demo

Start with the dashboard, then show one complete story: upload two demo resumes, create a job with a description, compare both resumes, assign the recommended version, change the application status, schedule an interview, and finish on analytics and the job-post review. That demonstrates product thinking, relational modeling, authorization, file handling, service-layer logic, and responsive UI in one coherent flow.

## Resume bullets

- Built a full-stack ASP.NET Core MVC application that helps job seekers track applications, manage resume versions, and remember which resume was submitted for each job.
- Added a privacy-first resume studio with structured editing, browser autosave, responsive live preview, and direct A4 PDF generation without uploading draft content.
- Implemented explainable resume-to-job analysis with match scoring, missing-skill detection, best-resume recommendation, and private analysis history.
- Developed interview scheduling, application-deadline tracking, dashboard analytics, platform response insights, and rule-based job-post risk detection.
- Used ASP.NET Core Identity, Entity Framework Core, SQL Server, Razor Views, Bootstrap, secure private PDF storage, and per-user authorization to deliver a SaaS-style product.

## Future improvements

- private object storage and malware scanning for scalable production resume storage
- Calendar integration for interviews and application deadlines
- Rate limiting, structured observability, account export/deletion, and formal retention controls
- Optional LLM-assisted analysis with consent, redaction, cost controls, and auditable prompts
- Automated unit, integration, accessibility, and browser regression suites in CI
