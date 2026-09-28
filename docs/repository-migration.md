# Move Hosted SaaS to a Separate Repository

## Status

Local repository migration completed on 2026-09-28. Initial commits, pushes, branch protection, and remote CI configuration remain operator tasks.

Move the hosted control plane from the public `help-motivate-me` repository into the private sibling repository `help-motivate-me-saas`. Keep the self-hostable tenant application public and independently buildable.

Expected local layout:

```text
/git/
├── help-motivate-me/       # Public tenant application
└── help-motivate-me-saas/  # Private hosted control plane
```

The sibling repository is not visible in the current dev container because only `help-motivate-me` is mounted. The SaaS development environment must explicitly mount or open both repositories before an editor or agent can work across them.

## Goals

- Keep all proprietary hosted-platform code and plans in `help-motivate-me-saas`.
- Keep the complete self-hostable application in `help-motivate-me`.
- Allow either repository to build and test independently in CI.
- Allow local SaaS development to rebuild the tenant image from the sibling application checkout.
- Allow two VS Code windows, or one multi-root VS Code window and one agent, to work on both repositories.
- Preserve current local tenant containers, volumes, URLs, and control-plane data during the move.
- Avoid Git submodules and avoid copying the tenant application source into the SaaS repository.

## Ownership Boundary

### Public `help-motivate-me`

Keep:

- `backend/`
- `frontend/`
- root `Dockerfile`
- root `docker-compose.yml`
- tenant health endpoints
- `/data` persistence contract
- `SingleUser` and AI configuration support
- self-host backup documentation
- public product documentation

The public repository produces the tenant workload image. It must not depend on the SaaS repository.

### Private `help-motivate-me-saas`

Move:

- the complete current `saas/` directory
- `ideas/0002-Payment.md`
- `ideas/0004-Custom-domains.md`
- future hosted billing, account ownership, managed AI, backup, routing, and operations plans

The private repository owns:

- signup and hosted-plan presentation
- tenant provisioning and lifecycle
- Docker and Traefik orchestration
- scale-to-zero and wake-up
- hosted account ownership
- paid entitlements
- managed AI credentials
- managed backup and restore
- custom domains and production TLS
- billing integration
- hosted administration and operational tooling

After migration, the public repository may retain a short document explaining that an official hosted service exists, but it should not contain private implementation details.

## Target SaaS Repository Layout

```text
help-motivate-me-saas/
├── .devcontainer/
│   └── devcontainer.json
├── .github/
│   └── workflows/
├── docs/
│   ├── architecture.md
│   ├── local-development.md
│   ├── tenant-runtime-contract.md
│   ├── testing-scale-to-zero.md
│   └── custom-domains.md
├── ideas/
│   └── hosted-saas-plan.md
├── src/
│   └── HelpMotivateMe.SaaS.Api/
├── tests/
│   └── HelpMotivateMe.SaaS.Tests/
├── compose.yml
├── compose.local.yml
├── Dockerfile
├── HelpMotivateMe.SaaS.slnx
├── README.md
└── .gitignore
```

The move changes paths as follows:

| Current path | New path |
| --- | --- |
| `saas/src/` | `src/` |
| `saas/tests/` | `tests/` |
| `saas/Dockerfile` | `Dockerfile` |
| `saas/compose.yml` | `compose.yml` |
| `saas/HelpMotivateMe.SaaS.slnx` | `HelpMotivateMe.SaaS.slnx` |
| `saas/README.md` | `README.md` |
| `saas/TESTING-SCALE-TO-ZERO.md` | `docs/testing-scale-to-zero.md` |
| `ideas/0002-Payment.md` | `ideas/hosted-saas-plan.md` |
| `ideas/0004-Custom-domains.md` | `docs/custom-domains.md` |

## Tenant Runtime Contract

The SaaS repository must consume the public application through an explicit runtime contract rather than source references.

Document and test this contract in `docs/tenant-runtime-contract.md`:

- image listens on port `8080`;
- `GET /health/live` reports process health;
- `GET /health/ready` reports application and SQLite readiness;
- mutable state is under `/data`;
- SQLite and uploads survive container replacement through the `/data` volume;
- `SingleUser__Username`, `SingleUser__Password`, and `SingleUser__DisplayName` configure the tenant account;
- `AI__Provider`, `AI__ApiKey`, `AI__BaseUrl`, `AI__ChatModel`, `AI__EnableTranscription`, and `AI__TranscriptionModel` configure AI;
- the tenant container runs without Docker-socket access;
- graceful stop completes before backup or restore accesses the volume.

Contract changes must be backward compatible or versioned. The SaaS repository should pin a known tenant image tag or digest in production rather than implicitly consuming `latest`.

## Compose Design

### Base Compose

`compose.yml` in the SaaS repository should build only the control plane and use a configurable tenant image:

```yaml
services:
  control-plane:
    build:
      context: .
      dockerfile: Dockerfile
    environment:
      TenantRuntime__Image: ${HMM_TENANT_IMAGE:-ghcr.io/topswagcode/help-motivate-me:main}

  tenant-image:
    image: ${HMM_TENANT_IMAGE:-ghcr.io/topswagcode/help-motivate-me:main}
    entrypoint: ["/bin/true"]
    profiles: ["bootstrap"]
```

