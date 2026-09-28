# Testing Free-Tier Scale to Zero

This procedure shortens the local idle timeout to one minute, verifies automatic shutdown, and then verifies the loading-page wake flow. Run commands from the repository root.

## 1. Start the stack with a short timeout

```bash
export SAAS_IDLE_TIMEOUT_MINUTES=1
export SAAS_IDLE_SWEEP_SECONDS=5
docker compose -f compose.yml -f compose.local.yml --profile bootstrap build
docker compose -f compose.yml -f compose.local.yml up -d --build --wait
```

Open <http://saas.localhost:8088>, select **Free**, leave AI disabled, and create a workspace. Keep its tenant URL and management-page URL open.

## 2. Verify the running workspace

Open the tenant URL and sign in. Confirm that the management page and <http://saas.localhost:8088/admin> show the workspace as `Running`.

You can also inspect managed containers directly:

```bash
docker ps --filter label=com.helpmotivateme.managed=true
```

## 3. Verify manual stop and wake

1. Select **Stop** on the workspace management page.
2. Refresh the management page and confirm the state is `Stopped`.
3. Open the tenant root URL, not `/health/ready`.
4. Confirm the **Starting workspace** page appears.
5. Leave that page open. It refreshes every two seconds and switches to the tenant when Docker reports it healthy.

The exact tenant route disappears while the container is stopped, so Traefik's lower-priority fallback serves the loading page and queues the start.

## 4. Verify automatic idle shutdown

After the workspace wakes, close its tenant tabs and do not request its hostname for at least 70 seconds. Requests to the management and admin pages do not count as tenant activity.

Refresh the management page or admin page and confirm the state changed to `Stopped`. The sweep runs every five seconds, so shutdown may occur a few seconds after the one-minute cutoff.

Open the tenant root URL again and confirm the loading page wakes it a second time.

## 5. Inspect activity and lifecycle logs

```bash
docker compose -f compose.yml -f compose.local.yml logs -f control-plane traefik
```

Useful non-browser checks:

```bash
curl -H 'Host: saas.localhost' http://127.0.0.1:8088/api/tenants
curl -i -H 'Host: YOUR-SUBDOMAIN.localhost' http://127.0.0.1:8088/
docker ps -a --filter label=com.helpmotivateme.managed=true
```

Replace `YOUR-SUBDOMAIN` with the workspace name. Use `/` for wake testing because `/health/ready` is also a control-plane health route.

## 6. Restore local defaults

```bash
unset SAAS_IDLE_TIMEOUT_MINUTES
unset SAAS_IDLE_SWEEP_SECONDS
docker compose -f compose.yml -f compose.local.yml up -d --build --wait
```

The local Compose default idle timeout is 3 minutes with a 10-second sweep. The application-level fallback is 30 minutes with a 60-second sweep.