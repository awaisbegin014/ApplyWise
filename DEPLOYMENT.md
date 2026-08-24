# ApplyWise release and operations notes

ApplyWise is one ASP.NET Core MVC application backed by SQL Server and private persistent file storage. The checked-in Docker Compose stack is for local integration only; it deliberately uses the SQL Server Developer edition and its `sa` account inside a private development network. It is not a production topology.

## Production prerequisites

Before a production rollout:

1. Set a least-privilege `ConnectionStrings__DefaultConnection`, canonical HTTPS `PublicOrigin`, exact `AllowedHosts`, and the exact trusted proxy IPs under `ForwardedHeaders__KnownProxies`. Production rejects `sa`, placeholders, wildcard hosts, missing proxies, relative private paths, and unconfirmed-account mode. SQL encryption and certificate-chain validation are mandatory; there is no certificate-validation bypass.
2. Create a Cloudflare Turnstile widget restricted to the exact public hostname. Store `HumanChallenge__SiteKey` and `HumanChallenge__SecretKey` in the host secret store, set `HumanChallenge__ExpectedHostname` to that hostname, and keep `HumanChallenge__Enabled=true`. Production refuses to start without this server-validated bot protection.
3. Configure SMTP and verify delivery for confirmation, recovery, and sensitive-action codes. Never treat an accepted SMTP request as proof of inbox delivery.
4. Put resumes, Data Protection keys, and the Data Protection certificate below a private persistent host directory, never `wwwroot`. Back up the encrypted key ring and restrict filesystem access to the application identity.
5. Provision every owner with the offline `--provision-owner` command in [docs/OPERATIONS.md](docs/OPERATIONS.md). Public registration rejects configured owner addresses. Audit any pre-existing row before using the explicit completion flag; the command verifies TOTP and produces recovery codes without exposing a web bootstrap route.
6. Finish Google OAuth verification and any restricted-scope security assessment before enabling Gmail imports for public users. Keep the feature disabled until this external approval is complete.
7. Implement and record the monitors, alert routing, coordinated backups, retention, monthly restore rehearsal, and RPO/RTO evidence defined in [docs/OPERATIONS.md](docs/OPERATIONS.md).

## Immutable release process

The supported hosted path is documented in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). Every production release follows this order:

1. Merge to `master` and wait for **Validate release** to succeed.
2. Record that workflow run ID and choose an earlier successful run carrying the same `2026-08-release-hardening-v2` security baseline as the rollback artifact. Pull-request, unreachable, pre-hardening, and schema-unbound artifacts are deliberately rejected. In normal mode, the workflow also requires production's current `/health/release` SHA, environment, baseline, and terminal migration to match that rollback artifact before it changes anything.
3. Take and verify a recoverable database backup.
4. Download `migrations.sql` from the exact successful release artifact, verify its SHA-256 and terminal migration against `release-manifest.json`, and review it. Automatic binary rollback is safe only when the schema remains compatible with both release and rollback binaries and every data invalidation is understood. This release deliberately invalidates outstanding six-digit security codes; users can request new codes. Apply the script before binary deployment with a temporary schema-migration identity; the application identity must not receive schema permissions. Never update Production from an arbitrary checkout.
5. Start **Deploy tested ApplyWise release** with the exact release run ID, rollback run ID, website name, canonical origin, backup reference, and all three confirmation switches. A declined confirmation fails the workflow visibly.
6. The workflow deploys the already-tested artifact; it does not rebuild mutable source. It then requires `/health/ready`, `/health/release`, and `/contact` to verify. A failed or partial Web Deploy attempt automatically restores the selected binary artifact and verifies recovery.
7. If the schema must be reversed, stop traffic and use the reviewed database recovery plan or backup. Do not improvise a destructive rollback while the site is serving requests.

`/health/live` proves the process is responsive. `/health/ready` verifies database connectivity, that no compiled migration is pending, that private resume and Data Protection storage are writable, that protection can round-trip, and—in Production—that a confirmed, MFA-enabled allowlisted owner has the `Admin` role. `/health/release` reports the deployed source revision, runtime environment, security baseline, and terminal compiled migration. Monitoring should use readiness; container liveness uses the lighter live endpoint.

The one-time first deployment of this security baseline needs two distinct successful artifacts: an ancestor baseline artifact and the intended release artifact. Use the explicitly reviewed `bootstrap_first_hardened_release` switch only for that first rollout, provide the production change-ticket/approval reference, and keep the GitHub `production` environment approval enabled. Bootstrap mode still requires a healthy live site, two baseline-valid artifacts, an ancestor relationship, a verified backup, and all migration confirmations; it only waives the impossible pre-existing live-SHA match while the legacy site returns 404 for `/health/release`. The workflow rejects bootstrap once that endpoint exists.

GitHub retains validation artifacts for 90 days. Refresh the exact live last-known-good commit at least every 60 days by dispatching **Validate release** from a protected tag/ref that resolves to that SHA and remains reachable from `master`, then record the new successful run ID. This keeps an immutable rollback payload available during quiet release periods.

## Local integration stack

Copy `.env.example` to the ignored `.env`, choose a strong development-only SA password, then run:

```powershell
docker compose up -d db
docker compose --profile migration run --rm migrate
docker compose up -d --build web
```

The web listener binds to `127.0.0.1:8080`; SQL is not published to the host. The stack persists local SQL data, resumes, and Data Protection keys in named volumes. Do not place production secrets or data in this stack.

## Release validation commands

```powershell
dotnet tool restore
dotnet restore ApplyWise.sln --locked-mode
dotnet build ApplyWise.sln -c Release --no-restore
dotnet test ApplyWise.sln -c Release --no-build
dotnet format ApplyWise.sln --verify-no-changes --no-restore
node --check src/ApplyWise.Web/wwwroot/js/home.js
node --check src/ApplyWise.Web/wwwroot/js/site.js
node --check src/ApplyWise.Web/wwwroot/js/resume-builder.js
node --test tests/resume-builder/resume-builder.test.cjs
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/ApplyWise.Web/ApplyWise.Web.csproj --startup-project src/ApplyWise.Web/ApplyWise.Web.csproj --configuration Release --no-build
dotnet list ApplyWise.sln package --vulnerable --include-transitive
```

The SDK, runtime image, ASP.NET Core/EF packages, and EF tool are pinned to the same supported .NET 10 servicing release. CI also boots the exact Web Deploy payload, verifies public routes and release identity, and binds its manifest to both migration and full payload hashes. Re-run the full gate whenever those pins or the lock files change.
