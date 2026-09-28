# Help Motivate Me SaaS

Private hosted control plane for Help Motivate Me. This repository provisions one public tenant image and one Docker volume per tenant. It is not required to build or run the self-hosted application.

## Start

To use a published tenant image:

```bash
export HMM_TENANT_IMAGE=ghcr.io/topswagcode/help-motivate-me:sha-<commit>
docker compose --profile bootstrap pull
docker compose up -d --build --wait
```

For local cross-repository development with `../help-motivate-me`:

```bash
docker compose -f compose.yml -f compose.local.yml --profile bootstrap build
docker compose -f compose.yml -f compose.local.yml up -d --build --wait
```

Open <http://saas.localhost:8088>. No DNS or hosts-file changes are normally needed: browsers and operating systems resolve `*.localhost` to the loopback interface.

The first signup choice links to the free, fully self-hosted application, where operators provide their own AI key and manage their own backups. Free and Paid-preview hosted choices provision a local Habit Space and redirect to its management page, where you can open, start, stop, restart, and configure the instance. The chosen Habit Space name becomes the installed PWA name. Each tenant generates its own push-notification keypair inside its persistent volume. Use the credentials entered in the create form to sign in.

The local pages are:

- Signup: <http://saas.localhost:8088/>
- Administration: <http://saas.localhost:8088/admin>
- Habit Space management: `http://saas.localhost:8088/workspace/{tenant-id}`

Set `SAAS_MANAGED_AI_API_KEY` before starting Compose to test supplied AI on a Paid-preview workspace. Without it, Paid workspaces can still be created with AI disabled.

If a browser or corporate DNS setup does not resolve subdomains of `localhost`, use curl with an explicit host header for testing:

```bash
curl -H 'Host: saas.localhost' http://127.0.0.1:8088/
curl -H 'Host: demo.localhost' http://127.0.0.1:8088/health/ready
```

## Test Wake-Up

Stop a Habit Space from the dashboard, then revisit its tenant URL. Traefik sends the request to the fallback route, which queues the container start and returns a page that refreshes until the exact tenant route becomes healthy.

Useful commands:

```bash
docker compose ps
docker compose logs -f control-plane traefik
docker ps --filter label=com.helpmotivateme.managed=true
dotnet test HelpMotivateMe.SaaS.slnx
```

See [docs/testing-scale-to-zero.md](docs/testing-scale-to-zero.md) for the full one-minute idle shutdown and automatic-wake procedure. The tenant image integration boundary is documented in [docs/tenant-runtime-contract.md](docs/tenant-runtime-contract.md). Use [docs/cross-repository-development.md](docs/cross-repository-development.md) when a feature changes both repositories, and [docs/production-rollout-runbook.md](docs/production-rollout-runbook.md) to prepare safe staged releases and rollbacks before production. Admin and hosted pages must follow [docs/admin-design-guide.md](docs/admin-design-guide.md).

## Stop and Reset

Stopping Compose retains the control-plane database and all tenant data:

```bash
docker compose down
```

A complete local reset removes the control-plane database plus dynamically managed tenant containers and volumes:

```bash
docker compose down -v
docker rm -f $(docker ps -aq --filter label=com.helpmotivateme.managed=true)
docker volume rm $(docker volume ls -q --filter label=com.helpmotivateme.managed=true)
```

The reset commands report an error when there are no matching resources; that is harmless.

## Current Scope

Included:

- local `*.localhost` routing through Traefik
- isolated container and persistent volume per tenant
- plan-aware signup plus Habit Space and administration views
- tenant creation, start, stop, restart, settings replacement, health wait, and automatic wake-up
- activity-aware idle shutdown after 3 minutes in the local Compose stack
- persistent control-plane metadata
- Free Habit Spaces with optional bring-your-own OpenAI configuration
- Paid-preview Habit Spaces with optional operator-supplied AI
- local paid-only managed backup history and restore
- workspace counts, type, state, last login, and estimated uptime
- a narrow runtime abstraction ready for future entitlement and payment work

Not production-ready yet:

- authentication and account ownership
- restricted Docker API proxy
- PostgreSQL and distributed lifecycle locks
- encrypted object-storage backups and retention policies
- TLS and production wildcard DNS
- billing and paid entitlements

Do not expose this local stack to the internet. The signup, management, and administration pages have no authentication, and the control-plane container runs as root locally so it can use the mounted Docker socket without host-specific group configuration.

The local Compose defaults are `SAAS_IDLE_TIMEOUT_MINUTES=3` and `SAAS_IDLE_SWEEP_SECONDS=10`. Override either environment variable before starting the stack; for a faster experiment, use one minute and five seconds. Tenant requests pass through a throttled Traefik activity hook, so normal browsing postpones shutdown. The application-level defaults remain 30 minutes and 60 seconds for deployments that do not use this Compose file.