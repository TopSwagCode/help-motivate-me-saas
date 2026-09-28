# Hosted SaaS and Scale-to-Zero Implementation Plan

## Status

Planning document. This plan adds a hosted SaaS around the existing application without removing or weakening the free self-hosted edition.

Local MVP status: this repository now presents Self-hosted, Free, and Paid-preview choices; implements Free BYO AI, Paid managed-AI configuration, and local paid-only volume backup/restore; and provides workspace and admin views, tenant metadata, Docker provisioning, isolated volumes, safe container replacement, Traefik `*.localhost` routing, health-aware lifecycle controls, automatic wake-up, activity tracking, uptime estimates, and idle shutdown. See `../README.md`. Authentication, PostgreSQL, encrypted object storage and retention, production TLS/DNS, usage metering, and payment remain future phases.

## Product Model

Help Motivate Me remains a free, AGPL-3.0 licensed, self-hostable single-user application. The hosted service sells managed operation and additional hosted capabilities, not access to the source code.

| Offering | Price | Application | AI | Managed backup and restore | Hosting |
| --- | --- | --- | --- | --- | --- |
| Self-hosted | Free | Full open-source application | Bring your own supported AI provider/key | User-managed using the documented `/data` backup procedure | User-managed |
| Hosted Free | Free | Core habit, identity, goal, journal, and analytics features | Disabled | No managed backup or restore guarantee | Managed |
| Hosted Paid | Subscription | Core application | Included, with plan usage limits | Automated backups and user-initiated restore | Managed |

The pricing, signup, and settings pages must state prominently:

- The application is free to self-host.
- Hosted Free does not include platform-provided AI or managed backups.
- Self-hosters retain the existing ability to configure their own AI key and perform their own backups.
- Hosted Paid includes managed AI subject to a documented usage policy, not unlimited unbounded API usage.

Polar is the initial payment and subscription provider: <https://polar.sh/>.

## Existing Product Boundary

The current repository already provides an appropriate tenant workload:

- `Dockerfile` produces one ASP.NET Core container serving the Svelte SPA and API on port `8080`.
- Mutable state is isolated under `/data`: the SQLite database at `/data/helpmotivateme.db` and uploads under `/data/uploads`.
- `/health/live` and `/health/ready` provide process and SQLite readiness checks.
- `SingleUser` configuration creates and maintains one account per installation.
- AI is enabled by configuration and already exposes a frontend-readable status endpoint.
- The current root `docker-compose.yml` remains the supported self-hosted deployment.

Do not convert the tenant application back to a shared PostgreSQL multi-user application. For the hosted service, one tenant equals one application container plus one persistent Docker volume.

## Target Architecture

```text
												 DNSimple
										app.example.com
										*.example.com
														|
														v
												 Traefik
										/        |        \
									 /         |         \
					SaaS control     exact       wildcard
							plane       tenant       fallback
														|              |
														v              v
										 running tenant    wake service
														^              |
														|              v
														+---------- Docker API

Control plane ---------- Polar webhooks
			|
			+----------------- PostgreSQL
			+----------------- backup object storage
			+----------------- AI gateway/provider
```

Docker Compose owns long-running infrastructure:

- `traefik`
- `control-plane-api`
- `control-plane-web`
- `postgres`
- optional object-storage service for local development

The control plane dynamically creates tenant containers and volumes through Docker.DotNet. Tenant containers are not Compose services and never receive access to the Docker socket.

## Proposed Expanded Repository Layout

Keep the hosted platform in this private repository so self-hosting does not depend on SaaS code.