Production and CI pull an immutable published tenant image. They do not clone the public source repository merely to build the SaaS control plane.

### Local Source Override

`compose.local.yml` should replace the bootstrap image with a build from the sibling checkout:

```yaml
services:
  tenant-image:
    build:
      context: ${HMM_APP_REPO:-../help-motivate-me}
      dockerfile: Dockerfile
    image: help-motivate-me:saas-local

  control-plane:
    environment:
      TenantRuntime__Image: help-motivate-me:saas-local
```

Local startup becomes:

```bash
cd /workspaces/help-motivate-me-saas
docker compose -f compose.yml -f compose.local.yml --profile bootstrap build
docker compose -f compose.yml -f compose.local.yml up -d --build --wait
```

`HMM_APP_REPO` supports nonstandard checkout locations:

```bash
HMM_APP_REPO=/absolute/path/to/help-motivate-me \
  docker compose -f compose.yml -f compose.local.yml --profile bootstrap build
```

Keep this line in `compose.yml`:

```yaml
name: help-motivate-me-saas
```

The explicit Compose project name preserves existing resources such as `help-motivate-me-saas_control-plane-data`, the proxy network, and dynamically provisioned tenant names after the Compose file moves.

Update the SaaS `Dockerfile` to remove the old `saas/` path prefix:

```dockerfile
COPY src/HelpMotivateMe.SaaS.Api/HelpMotivateMe.SaaS.Api.csproj src/HelpMotivateMe.SaaS.Api/
RUN dotnet restore src/HelpMotivateMe.SaaS.Api/HelpMotivateMe.SaaS.Api.csproj
COPY src/HelpMotivateMe.SaaS.Api/ src/HelpMotivateMe.SaaS.Api/
```

## Side-by-Side VS Code Development

### Recommended: One Multi-Root Window

Create `help-motivate-me-saas.code-workspace` in the SaaS repository:

```json
{
  "folders": [
    { "name": "SaaS", "path": "." },
    { "name": "Tenant app", "path": "../help-motivate-me" }
  ],
  "settings": {
    "dotnet.defaultSolution": "HelpMotivateMe.SaaS.slnx"
  }
}
```

Open this workspace when changing the hosted platform and tenant contract together. Each folder remains a separate Git repository with separate source control actions.

The SaaS dev container must mount the sibling checkout because the current app dev container exposes only one repository. Add a mount similar to:

```json
{
  "workspaceFolder": "/workspaces/help-motivate-me-saas",
  "mounts": [
    "source=${localWorkspaceFolder}/../help-motivate-me,target=/workspaces/help-motivate-me,type=bind"
  ]
}
```

Verify `${localWorkspaceFolder}` behavior on Linux, macOS, and Windows before committing this as the only supported setup. If the mount is unreliable on a host, use a parent-folder development workspace that bind-mounts the whole parent directory to `/workspaces`.

With both paths visible inside the same container, one agent can:

- edit either repository using absolute workspace paths;
- run each repository's tests independently;
- build the tenant image from `../help-motivate-me`;
- inspect diffs and create commits separately in each repository.

Never combine changes from both repositories in one Git commit.

### Alternative: Two Editor Windows

Open each repository in its own VS Code window and dev container:

1. `help-motivate-me` for tenant backend/frontend work.
2. `help-motivate-me-saas` for control-plane work.

Both dev containers can use the host Docker daemon. Only one window should start the hosted Compose stack to avoid port and project-name conflicts. The tenant editor may run its normal frontend/backend development servers independently.

### Agent Workflow

For a cross-repository task, give the agent both absolute roots and identify the owning repository for each requested change. The agent should:

1. inspect status in both repositories;
2. update the tenant contract first when necessary;
3. build and test the public application;
4. rebuild the local tenant image from the sibling checkout;
5. update and test the SaaS control plane;
6. report validation and diffs per repository;
7. never commit or push one repository implicitly while working on the other.

## CI and Image Flow

### Public Application CI

On merges and releases in `help-motivate-me`:

1. run backend and frontend tests;
2. build the root tenant `Dockerfile`;
3. run runtime-contract smoke tests;
4. publish to GHCR with immutable commit and release tags;
5. optionally notify the SaaS repository through `repository_dispatch` that a candidate image is available.

Recommended tags:

```text
ghcr.io/topswagcode/help-motivate-me:sha-<commit>
ghcr.io/topswagcode/help-motivate-me:v<release>
```

### Private SaaS CI

On changes in `help-motivate-me-saas`:

1. run `dotnet test HelpMotivateMe.SaaS.slnx`;
2. build the control-plane image;
3. pull the pinned tenant image;
4. start the Compose integration environment;
5. test provisioning, health, stop/wake, settings replacement, backup/restore, and entitlement enforcement;
6. publish the control-plane image only after the integration suite passes.

A tenant-image update should be a reviewed dependency change in the SaaS repository, for example an environment default or deployment manifest updated from one digest to another.

