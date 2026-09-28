# Production Rollout Runbook

This document describes how to release the Help Motivate Me tenant application and SaaS control plane without breaking active Habit Spaces or browser clients.

> **Current status:** the stack is local development only. There is no production environment yet. Local databases, containers, and volumes may be deleted when test data is no longer needed. The production safeguards in this document become mandatory before real customer data exists.

## Release Units

Treat these as independently deployable products:

| Release unit | Repository | Persistent state | Compatibility responsibility |
| --- | --- | --- | --- |
| Tenant application | `help-motivate-me` | Each tenant's `/data` volume | Browser UI, API, SQLite schema, uploads, data-protection keys, and VAPID keys |
| SaaS control plane | `help-motivate-me-saas` | Control-plane database and backups | Provisioning, routing, tenant lifecycle, configuration, and image selection |

The control plane consumes the tenant application through the [tenant runtime contract](tenant-runtime-contract.md). Never require both repositories to deploy at exactly the same instant.

## Safety Rules

1. Publish immutable image tags such as a commit SHA and record the resolved image digest. Never deploy `main`, `latest`, or a mutable local tag to production.
2. Keep the new control plane compatible with both the currently deployed tenant image and the next tenant image during a rollout.
3. Keep the new tenant API compatible with browser assets from the previous release. Users may have an older service worker or an open tab during deployment.
4. Make database changes additive first. Add tables or nullable columns before code requires them; backfill separately; remove old fields only in a later release.
5. Keep all tenant state under `/data`. Container replacement must never use `RemoveVolumes=true`.
6. Back up and test restore procedures before a migration that changes persistent data.
7. Deploy to one canary Habit Space before updating the fleet.
8. Stop a rollout when health checks, error rates, data validation, or a critical user journey regresses.

## Production Readiness Gate

Do not expose the current local stack to the internet. Before the first production rollout, implement and verify:

- authentication and authorization for signup, administration, tenant settings, backups, and lifecycle APIs;
- TLS, production DNS, secure cookies, trusted proxy configuration, and restricted CORS;
- a restricted Docker API proxy or an orchestrator that removes direct control-plane access to the Docker socket;
- production-grade control-plane persistence, migrations, concurrency control, and tested point-in-time recovery;
- encrypted off-host tenant backups with retention, restore drills, and access auditing;
- centralized logs, metrics, alerts, audit history, and secret management;
- resource limits, rate limits, abuse controls, and capacity planning;
- billing and entitlement enforcement before accepting paid customers;
- a documented incident owner, maintenance procedure, and customer communication path.

Passing application tests is necessary but does not satisfy this gate by itself.

## Change Classification

Classify every release before deployment.

### Tenant-only

Examples include UI changes, API behavior, and additive tenant database changes. Publish a new tenant image, canary it, then replace existing tenant containers in batches.

### Control-plane-only

Examples include administration UI or lifecycle changes. The new control plane must work with the tenant image currently pinned in production. Deploying it must not implicitly recreate every tenant.

### Runtime-contract change

Examples include ports, health endpoints, environment variables, `/data` layout, startup behavior, or shutdown behavior. Follow an expand-and-contract sequence:

1. Add support without removing the old behavior.
2. Deploy the backward-compatible producer or consumer first.
3. Roll out the corresponding release across the fleet.
4. Confirm no deployed component requires the old behavior.
5. Remove compatibility code in a later release.

### Destructive data change

Renames, type changes, dropped fields, and irreversible data transformations require a dedicated migration plan. Prefer a multi-release sequence: add, dual-read or dual-write, backfill, switch reads, then remove. If rollback would require restoring data, schedule a maintenance window and prove the restore procedure first.

## Pre-Release Checklist

Record the release in a ticket or deployment log:

- tenant application commit, immutable image tag, and digest;
- control-plane commit and image digest;
- previous known-good digests;
- change classification and expected database changes;
- runtime-contract version or compatibility statement;
- owner, start time, rollback decision deadline, and monitoring links.

Validate the public tenant application:

```bash
cd help-motivate-me
ConnectionStrings__DefaultConnection='Data Source=/tmp/help-motivate-me-tests/helpmotivateme.db;Foreign Keys=True;Default Timeout=30' \
LocalStorage__BasePath=/tmp/help-motivate-me-tests/uploads \
  dotnet test backend/HelpMotivateMe.sln
npm --prefix frontend run check
npm --prefix frontend run build
docker build -t help-motivate-me:release-check .
```

Validate the private control plane:

```bash
cd help-motivate-me-saas
dotnet test HelpMotivateMe.SaaS.slnx
docker compose -f compose.yml config --quiet
docker build -t help-motivate-me-saas:release-check .
```

For cross-repository changes, complete the checks in [cross-repository-development.md](cross-repository-development.md). Resolve newly introduced security advisories before release, or record an explicit risk acceptance with an owner and expiry date.

## Backup Gate

Before changing schemas or tenant runtime behavior:

1. Verify the most recent control-plane backup is complete, encrypted, and stored off-host.
2. Verify every tenant in the rollout batch has a recent backup.
3. Confirm backups include the complete `/data` contents, especially SQLite, uploads, data-protection keys, and `vapid-keys.json`.
4. Restore a representative backup into an isolated environment and run readiness plus a login/data smoke test.
5. Record backup identifiers and restore results in the release log.

A backup that has not passed a restore drill is not a verified backup.

## Tenant Application Rollout

1. Build and publish the tenant image under an immutable commit tag.
2. Resolve and record its registry digest.
3. Run vulnerability and provenance checks required by the production platform.
4. Set the control plane's tenant image reference to the immutable tag or digest.
5. Provision a disposable canary Habit Space from the production control plane.
6. Verify `/health/live`, `/health/ready`, login, settings, data writes, uploads, PWA manifest, push subscription, and a test notification.
7. Replace one existing internal or test Habit Space through the control plane's replacement path.
8. Confirm its container uses the new digest and the same `/data` volume.
9. Verify existing identities, habits, journal entries, uploads, login cookies, installed PWA identity, and push subscriptions.
10. Observe the canary for the agreed soak period.
11. Replace customer tenants in small batches with a pause between batches.
12. Monitor replacement failures, readiness latency, HTTP errors, login failures, database errors, and notification failures.
13. Mark the rollout complete only after every intended tenant reports the target digest.

Rebuilding or retagging an image does not update an existing container. Restart also starts the same container. Existing Habit Spaces must go through the control plane's safe replacement path, which retains the volume, starts the replacement, waits for health, and removes the old container only after success.

## Control-Plane Rollout

1. Confirm the release can read the current control-plane schema and manage both old and new tenant image versions.
2. Back up the control-plane database.
3. Deploy one control-plane instance or staging environment first.
4. Verify signup, admin listing, start, stop, wake, settings replacement, backup, and restore.
5. Deploy production without changing the pinned tenant image unless the release plan explicitly includes a tenant rollout.
6. Confirm existing tenant routes continue serving before provisioning or replacing tenants.
7. Observe lifecycle errors and database writes through the soak period.

When multiple control-plane instances are introduced, schema migration and tenant replacement operations must use distributed locking or another proven single-writer mechanism.

## Browser Compatibility

An active browser may run the previous frontend while calling the new API. During at least one release window:

- preserve existing API routes, fields, and response meanings;
- add fields as optional and tolerate unknown fields;
- do not reuse an old asset URL for different content;
- keep hashed static assets available long enough for open tabs to finish loading;
- ensure service-worker activation does not strand the old application shell;
- test an upgrade from an installed PWA and from a tab left open on the previous version.

Do not use “ask users to hard refresh” as the production update strategy. Fix service-worker or cache-version behavior before continuing a rollout.

## Rollback

### Tenant image

1. Stop further replacements.
2. Re-pin the previous immutable tenant image digest.
3. Determine whether the new release made backward-compatible data changes.
4. If compatible, replace affected canaries or batches through the normal safe replacement path.
5. If incompatible, do not start old code against new data. Prefer a forward fix; otherwise stop the tenant and restore its verified pre-release backup.
6. Re-run health and user-journey checks and record which tenants were rolled back.

### Control plane

1. Stop provisioning and configuration changes.
2. Roll back to the previous immutable control-plane image only if it supports the current database schema.
3. If the schema is incompatible, prefer a forward fix or restore the control-plane database under a maintenance window.
4. Confirm tenant routing remains available independently of control-plane recovery where the platform supports it.

Never delete a tenant volume as part of rollback.

## Stop Conditions

Pause the rollout immediately when any of these occur:

- a replacement loses or mounts the wrong tenant volume;
- readiness fails or startup time exceeds the release threshold;
- authentication, encryption keys, uploads, or push subscriptions are lost;
- database corruption, migration failure, or unexpected schema drift appears;
- elevated server errors, login failures, or client-side exceptions persist;
- rollback compatibility is uncertain.

Preserve logs and affected containers for investigation. Do not repeatedly retry a state-changing migration without understanding whether it is idempotent.

## Post-Release Checklist

- Confirm all intended tenants use the recorded target digest.
- Run critical user journeys on desktop, mobile, and an installed PWA.
- Confirm new tenants provision successfully from the same digest.
- Confirm stop and wake behavior still works.
- Confirm backups continue and perform a sampled restore.
- Review errors and alerts after the soak period.
- Keep the previous image and backups available for the retention period.
- Record the result, incidents, manual actions, and final decision in the release log.

## Local Development Reset

Today, all SaaS data is local and disposable. It is acceptable to clear the control-plane database, tenant containers, tenant volumes, and backups when a clean test environment is useful:

```bash
cd help-motivate-me-saas
docker compose down -v
docker rm -f $(docker ps -aq --filter label=com.helpmotivateme.managed=true)
docker volume rm $(docker volume ls -q --filter label=com.helpmotivateme.managed=true)
```

These commands permanently delete local Habit Spaces and must never be copied into a production procedure. Before production exists, replace this section with environment-specific, authenticated deployment commands and tested backup locations rather than weakening the production rules above.