```text
HelpMotivateMe.SaaS.slnx
compose.yml
src/
	HelpMotivateMe.SaaS.Api/              # account, tenant, billing, backup APIs
	HelpMotivateMe.SaaS.Core/             # entities, plans, lifecycle contracts
	HelpMotivateMe.SaaS.Infrastructure/   # EF Core, Docker.DotNet, Polar, storage
	HelpMotivateMe.SaaS.Worker/           # idle stop, backup, reconciliation jobs
	HelpMotivateMe.SaaS.Web/              # signup, pricing, tenant and billing UI
tests/
	HelpMotivateMe.SaaS.UnitTests/
	HelpMotivateMe.SaaS.IntegrationTests/
infra/
	traefik/
		dynamic.yml
	scripts/
```

The wake endpoint may initially live in `HelpMotivateMe.SaaS.Api`. Split it into a dedicated service only if independent scaling or a smaller Docker privilege boundary becomes useful.

## Control Plane Data Model

Use PostgreSQL for platform state. Do not store platform billing or orchestration state in tenant SQLite files.

### Account

- `Id`
- `Email`
- `DisplayName`
- authentication fields
- `CreatedUtc`

### Tenant

- `Id` as an internal GUID or random identifier
- `AccountId`
- normalized `Subdomain`
- `Status`: `Provisioning`, `Running`, `Stopped`, `Suspended`, `Failed`, `Deleting`
- `ContainerId`
- `VolumeName`
- `ImageReference`
- `LastRequestUtc`
- `LastStartedUtc`
- `ProvisionedUtc`
- optimistic concurrency token

Never derive Docker resource names from the user-selected subdomain. Use names such as `hmm-tenant-{tenantId}` and `hmm-data-{tenantId}`.

### Subscription

- `AccountId`
- `Provider` and `ProviderCustomerId`
- `ProviderSubscriptionId`
- `Plan`: `Free` or `Paid`
- `Status`
- `CurrentPeriodEndUtc`
- `CancelAtPeriodEnd`
- `UpdatedUtc`

### Backup

- `Id`
- `TenantId`
- `ObjectKey`
- `ApplicationVersion`
- `DatabaseSchemaVersion`
- `SizeBytes`
- `Checksum`
- `Status`
- `CreatedUtc`
- `ExpiresUtc`
- `RestoredUtc`

### Operation

Record provisioning, start, stop, upgrade, backup, and restore attempts with status, timestamps, and a sanitized error. This supports retries and an operator audit trail.

## DNS, TLS, and Routing

1. Configure `A app.example.com -> SERVER_IP` and `A *.example.com -> SERVER_IP` once in DNSimple.
2. Give Traefik a restricted DNSimple token through a Docker secret, not source control or a tenant environment.
3. Use Let's Encrypt DNS-01 to obtain `example.com` and `*.example.com` in one certificate.
4. Persist Traefik ACME state in an infrastructure volume and back it up separately.
5. Attach Traefik, the control plane, and all tenant containers to one externally created `proxy` network.
6. Do not publish tenant ports on the host.

Each tenant container receives labels equivalent to:

```text
traefik.enable=true
traefik.docker.network=proxy
traefik.http.routers.tenant-{id}.rule=Host(`{subdomain}.example.com`)
traefik.http.routers.tenant-{id}.entrypoints=websecure
traefik.http.routers.tenant-{id}.tls=true
traefik.http.routers.tenant-{id}.priority=100
traefik.http.services.tenant-{id}.loadbalancer.server.port=8080
```

Do not set a certificate resolver on each tenant router. The wildcard certificate is the default certificate for `websecure`.

Add one priority `1` fallback router matching approved tenant-shaped hosts and forwarding to the wake endpoint. Verify with the pinned Traefik version that stopped containers are removed from Docker-provider routing; the required acceptance test is `stop -> request hostname -> wake page`, not an assumption about provider behavior.

## Subdomain Rules

Normalize to lowercase and validate with:

```regex
^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$
```

Reserve at least `www`, `api`, `admin`, `app`, `auth`, `billing`, `cdn`, `docs`, `mail`, `smtp`, `static`, `status`, and `support`. Enforce uniqueness with a database constraint.

The wake service must extract and normalize the host, then query the tenant table. It must never construct or start a Docker resource solely from an untrusted `Host` header.