## Migration Sequence

### Phase 1: Prepare and Protect

1. Confirm `help-motivate-me-saas` is private and has the intended collaborators.
2. Audit the current SaaS files for secrets before the first push.
3. Do not copy `.env`, local databases, archives, Docker volumes, credentials, API keys, or generated `bin/` and `obj/` directories.
4. Record the currently deployed tenant and control-plane image IDs for rollback.
5. Stop making parallel structural changes to the current `saas/` folder during the move.

The current SaaS work is untracked in the public repository, so copying it into the private repository and making its first commit there avoids publishing private implementation history. If the SaaS code has already been committed publicly by migration time, use `git filter-repo` only if preserving that history is worth carrying it into the private repository.

### Phase 2: Copy and Normalize

1. Copy the current `saas/` content into the new target layout.
2. Move the hosted implementation plans.
3. Rewrite project paths in the solution and Dockerfile.
4. Split Compose into base and local override files.
5. Add SaaS-specific `.gitignore`, dev-container, and workflow files.
6. Update all documentation commands from `saas/...` to repository-root paths.
7. Add `docs/tenant-runtime-contract.md`.

Use `rsync` or normal filesystem copy for the initial transfer, excluding build output and secrets. Review the complete new-repository diff before its first commit.

### Phase 3: Validate the Private Repository

From `help-motivate-me-saas`:

```bash
dotnet test HelpMotivateMe.SaaS.slnx
docker compose -f compose.yml -f compose.local.yml --profile bootstrap build
docker compose -f compose.yml -f compose.local.yml up -d --build --wait
```

Validate:

- `http://saas.localhost:8088` loads;
- existing control-plane metadata remains available;
- existing dynamically managed tenant containers remain discoverable;
- a new Free tenant can be provisioned;
- a Free+AI tenant receives provider/model/key configuration;
- a Paid tenant can back up and restore;
- stop leads to the loading page and automatic wake;
- the idle worker stops inactive tenants;
- the admin page reports correct type, state, login, and uptime values.

Run the same stack once with a published tenant image and without the sibling source override. This proves CI and production do not rely on the local checkout layout.

### Phase 4: Remove SaaS Code from the Public Repository

Only after Phase 3 succeeds:

1. delete `saas/` from `help-motivate-me`;
2. remove `saas/**` build and test entries from the public `.gitignore`;
3. remove or replace private hosted implementation plans;
4. retain public self-hosting documentation and tenant runtime behavior;
5. add a short hosted-service link if desired;
6. verify the root Docker and Compose build remains unchanged;
7. run backend tests, frontend checks, and the root image build.

The password-validation correction in the public `README.md` remains in the public repository because it documents tenant behavior, not SaaS behavior.

### Phase 5: Switch GitHub and Automation

1. Configure branch protection and required tests in both repositories.
2. Add GHCR read permission for the private SaaS workflow.
3. Add only SaaS secrets to the private repository.
4. Configure dependency notifications or `repository_dispatch` without granting the public workflow broad private-repository permissions.
5. Update issue links, project boards, and deployment documentation.
6. Mark migrated public issues without copying confidential discussion into the public repository.

## Data and Runtime Preservation

Moving source files does not require moving Docker data.

Preserve:

- Compose project name `help-motivate-me-saas`;
- network name `help-motivate-me-saas-proxy`;
- control-plane volume `help-motivate-me-saas_control-plane-data`;
- tenant volume names `hmm-data-{tenantId}`;
- tenant container names `hmm-tenant-{tenantId}`;
- existing labels and local host-routing rules.

Do not run `docker compose down -v` during migration. Do not rename the control-plane volume in the first migration. A later explicit data migration can rename resources if needed, with backups and rollback tested separately.

Before switching Compose ownership, create a cold backup of the control-plane volume and record all managed resources:

```bash
docker ps -a --filter label=com.helpmotivateme.managed=true
docker volume ls --filter label=com.helpmotivateme.managed=true
docker volume inspect help-motivate-me-saas_control-plane-data
```

## Rollback

Keep the original checkout unchanged until the private repository passes end-to-end tests.

If the moved stack fails:

1. stop the new Compose process without deleting volumes;
2. return to `help-motivate-me`;
3. start the old `saas/compose.yml` with the same project name;
4. verify control-plane readiness and tenant discovery;
5. diagnose path/image issues before attempting the move again.

Because both Compose definitions use the same explicit project and resource names, never run old and new stacks simultaneously.

## Definition of Done

- `help-motivate-me` contains no hosted control-plane source or private hosted operations documentation.
- `help-motivate-me` remains fully self-hostable and builds without access to the private repository.
- `help-motivate-me-saas` builds and tests without tenant source by pulling a pinned image.
- Local SaaS development can optionally build from `../help-motivate-me`.
- One multi-root VS Code session exposes both Git repositories to an agent.
- Two independent VS Code windows also work without duplicated source.
- CI validates the tenant runtime contract across repositories.
- Existing local control-plane and tenant data survive the move.
- Secrets and local runtime data are absent from both Git histories.
- Documentation clearly states which repository owns each concern.
