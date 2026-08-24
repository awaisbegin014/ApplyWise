# ApplyWise production operations runbook

This runbook defines the minimum operating controls for the first public release. Keep production credentials and recovery material in the hosting provider's secret store, never in this repository.

## Service objectives and ownership

- Availability target: 99.5% per calendar month for the web application.
- Recovery point objective (RPO): no more than 24 hours of SQL and resume-file data loss.
- Recovery time objective (RTO): restore service within 4 hours of a declared production incident.
- Primary responder: the GitHub `production` environment owner. Record a named backup responder and an out-of-band contact in the private operations record before launch.
- Retain production application/security logs for 30 days and deployment evidence for at least 90 days. Never log passwords, tokens, security codes, resume text, email bodies, connection strings, or Turnstile responses.

If the hosting plan cannot meet these objectives, do not describe the service as generally available; operate it as a limited beta until the hosting plan is upgraded.

## Required monitors and alerts

Configure an external monitor from a region near the primary users:

| Signal | Interval | Alert condition |
|---|---:|---|
| `GET /health/live` | 1 minute | Two consecutive failures |
| `GET /health/ready` | 1 minute | Three failures in 5 minutes, or any component reports `Unhealthy` |
| Public home and `/contact` | 5 minutes | Two consecutive non-200 responses or missing expected page title |
| HTTP 5xx rate | Continuous | More than 2% for 5 minutes, with at least 20 requests |
| Request latency | Continuous | p95 above 2 seconds for 10 minutes |
| SQL/private-storage capacity | 15 minutes | Warning at 75%, critical at 85% |
| SMTP confirmation canary | 15 minutes | Message not accepted and observed in the controlled canary inbox within 5 minutes |

Route critical alerts to two independent channels. Send a test alert before launch and once per month. Treat Turnstile or SMTP provider incidents as registration degradation even when `/health/ready` remains healthy; readiness deliberately avoids making the site unavailable because of a transient third-party outage.

## Coordinated backup procedure

At least once every 24 hours:

1. Stop the website or place it in a documented maintenance window so SQL rows and private files have one consistency point.
2. Record the UTC start time, current `/health/release` response, deployed commit, and terminal migration.
3. Create a full SQL backup using the provider's supported mechanism and verify that the backup reports success.
4. Snapshot the complete resume-storage directory.
5. Snapshot the Data Protection key directory, current encryption certificate with its private key, every configured previous certificate, and the separate secret-store references needed to decrypt them.
6. Encrypt the backup set, compute checksums, copy it to a failure domain separate from the production site, and record its immutable identifier.
7. Restart the site and require `/health/ready`, `/health/release`, login, and one private resume download to pass.

Retain 7 daily, 4 weekly, and 6 monthly recovery points. Access to backups must be narrower than access to the website. A database-only backup is insufficient because resume rows, private files, protected Gmail tokens, and Data Protection keys must be restored together.

## Restore rehearsal and disaster recovery

Rehearse restoration into an isolated non-production site before first launch and monthly afterward:

1. Select a recorded recovery point and verify its checksums.
2. Restore SQL with a temporary migration-capable identity; configure the application with a separate least-privilege identity.
3. Restore resume storage, Data Protection keys, and all required certificates to private paths outside the deployment directory.
4. Start the exact immutable release that matches the recorded migration. Never point the public domain at the rehearsal.
5. Require `/health/ready` and `/health/release` to match the recorded evidence.
6. Verify owner MFA, a candidate login, private resume download, application CRUD, and tenant isolation using synthetic accounts.
7. Record elapsed time, recovered data timestamp, any manual repair, and whether RPO/RTO were met. Delete rehearsal secrets and data afterward.

For a real incident, stop writes before restoration, preserve logs, select the last verified recovery point, and communicate the recovery timestamp. Do not combine an unreviewed schema downgrade with a binary rollback.

## First owner provisioning

Public registration intentionally cannot claim an allowlisted owner address. From a private interactive terminal with the complete Production configuration and temporary trusted database access, run the tested release payload:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet ApplyWise.Web.dll --provision-owner --email=owner@example.com
```

The command prompts without echo for the initial password, prints an authenticator key/URI, verifies a live six-digit TOTP, enables MFA, assigns the `Admin` role, and prints one-time recovery codes. Store recovery codes offline and clear the terminal. If an incomplete account already exists, independently audit its origin; only then may the operator rerun with `--complete-existing-owner`. The command is idempotent for an already healthy owner and refuses redirected input/output so MFA secrets do not enter CI logs.

Afterward, disable temporary database access, sign in with MFA, and require the `admin_owner` readiness check to report healthy.

## Data Protection certificate rotation

Never replace the certificate and discard the old private key in one step. Old cookies, recovery tokens, and protected Gmail refresh tokens can depend on keys encrypted by the previous certificate.

1. Back up the key ring and both certificates before changing configuration.
2. Set the new certificate as `DataProtection__CertificatePath` and `DataProtection__CertificatePassword`.
3. Keep each prior certificate available through indexed settings such as:

   ```text
   DataProtection__PreviousCertificates__0__Path=<absolute private previous PFX/PEM path>
   DataProtection__PreviousCertificates__0__Password=<previous certificate password>
   ```

4. Deploy and verify authentication, owner MFA, account-recovery tokens, and a Gmail sync for a controlled connection.
5. Retain the prior private certificate while any key-ring file or protected Gmail credential may require it. Removing it is an intentional credential invalidation and requires users to reconnect Gmail and sign in again.

Production startup rejects missing/private-key-free, non-RSA, undersized, expired current certificates and certificate files inside the deployment root.

## Gmail release control

Google sign-in and Gmail import have separate release controls. Keep `Google__GmailImportEnabled=false` until Google has approved the restricted Gmail scope and any required security assessment is complete. Enabling Google credentials alone exposes only basic Google sign-in. After approval, set the Gmail flag to `true`, deploy, connect a controlled mailbox, verify manual sync, then enable scheduled sync if desired.

## Release evidence

For every release, retain:

- protected `master` commit and successful **Validate release** run ID;
- exact workflow path `.github/workflows/ci.yml`;
- release and rollback commit IDs;
- publish-payload SHA-256, migration SHA-256, and terminal migration from `release-manifest.json`;
- coordinated backup identifier and restore-test date;
- production approval/change reference;
- deployed `/health/release` response;
- SMTP, Turnstile, owner-MFA, public-route, and authenticated smoke-test results;
- monitoring test-alert evidence and responder acknowledgement.

The first hardened deployment needs two distinct successful baseline-valid commits/runs: an ancestor rollback candidate followed by the intended release. Do not weaken this control to work around missing release history.