## Tenant Provisioning

Provisioning is an idempotent operation keyed by `Tenant.Id`:

1. Validate and reserve the subdomain in PostgreSQL.
2. Create the tenant record in `Provisioning` state.
3. Create a dedicated Docker volume mounted at `/data`.
4. Generate the single-user credentials with a cryptographically secure password. Do not log secrets.
5. Create the container from an immutable image digest.
6. Set `SingleUser`, storage, entitlement, and AI gateway configuration.
7. Apply exact-host Traefik labels and attach only the required networks.
8. Apply resource limits, a non-root user, dropped capabilities, and a read-only root filesystem where supported; `/data` remains writable.
9. Start the container and wait for Docker health status `healthy` with a bounded timeout.
10. Save the container ID and mark the tenant `Running`.
11. If a step fails, retain enough operation state for safe retry and clean up only resources created by that failed operation.

The existing configuration-driven username and password are sufficient for an internal prototype. Before public launch, add a one-time account activation or signed cross-subdomain launch flow so control-plane credentials are not retained as plaintext container configuration. Authentication design is a launch blocker, not a post-launch refinement.

## Entitlements

Create a small tenant-side `Hosting` options model with at least:

- `Mode`: `SelfHosted` or `Managed`
- `Plan`: `Free` or `Paid`
- `TenantId`
- `ControlPlaneUrl`
- short-lived or rotatable machine credential

Feature access must be enforced by backend APIs, with frontend state used only to hide or explain unavailable controls. Do not trust a query string, browser flag, or editable local setting as proof of a paid subscription.

For the first release, the control plane is authoritative and injects signed or otherwise authenticated entitlement configuration when creating/recreating a container. A reconciliation worker updates tenants after subscription changes. A paid-to-free transition disables new AI use and managed backup creation but does not delete tenant data or existing backups before their stated retention date.

Self-hosted mode must not contact the control plane for an entitlement and must continue to support user-provided AI configuration.

## Hosted AI

Do not place the provider's shared OpenAI key in every paid tenant container. Add an authenticated AI gateway to the control plane:

```text
paid tenant -> SaaS AI gateway -> configured AI provider
```

The existing `AI__BaseUrl` configuration can point managed tenants to the gateway. Give each tenant a rotatable machine token, validate its paid entitlement at the gateway, and apply per-tenant rate and spending limits there.

Implementation requirements:

- Hosted Free receives no AI gateway credential and reports AI as disabled.
- Hosted Paid receives gateway configuration, never the upstream provider key.
- The gateway records tenant, operation type, token usage, estimated cost, and timestamp without logging prompts or journal content by default.
- Enforce both per-tenant monthly limits and a global emergency limit.
- Return a stable quota-exceeded response that the tenant UI can explain.
- Disable access promptly when a subscription expires, while allowing a short webhook/reconciliation grace period.
- Keep self-hosted bring-your-own-key behavior unchanged.

## Backup and Restore

The complete tenant state is the `/data` volume, including SQLite, uploads, and persisted data-protection keys. A database-only copy is not a complete backup.

Managed backups are a Paid feature. Use object storage with encryption, checksums, retention rules, and tenant-scoped object keys.

### Backup flow

1. Acquire a distributed per-tenant operation lock.
2. Reject or queue the job if provisioning, restore, deletion, or upgrade is active.
3. Stop the tenant container for the first implementation to guarantee a consistent SQLite and uploads snapshot.
4. Archive the volume using a short-lived, tightly constrained helper container.
5. Upload the archive to object storage and verify its checksum.
6. Record application and schema versions in the backup row.
7. Restart the tenant if it was running and wait until healthy.
8. Mark the backup complete only after upload verification and successful metadata persistence.

Later, online SQLite backup support may reduce downtime, but it must also define upload consistency. Do not copy a live SQLite file with ordinary filesystem copy semantics.

