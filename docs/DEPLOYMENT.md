# MonsterASP.NET deployment guide

ApplyWise is prepared for MonsterASP.NET with Monster MSSQL. The application does not embed production credentials and does not automatically mutate the production schema at startup.

## 1. Provision

Create a .NET 10 website and MSSQL database in the MonsterASP.NET Control Panel. Give the application database credentials only the permissions it needs. Enable database remote access only while applying migrations from a trusted workstation, then disable it again if it is not otherwise required.

Choose the Monster region closest to the first users. The free plan is suitable for staging validation; use a Premium plan before attaching a custom domain or relying on the service for a public commercial launch.

## 2. Configure the Monster website

Add these settings under **Websites → Manage website → Scripting → Environment Variables**:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<Monster MSSQL connection string>
SqlTransport__AllowMonsterAspManagedCertificate=true
SqlTransport__MonsterAspHost=<exact Monster internal database hostname>
PublicOrigin=https://<public-host-name>
AllowedHosts=<public-host-name>
AdminAccess__Emails__0=<out-of-band provisioned owner address>
AdminAccess__RequireMfa=true
ForwardedHeaders__KnownProxies__0=<trusted reverse-proxy IP address>
HumanChallenge__Enabled=true
HumanChallenge__SiteKey=<Cloudflare Turnstile site key>
HumanChallenge__SecretKey=<Cloudflare Turnstile secret key>
HumanChallenge__ExpectedHostname=<public-host-name>
Email__Host=<SMTP host>
Email__Port=587
Email__UserName=<SMTP user>
Email__Password=<SMTP password>
Email__From=<verified sender address>
ResumeStorage__RootPath=<absolute private persistent directory>
DataProtection__KeysPath=<absolute persistent key directory>
DataProtection__CertificatePath=<absolute path to a mounted PFX or encrypted PEM>
DataProtection__CertificatePassword=<certificate password>
Google__GmailImportEnabled=true
Google__GmailAutoSyncEnabled=true
Google__GmailSyncIntervalMinutes=5
Gemini__Enabled=true
Gemini__ApiKey=<server-side Gemini API key>
Gemini__Model=<approved Gemini model>
Subscriptions__FreeAtsAnalysisLimit=2
Subscriptions__ProAtsAnalysisLimit=100
Subscriptions__FreeResumeBuildLimit=2
Subscriptions__ProPrice=<displayed price>
Subscriptions__Currency=PKR
Subscriptions__PaymentInstructions=<verified payment destination and instructions>
```

Use absolute paths below the site's sibling `Private` directory for resumes, Data Protection keys, and the PFX certificate—for example, `D:\Sites\site12345\Private\ApplyWise\...` using the actual physical path shown for your Monster site. Production rejects any of these paths beneath the application/Web Deploy root because `target-delete` could erase them. Upload the certificate to `Private` through Monster WebFTP and back up the encrypted key directory; it protects authentication cookies, protected Gmail credentials, and account-recovery tokens.

Production intentionally refuses to start with the `sa` login, placeholder values, wildcard hosts, a non-HTTPS public origin, an untrusted proxy configuration, missing Turnstile credentials, or relative storage/key paths. SQL encryption is enforced by the application even if a hosting profile supplies weaker client flags. Publicly trusted certificate-chain validation remains the default. MonsterASP's provider-managed SQL certificate is accepted only when `SqlTransport__AllowMonsterAspManagedCertificate=true`, the configured server exactly matches `SqlTransport__MonsterAspHost`, and that host is under `databaseasp.net`; this exception never disables encryption and cannot authorize an arbitrary SQL server. Use a separate, temporary migration identity with schema permissions and never place those elevated credentials in the Monster website environment. Set `ForwardedHeaders__KnownProxies__0` (and additional indexed values as needed) only to the exact proxy IP addresses that terminate TLS. Restrict the Turnstile widget to the public hostname; registration and anonymous contact submissions are accepted only after server-side token, action, and hostname validation.

Mark secrets as deployment settings and keep them out of `appsettings.json`, shell history, screenshots, and Git.

Owner identities must be provisioned through the offline `--provision-owner` command documented in [OPERATIONS.md](OPERATIONS.md); public registration intentionally rejects every configured owner address. Before first launch, audit the identity database for pre-existing rows using an owner address and never complete an account whose origin cannot be proven. The command confirms the identity, verifies live TOTP enrollment, assigns the role, and produces recovery codes. On startup, ApplyWise synchronizes the `Admin` role to confirmed allowlisted identities and removes access from administrators no longer listed. The owner console is available at `/admin`, and the policy requires second-factor evidence from the current sign-in session. Add more owners with `AdminAccess__Emails__1`, `AdminAccess__Emails__2`, and so on, using the same procedure.

The production release enables Google sign-in, read-only Gmail import, and scheduled sync. Before approving deployment, complete verification for the restricted Gmail scope, rotate any development OAuth secret, configure the production consent screen, and register both `/signin-google` and `/signin-google-gmail` redirect paths. Newly connected inboxes automatically add only high-confidence confirmations; uncertain messages remain in review and users can disable automatic addition from Imports.

## 3. Apply migrations

Back up an existing production database before schema changes. Download the idempotent `migrations.sql` from the exact successful **Validate release** artifact, verify that its SHA-256 and terminal migration match `release-manifest.json`, review it, and apply that file with a temporary migration identity. Record the backup identifier, release workflow run ID, migration ID, and checksum. Never run `database update` from an arbitrary local checkout against Production, and do not expose the development migrations endpoint there.

## 4. Configure GitHub deployment

Activate Web Deploy in the Monster Control Panel, then add these GitHub **production environment secrets**:

```text
MONSTER_SERVER_COMPUTER_NAME=https://site12345.siteasp.net:8172
MONSTER_SERVER_USERNAME=site12345
MONSTER_SERVER_PASSWORD=<Web Deploy password>
```

Run **Deploy tested ApplyWise release** from GitHub Actions. Supply the successful **Validate release** run ID from `master`, a distinct earlier successful rollback run ID from an ancestor commit carrying the same required security baseline, the exact HTTPS origin, and the verified backup reference. The workflow derives the single production website name from the protected `MONSTER_SERVER_USERNAME` value, so operators do not retype or expose that deployment identity. Confirm that the exact artifact's migration was reviewed and applied, that its schema remains compatible with the selected rollback binary, and that every data invalidation is understood. The current hardening migration intentionally invalidates outstanding six-digit security codes; users can request replacements. The workflow validates both manifests and proves the rollback SHA is the currently healthy production SHA before changing production. It downloads and deploys the immutable tested release artifact and does not rebuild source. On a failed or partial Web Deploy attempt, readiness failure, or version mismatch, it redeploys the selected rollback binary and rechecks readiness, version, and a public route. Pre-hardening rollback artifacts are rejected. If a migration is destructive or incompatible with the old binary, do not run this workflow until the change has been split into a safe expand/deploy/contract sequence.

Validation artifacts are retained for 90 days. At least every 60 days, refresh the current live last-known-good artifact by dispatching **Validate release** at a protected tag or ref that resolves to the exact live SHA and is still reachable from `master`; record the replacement run ID only after it succeeds. The deploy workflow rejects pull-request artifacts and commits not reachable from the repository's current `master`. Do not allow the live rollback artifact to expire.

For the one-time first rollout of `2026-08-release-hardening-v2`, create two distinct successful validation artifacts carrying the baseline (an ancestor baseline artifact and the intended release), obtain approval through the protected GitHub `production` environment, and enable `bootstrap_first_hardened_release` with the production change-ticket/approval reference. Bootstrap mode still requires a healthy current site, a verified backup, both validated baseline artifacts, their ancestor relationship, and every migration confirmation; it only waives the live rollback-SHA match when the legacy site returns 404 for `/health/release`. Once the hardened endpoint exists, the workflow rejects bootstrap mode automatically. Every later deployment must use the normal live last-known-good SHA check.

## 5. Production checks

- Confirm HTTPS redirection and HSTS responses.
- Confirm the security headers remain present after any reverse proxy or CDN configuration.
- Register a fresh smoke-test account; do not use a personal account.
- Confirm the Turnstile widget loads on registration and anonymous contact, invalid/replayed tokens fail, and signed-in contact remains usable.
- Verify static CSS/JavaScript, login/logout, and every protected navigation link.
- Upload a small text-based demo PDF and confirm it is absent from public static URLs.
- Create/edit/delete an application and confirm its resume relationship.
- Run ATS analysis, compare-all resume selection, application tracking, Gmail import, and scam review.
- Confirm a second account receives 404/no data for the first account's record IDs.
- Review Monster Control Panel logs without logging resume contents or connection strings.
- Configure backups, health monitoring, alerts, storage retention, and a rollback plan.
- Confirm `/health/live` is responsive; `/health/ready` reports healthy database/schema, private storage, Data Protection, and MFA-enabled owner checks; and `/health/release` reports the expected source commit, `Production` environment, security baseline, and terminal migration.
- Restrict readiness and release metadata to trusted monitoring where supported; health routes are rate-limited, and readiness performs real database/schema and private-storage probes.
- Keep the application behind Monster's HTTPS/IIS front end; do not expose a separate internal listener.

See the repository-level [deployment notes](../DEPLOYMENT.md) and [operations runbook](OPERATIONS.md) for the Docker Compose flow, backups, monitoring, recovery, owner provisioning, and certificate rotation.
