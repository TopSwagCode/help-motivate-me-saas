#!/usr/bin/env bash

set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
base_url="${HMM_SAAS_URL:-http://127.0.0.1:8088}"
host_header="${HMM_SAAS_HOST:-saas.localhost}"
tenants_file="$(mktemp)"
trap 'rm -f "$tenants_file"' EXIT

for command in docker curl jq; do
    if ! command -v "$command" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "$command" >&2
        exit 1
    fi
done

compose=(docker compose -f "$repo_dir/compose.yml" -f "$repo_dir/compose.local.yml")

printf 'Building the local tenant image...\n'
"${compose[@]}" --profile bootstrap build tenant-image

curl --fail --silent --show-error \
    -H "Host: $host_header" \
    "$base_url/api/tenants/" > "$tenants_file"

tenant_count="$(jq 'length' "$tenants_file")"
if [[ "$tenant_count" -eq 0 ]]; then
    printf 'No Habit Spaces found.\n'
    exit 0
fi

printf 'Updating %s Habit Space(s)...\n' "$tenant_count"
failures=0

while IFS= read -r tenant; do
    id="$(jq -r '.id' <<< "$tenant")"
    subdomain="$(jq -r '.subdomain' <<< "$tenant")"
    previous_status="$(jq -r '.status' <<< "$tenant")"
    payload="$(jq -n \
        --arg app_name "$(jq -r '.appName' <<< "$tenant")" \
        --arg username "$(jq -r '.username' <<< "$tenant")" \
        --argjson enable_ai "$(jq '.aiMode != "Disabled"' <<< "$tenant")" \
        '{appName: $app_name, username: $username, enableAi: $enable_ai}')"

    printf '  %s: replacing container... ' "$subdomain"
    if ! curl --fail --silent --show-error \
        -X PUT \
        -H "Host: $host_header" \
        -H 'Content-Type: application/json' \
        --data "$payload" \
        "$base_url/api/tenants/$id/settings" >/dev/null; then
        printf 'failed\n' >&2
        failures=$((failures + 1))
        continue
    fi

    if [[ "$previous_status" == "Stopped" ]]; then
        if ! curl --fail --silent --show-error \
            -X POST \
            -H "Host: $host_header" \
            "$base_url/api/tenants/$id/stop" >/dev/null; then
            printf 'updated, but failed to restore stopped state\n' >&2
            failures=$((failures + 1))
            continue
        fi
    fi

    printf 'done\n'
done < <(jq -c '.[]' "$tenants_file")

if [[ "$failures" -gt 0 ]]; then
    printf '%s Habit Space update(s) failed.\n' "$failures" >&2
    exit 1
fi

printf 'All %s Habit Space(s) now use the rebuilt image.\n' "$tenant_count"