### Restore flow

1. Require recent reauthentication and explicit confirmation.
2. Acquire the tenant operation lock and stop the container.
3. Automatically create a pre-restore safety backup.
4. Verify archive ownership, checksum, format, and compatible application/schema version.
5. Restore into a new volume rather than overwriting the current volume in place.
6. Start a replacement container against the new volume and wait until healthy.
7. Atomically update the tenant record to the replacement container and volume.
8. Retain the previous volume for a short rollback window, then delete it through a scheduled cleanup job.

Test restore regularly. A successful upload is not evidence that a backup is restorable.

Suggested initial policy: daily backups, seven-day retention, manual backup before upgrades/restores, and one restore operation at a time per tenant. Final limits belong in product configuration, not hard-coded business logic.

## Polar Billing

Integrate Polar only in the control plane.

1. Create one paid product/price and map its immutable provider ID to the internal `Paid` plan.
2. Create checkout sessions server-side for authenticated accounts.
3. Store Polar customer and subscription IDs; do not infer entitlement from the return URL.
4. Verify webhook signatures against the raw request body.
5. Store webhook event IDs and process each event idempotently.
6. Translate provider states into the internal subscription state machine.
7. Expose a billing portal/action for updating or cancelling the subscription.
8. Run a periodic reconciliation job so missed or delayed webhooks cannot leave permanent entitlement drift.

Subscription transitions:

- `Free -> Paid`: enable AI gateway access and schedule the first backup.
- `Paid -> cancel at period end`: retain Paid features until the paid period ends.
- `Paid -> past due`: apply a documented grace period.
- `Paid -> Free`: revoke AI access, stop future managed backups, retain data, and retain existing backups according to policy.
- account deletion: cancel billing as appropriate, revoke credentials, stop the tenant, and run the documented delayed deletion workflow.

Keep provider DTOs out of core business logic behind an `IBillingProvider` interface.

## Wake-Up and Scale-to-Zero

Use one in-process and distributed per-tenant lock so concurrent requests and multiple control-plane replicas cannot issue conflicting Docker operations.

Wake request flow:

1. Validate the host and load the tenant by normalized subdomain.
2. Return `404` for an unknown hostname and a specific unavailable page for suspended/deleting tenants.
3. Acquire the tenant lock.
4. Inspect the stored container ID and verify labels identify the expected tenant.
5. Start a stopped container, or enqueue repair if it is missing.
6. Release the HTTP request from the startup work: return a lightweight `Starting your workspace...` page that reloads every two seconds.
7. Continue startup under a bounded background operation and update status when Docker reports healthy.
8. On reload, Traefik's priority `100` exact route serves the tenant.

The starting page must use HTML escaping, no tenant-provided markup, `Cache-Control: no-store`, and a maximum wait state that changes to a useful retry/support message.

## Activity and Idle Shutdown

The tenant application has the best signal for authenticated activity. Add throttled activity reporting from managed tenants to the control plane:

- Report at most once every five minutes per tenant.
- Authenticate reports with the tenant machine credential.
- Count meaningful authenticated API or page activity, not health probes or static assets.
- Buffer failures locally/in memory; activity reporting must not make the product request fail.

An idle worker runs every five minutes:

1. Select `Running` tenants whose `LastRequestUtc` is older than the configured timeout, initially 45 minutes.
2. Acquire the tenant operation lock and re-read activity/state.
3. Stop, but do not remove, the container.
4. Mark the tenant `Stopped` only after Docker confirms the state.

Do not stop tenants during backup, restore, provisioning, upgrade, or active AI streaming. Add startup-duration and cold-start-rate metrics before tuning the timeout.

## Container Lifecycle and Upgrades

Stopping preserves labels and configuration, but containers must still be treated as replaceable compute. Volumes are the durable tenant resource.

For upgrades:

1. Publish a versioned image and immutable digest.
2. Create a paid pre-upgrade backup where available; for Hosted Free, preserve the old volume for rollback.
3. Stop the old container.
4. Create a replacement container against the same volume and new image.
5. Wait for readiness and run a smoke check.
6. Update the control-plane record, then remove the old container.
7. Roll back to the old image/container if startup or schema upgrade fails where schema compatibility allows.

Never start two application containers against the same SQLite volume.

## Security Requirements

- Only the provisioning/wake component may access Docker. Prefer a restricted Docker socket proxy or a dedicated Docker API boundary over mounting the raw socket into a public API process.
- Tenant containers have no Docker access and no control-plane database credentials.
- Use Docker secrets or an external secret manager for DNSimple, Polar, object storage, signing, and upstream AI credentials.
- Pin tenant images by digest and validate resource ownership labels before start/stop/delete operations.
- Set CPU, memory, PID, and log-size limits per tenant.
- Keep the tenant root filesystem and capabilities minimal; run as the existing non-root `app` user.
- Rate-limit signup, subdomain checks, wake requests, checkout creation, restore, and AI gateway traffic.
- Protect all state-changing control-plane endpoints against CSRF/session abuse.
- Never log passwords, machine tokens, webhook secrets, AI prompts, journal text, or backup contents.
- Audit subscription, provisioning, backup, restore, suspension, and deletion actions.
- Document privacy, subprocessors, retention, export, and deletion behavior before accepting hosted users.

## Observability and Operations

Track at minimum:

- tenant counts by lifecycle state and plan
- provisioning/start/stop duration and failure count
- cold starts and wake failures
- running container count and host CPU/memory/disk pressure
- volume and backup storage usage
- backup age, failure rate, and restore-test results
- AI requests, quota rejections, token estimates, and cost by tenant
- Polar webhook age/failures and subscription reconciliation drift

Use correlation IDs across the control plane, operation records, and tenant lifecycle logs. Alert on stale provisioning operations, unhealthy paid tenants, missed backup objectives, low disk space, repeated restore failures, and billing webhook backlog.

## Implementation Phases

### Phase 0: Decisions and local proof

- Choose the hosted domain, DNSimple account, object storage, production secret store, and Polar organization/product.
- Confirm AGPL source-offer and commercial messaging with legal review.
- Define hosted plan limits, backup retention, grace periods, deletion delay, and AI quota.
- Manually attach one current application container to Traefik with a dedicated volume and exact host label.
- Prove wildcard TLS, health routing, stop/wake/reload behavior, and no host port publication.

Exit criterion: a manually stopped test tenant can be reached through the fallback and restarted without data loss.

### Phase 1: Control-plane foundation

- Expand the SaaS solution with a PostgreSQL schema, authentication, tenant dashboard, and subdomain reservation.
- Add Docker.DotNet behind a narrow `ITenantRuntime` interface.
- Implement idempotent create, inspect, start, stop, and replace operations.
- Add operation records, distributed locking, and reconciliation of database state against Docker state.
- Add focused integration tests using disposable containers and volumes.

Exit criterion: an authenticated account can provision one isolated healthy tenant and revisit it by subdomain.

### Phase 2: Wake and idle shutdown

- Add the Traefik fallback route and starting page.
- Implement authenticated throttled activity reporting from managed tenant containers.
- Implement idle stopping with exclusions for active operations.
- Add cold-start metrics and failure recovery.

Exit criterion: `running -> idle stop -> hostname visit -> healthy tenant` passes repeatedly, including concurrent visits.

### Phase 3: Plans and Polar

- Add internal entitlements and backend enforcement.
- Add Polar checkout, verified idempotent webhooks, billing management, and reconciliation.
- Add plan-aware UI to the SaaS dashboard and tenant product.
- Publish the Self-hosted, Hosted Free, and Hosted Paid comparison.

Exit criterion: subscription state changes produce the correct entitlement without trusting browser redirects or frontend state.

### Phase 4: Managed AI

