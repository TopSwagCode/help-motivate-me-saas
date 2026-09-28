# Cross-Repository Development

The public tenant application and private hosted control plane are separate products joined by a versioned container runtime contract. Keep each repository independently buildable, testable, reviewable, and releasable.

## Repository Ownership

| Repository | Owns |
| --- | --- |
| `help-motivate-me` | Tenant backend and frontend, root tenant image, `/data` persistence, health endpoints, self-host configuration, and public documentation |
| `help-motivate-me-saas` | Hosted signup, provisioning, lifecycle, routing, entitlements, managed AI, backups, billing, and operations |

Do not add source or project references between repositories. The SaaS control plane consumes a published tenant image through [tenant-runtime-contract.md](tenant-runtime-contract.md).

## Local Setup

Keep sibling checkouts with these names unless `HMM_APP_REPO` is set:

```text
git/
├── help-motivate-me/
└── help-motivate-me-saas/
```

Open `help-motivate-me-saas.code-workspace` for one multi-root VS Code window. Each folder remains a separate Git repository. Two editor windows work as well, but only one should start the hosted Compose stack.

Build and start the hosted stack against local tenant source:

```bash
cd help-motivate-me-saas
docker compose -f compose.yml -f compose.local.yml --profile bootstrap build
docker compose -f compose.yml -f compose.local.yml up -d --build --wait
```

Rebuild the bootstrap image after every tenant application change and before provisioning a Habit Space:

```bash
docker compose -f compose.yml -f compose.local.yml --profile bootstrap build tenant-image
```

Docker resolves the image tag when a tenant container is created. Rebuilding `help-motivate-me:saas-local` does not update existing tenant containers, and Restart starts the same container again. To move an existing Habit Space to the rebuilt image while preserving its `/data` volume, open its management page and save Settings without changing them. This uses the control plane's safe container-replacement path.

For another checkout location:

```bash
HMM_APP_REPO=/absolute/path/to/help-motivate-me \
  docker compose -f compose.yml -f compose.local.yml --profile bootstrap build
```

## Choose The Change Order

### Tenant-only change

Change and validate `help-motivate-me`. Rebuild `tenant-image` through `compose.local.yml` only when hosted behavior could be affected.

### SaaS-only change

Change and validate `help-motivate-me-saas` against its currently pinned tenant image. Do not require an unpublished tenant change.

### Contract change

Use this order when ports, health endpoints, `/data`, configuration, startup, shutdown, or capabilities change:

1. Update [tenant-runtime-contract.md](tenant-runtime-contract.md) and define compatibility expectations.
2. Make the tenant change backward compatible where possible.
3. Add or update public runtime-contract smoke tests.
4. Build and test the public tenant image locally.
5. Test the SaaS control plane against the local image through `compose.local.yml`.
6. Publish the tenant image with an immutable commit tag or digest.
7. Update the private repository's pinned image in a separate reviewed change.
8. Run provisioning, health, stop/wake, settings, and backup/restore checks.
9. Remove old contract behavior only after deployed SaaS versions no longer depend on it.

This sequence keeps either repository deployable if one change merges before the other.

## Validation

Run public checks from `help-motivate-me`:

```bash
ConnectionStrings__DefaultConnection='Data Source=/tmp/help-motivate-me-tests/helpmotivateme.db;Foreign Keys=True;Default Timeout=30' \
LocalStorage__BasePath=/tmp/help-motivate-me-tests/uploads \
  dotnet test backend/HelpMotivateMe.sln
npm --prefix frontend run check
SINGLE_USER_USERNAME=admin SINGLE_USER_PASSWORD=development-only-password docker build -t help-motivate-me:contract-check .
```

Run private checks from `help-motivate-me-saas`:

```bash
dotnet test HelpMotivateMe.SaaS.slnx
docker compose -f compose.yml config --quiet
docker compose -f compose.yml -f compose.local.yml --profile bootstrap config --quiet
docker build -t help-motivate-me-saas:check .
```

For a cross-repository runtime change, also verify:

- a new tenant can be provisioned;
- `/health/live` and `/health/ready` succeed;
- tenant data survives container replacement;
- a stopped tenant wakes from its hostname;
- settings replacement preserves data and applies configuration;
- the runtime manifest uses the configured Habit Space name and generated VAPID keys survive replacement;
- backup and restore complete with the tenant stopped;
- the base Compose stack works with a published image and no sibling checkout.

## Git Discipline

Inspect both worktrees before and after a cross-repository task:

```bash
git -C ../help-motivate-me status --short
git status --short
```

Create separate commits and pull requests in each repository. Link the related changes and state the required merge/deployment order. Never include private implementation details, plans, credentials, databases, archives, or hosted runtime data in the public repository.

Use immutable tenant image references outside local development. Do not make production depend on `latest`, `main`, or the presence of a sibling checkout.

## Runtime Safety

Before changing Compose ownership, image defaults, persistence, backup, or restore behavior:

1. Record current control-plane and tenant image IDs.
2. Stop the control plane for a cold backup when database consistency requires it.
3. Store local archives outside either repository, preferably under `/tmp` or protected backup storage.
4. Never run `docker compose down -v` during migration or upgrade work.
5. Confirm the control-plane volume, proxy network, tenant volumes, container names, and management labels remain unchanged.

The private `.gitignore` rejects `*.tar.gz`, the standard cold-backup filename, and both `backup/` and `backups/` directories as a second line of defense.