- Implement the AI gateway, per-tenant machine credentials, quotas, usage ledger, and emergency cutoff.
- Point paid managed tenants at the gateway through existing AI configuration.
- Verify Hosted Free AI is disabled and self-hosted bring-your-own-key still works.
- Add clear quota and temporary-provider-error states in the product UI.

Exit criterion: no tenant contains the upstream AI provider key, and plan/quota enforcement is covered by integration tests.

### Phase 5: Backup and restore

- Implement stopped-volume archive backups, encrypted object storage, checksums, retention, and backup status UI.
- Implement new-volume restore with pre-restore backup and rollback window.
- Add scheduled restore drills and compatibility tests across supported application versions.
- Ensure Hosted Free cannot request managed backups while its data volume remains durable during normal operation.

Exit criterion: a paid tenant can create data and uploads, back up, mutate data, restore, and recover the exact prior state.

### Phase 6: Production hardening

- Add resource quotas, secret rotation, Docker privilege isolation, rate limits, audit logs, alerts, and operator runbooks.
- Implement rolling tenant image upgrades and rollback behavior.
- Test host restart, control-plane restart, Docker daemon restart, full disk, corrupt archive, missed webhook, provider outage, and concurrent lifecycle operations.
- Complete privacy, terms, backup policy, AI usage policy, incident response, export, and account deletion documentation.
- Run a limited paid beta before broad signup.

Exit criterion: operational recovery is documented and exercised, not only implemented.

## Required Automated Tests

- Subdomain normalization, reserved names, uniqueness, and malicious Host headers.
- Idempotent provisioning and cleanup after every failed provisioning step.
- Container ownership validation before all Docker mutations.
- Concurrent wake requests start a tenant once.
- Idle worker rechecks activity under lock and never stops an active operation.
- Exact tenant route wins over fallback while healthy.
- Stopped tenant reaches fallback with the production Traefik version.
- Free/Paid/self-hosted entitlement matrix is enforced server-side.
- Polar signature verification, duplicate events, out-of-order events, cancellation, grace period, and reconciliation.
- AI gateway authentication, quota enforcement, provider failure, and revoked subscription.
- Backup checksum, retention, interrupted upload, restore into a new volume, rollback, and version incompatibility.
- Tenant isolation: one tenant cannot address another tenant's container, volume, backup, activity, or AI usage.
- Existing product backend tests plus `npm run check`, `npm run lint`, and `npm run build` continue to pass in self-hosted mode.

## MVP Scope

The smallest credible hosted beta includes:

- wildcard DNS and TLS
- control-plane account and one tenant per account
- one container and one volume per tenant
- exact routing, fallback wake-up, and idle shutdown
- Hosted Free entitlement with AI disabled
- Polar subscription and Hosted Paid entitlement
- AI gateway with a conservative paid quota
- daily paid backups and new-volume restore
- explicit free self-hosting messaging
- lifecycle, billing, backup, and AI monitoring

Defer custom domains, teams, multiple tenants per account, online backups, regional placement, Kubernetes, and automatic cross-host scheduling until real usage demonstrates the need.

## Definition of Done

The SaaS launch is ready when:

- A user can choose a valid subdomain, create a Hosted Free tenant, and use the core product.
- An idle tenant stops and reliably wakes through its hostname without data loss.
- A user can subscribe through Polar and receives Paid entitlements through verified server-side state.
- Paid AI works through the gateway within quota, while Hosted Free AI remains unavailable.
- A paid user can view backups and restore successfully; Hosted Free receives no managed backup claim.
- The same repository can still be built and run independently with the root self-hosted Docker Compose instructions.
- Pricing and product UI clearly explain that self-hosting is free and may use a self-supplied AI key and user-managed backups.
- Tenant isolation, restore, billing idempotency, and Docker lifecycle tests pass in CI.
- Operators have tested runbooks for failed starts, failed backups, missed billing events, bad releases, and host recovery.