# Paid Custom Domains and Managed SSL

## Status

Product and implementation idea for the hosted Paid plan. A customer can keep their normal workspace URL and optionally attach a domain they own, with HTTPS certificates issued and renewed automatically.

Examples:

- Apex domain: `theirdomain.com`
- Customer subdomain: `habits.theirdomain.com`
- Existing platform URL retained as a fallback: `workspace.helpmotivate.me`

“Free SSL” means there is no separate certificate charge to the customer. Certificates can be issued by Let's Encrypt through ACME, but operating certificate issuance, renewal, monitoring, and support still has a platform cost.

## Recommended First Release

Support one exact custom hostname per Paid workspace. Accept both apex domains and subdomains, but recommend a subdomain because its DNS setup is simpler and less likely to conflict with an existing website.

Use Traefik's ACME HTTP-01 challenge to issue a certificate after the hostname points at the platform. This supports both `theirdomain.com` and `habits.theirdomain.com` without asking for access to the customer's DNS account.

Do not support arbitrary customer wildcards such as `*.theirdomain.com` initially. Wildcard certificates require DNS-01 validation and therefore customer DNS credentials, provider-specific integrations, or delegated ACME challenge records. An exact `habits.theirdomain.com` hostname does not require a wildcard certificate.

## Customer Experience

Add a **Custom domain** section to the Paid workspace settings:

1. The customer enters a hostname only, such as `habits.theirdomain.com`. Reject schemes, paths, ports, and wildcards.
2. The control plane creates a domain record and shows a unique ownership-verification TXT value.
3. The UI presents the required DNS records for that hostname.
4. The customer clicks **Verify DNS**, or the platform checks periodically.
5. Once ownership and routing are verified, the platform enables the Traefik route and requests the certificate.
6. The domain moves through visible states such as **Waiting for DNS**, **Issuing certificate**, **Active**, or **Action required**.
7. The original platform URL remains available for recovery and support.

Never promise that DNS changes are instant. The UI should show the records currently observed and the last check time instead of only saying “wait up to 48 hours.”

## DNS Instructions

### Customer Subdomain

For `habits.theirdomain.com`, ask the customer to create:

```dns
habits  CNAME  domains.helpmotivate.me.
_hmm-verification.habits  TXT  "hmm-domain-verification=<random-token>"
```

`domains.helpmotivate.me` is a stable platform hostname that resolves to the production ingress. This allows the platform IP addresses to change without every customer changing DNS.

### Apex Domain

DNS standards do not permit a normal CNAME at an apex. For `theirdomain.com`, offer one of these options:

- Preferred when supported: `ALIAS`, `ANAME`, or provider-specific CNAME flattening to `domains.helpmotivate.me`.
- Portable fallback: platform-provided `A` and, when available, `AAAA` records.

Ownership verification remains a TXT record:

```dns
_hmm-verification  TXT  "hmm-domain-verification=<random-token>"
```

If the customer already has a website on the apex, recommend a subdomain instead. Help Motivate Me cannot share the same hostname with an unrelated website merely through DNS.

### Operational DNS Details

- Publish stable ingress addresses and document changes before replacing them.
- Detect CAA records that prevent Let's Encrypt issuance and show the required correction.
- Warn when a CDN or proxy masks the origin during setup. Cloudflare's proxied mode may work, but certificate validation and redirect behavior must be tested explicitly.
- Follow CNAME chains safely, cap recursion, and compare resolved addresses against the configured public ingress addresses.
- Treat DNS verification as eventually consistent and retry with backoff.

## Domain Ownership and Validation

Require both of the following before activating a hostname:

1. **Ownership proof:** the expected random TXT token resolves at the documented verification name.
2. **Routing proof:** the requested hostname resolves, directly or through a CNAME/ALIAS, to the platform ingress.

Normalize hostnames using an IDNA-aware domain library, store the ASCII/Punycode form for comparisons, and display the Unicode form carefully. Convert to lowercase, remove one trailing dot, and reject IP addresses, localhost names, public-suffix-only names, wildcard labels, URL syntax, and invalid label lengths.

A unique database constraint on the normalized hostname prevents two tenants from claiming the same domain. Tokens must be random, scoped to one domain claim, rotatable, and safe to display.

Keep the TXT ownership check separate from certificate validation. Successful ACME validation proves control at issuance time, but it is not a sufficient application-level ownership workflow and should not be used to decide which tenant receives a hostname.

## Control Plane Data Model

Add a `TenantDomain` entity rather than placing domain lifecycle fields directly on `Tenant`:

- `Id`
- `TenantId`
- `HostnameNormalized`
- `HostnameDisplay`
- `VerificationTokenHash` or a protected token value
- `Status`: `PendingVerification`, `PendingDns`, `Provisioning`, `Active`, `Failed`, `Removing`
- `IsPrimary`
- `VerifiedUtc`
- `ActivatedUtc`
- `LastCheckedUtc`
- `LastErrorCode` and a sanitized message
- `CertificateExpiresUtc`, when available from ingress status
- concurrency token

Create a globally unique index on `HostnameNormalized`. Keep domain operations auditable so support can see who added, verified, activated, or removed a hostname without logging secrets.

The control plane needs endpoints to:

- list a workspace's domains and setup instructions;
- create a domain claim;
- request an immediate verification check with rate limiting;
- choose the primary hostname;
- remove a hostname safely;
- expose certificate and DNS status without exposing internal ACME data.

Paid entitlement must be enforced by these APIs, not only by hiding the settings UI.

## Traefik and Certificate Automation

Production Traefik needs:

- public entry points on ports `80` and `443`;
- an ACME certificate resolver configured for Let's Encrypt HTTP-01;
- HTTP-to-HTTPS redirection that still permits ACME handling;
- durable storage for `acme.json`, restricted to mode `0600`;
- backup and restore of ACME state;
- a staging ACME resolver for automated and pre-production tests;
- certificate-expiry and renewal-failure monitoring.

After the control plane verifies a domain, add it as an exact `Host()` rule on that tenant's router and enable the production certificate resolver. Do not create an ACME-enabled router from an unverified user submission: it would allow certificate-request abuse and consume CA rate limits.

Conceptual labels for a running tenant are:

```text
traefik.http.routers.tenant-{id}.rule=Host(`workspace.helpmotivate.me`) || Host(`habits.theirdomain.com`)
traefik.http.routers.tenant-{id}.entrypoints=websecure
traefik.http.routers.tenant-{id}.tls=true
traefik.http.routers.tenant-{id}.tls.certresolver=letsencrypt
```

The exact mechanism may evolve from Docker labels to Traefik's file or API-backed dynamic configuration. Docker labels require recreating a tenant container whenever a domain changes. A separately generated dynamic configuration is preferable once domain changes become frequent because routing and certificates can then change without replacing tenant workloads.

Use one ACME account per production environment, not one account per tenant. Respect Let's Encrypt duplicate-certificate and failed-validation limits, serialize operations for the same hostname, and use exponential backoff instead of retry loops.

## Avoiding Let's Encrypt Rate Limits

Do not create a Traefik ACME account from each customer's email address. The email is only contact information on the ACME account; it does not give the customer an independent certificate allowance. Several important limits are global across all accounts, and creating many accounts from the platform also encounters the new-account-per-IP limit. Let’s Encrypt recommends that larger hosting providers use one well-protected account, which also makes a future rate-limit override practical.

As documented by Let's Encrypt on August 5, 2026, the production limits most relevant to this service are:

| Limit | Current allowance | Scope and effect |
| --- | ---: | --- |
| New orders | 300 per 3 hours | Per ACME account; capacity refills at one order every 36 seconds |
| New certificates | 50 per 7 days | Per registered domain, globally across all ACME accounts |
| Exact identifier set | 5 per 7 days | Globally across all accounts; repeatedly deleting ACME state commonly triggers this |
| Authorization failures | 5 per identifier per hour | Per identifier and ACME account |
| New accounts | 10 per IP per 3 hours | Makes one-account-per-customer provisioning unsuitable |

These values can change, so verify them against the [official rate-limit documentation](https://letsencrypt.org/docs/rate-limits/) before production launch.

The registered-domain limit behaves differently for the two URL types:

- Exact platform subdomains such as `alice.helpmotivate.me` and `bob.helpmotivate.me` all count against `helpmotivate.me`. Do not issue a separate certificate for every platform subdomain. Use the platform wildcard certificate obtained through DNS-01.
- Customer domains such as `habits.customer-one.com` and `go.customer-two.net` normally count against their respective registered domains, so they do not all consume one shared 50-per-week allowance.
- Multiple customers using hostnames below the same registered domain still share that domain's global allowance, regardless of which ACME account or email requests them.

Use the following issuance controls:

1. Complete TXT ownership and DNS routing preflight checks before adding an ACME-enabled router. Also verify that public port `80` reaches the HTTP-01 entry point.
2. Put new certificate requests through a durable queue with a global rate below the account refill rate, plus per-hostname and per-registered-domain locks.
3. Make each request idempotent. Before ordering, check whether a usable certificate already exists in durable ACME storage and whether an issuance operation is already pending.
4. Persist and back up Traefik's `acme.json`. Never recreate it during deploys, container replacement, scale-to-zero, or ingress failover.
5. Use Let's Encrypt staging for development, automated tests, DNS troubleshooting, and migration rehearsals. Production issuance should happen only after every local preflight passes.
6. Store the failure category and next-attempt time. Honor the ACME `Retry-After` response exactly and add exponential backoff with jitter; do not let UI clicks bypass the cooldown.
7. Monitor order volume, failed authorizations, duplicate issuance, certificate expiry, and queue depth. Alert well before capacity or expiry becomes urgent.
8. Confirm that the pinned Traefik ACME client supports ACME Renewal Information (ARI). ARI-coordinated renewals are exempt from Let's Encrypt rate limits; otherwise preserve the exact identifier set and spread renewals across the renewal window.
9. Request a [hosting-provider rate-limit override](https://letsencrypt.org/docs/rate-limits/#requesting-an-override) several weeks before expected onboarding exceeds the account allowance. A single account makes the request and usage history auditable.

Traefik's resolver email should therefore be an operator-controlled address such as `tls-operations@helpmotivate.me`, not the customer's email. Customer notifications about DNS or certificate problems should come from the control plane using the account email already held by Help Motivate Me.

At larger scale, consider moving ACME orchestration out of individual Traefik instances into a dedicated certificate service or a managed ingress/load balancer. This provides one durable issuance queue and certificate store across all ingress replicas. A second CA can provide outage resilience, but it must not be used to evade a CA's limits, and its own policies and automation require separate testing.

## Custom Domains and Scale-to-Zero

The current wildcard fallback only recognizes platform subdomains. The wake path must also resolve an exact normalized custom hostname to its tenant:

```text
request host -> exact platform/custom-domain lookup -> tenant -> queue start -> refresh until healthy
```

Never infer a Docker resource name or tenant identifier from the untrusted `Host` header. Unknown hosts must receive a neutral `404` response and must not trigger provisioning, certificate issuance, redirects, or container startup.

A stopped tenant still needs a valid ingress route so Traefik can terminate TLS and send the request to the wake service. Routing configuration therefore belongs to the control plane lifecycle, not solely to running tenant containers. This is another reason to move custom-domain routes into a durable Traefik dynamic configuration.

## Application Concerns

Test the tenant application under both its platform URL and custom hostname:

- Authentication cookies should remain host-only. Do not set a platform-wide cookie domain.
- Generate absolute links from a trusted configured primary URL, never directly from an arbitrary `Host` header.
- Add allowed-host validation to prevent host-header injection.
- Ensure OAuth and passwordless login callbacks work. OAuth providers often require pre-registered redirect URIs, so use a fixed control-plane callback followed by a signed handoff to the tenant instead of registering every customer domain with every provider.
- Review CORS, antiforgery, WebSocket forwarding, CSP, and trusted-proxy headers.
- Use the primary custom domain for emails and share links only after it is active; retain the platform URL as a recovery route.
- Service workers and PWA installation are origin-specific. A user who moves domains may need to install the PWA again and will have separate browser storage on the new origin.
- Decide whether the platform hostname redirects to the primary custom domain. Initially, keeping both active without a forced redirect is safer for login and recovery.

## Security and Abuse Controls

- Allow custom domains only for authenticated owners of active Paid workspaces.
- Rate-limit domain creation, DNS checks, and certificate operations by account, tenant, hostname, and source IP.
- Block platform-owned domains, internal service names, and reserved administration hostnames.
- Re-check DNS ownership periodically and before reassigning a hostname, but do not disable a healthy domain because of one transient DNS failure.
- Prevent domain takeover: a removed or expired claim must stop routing before it can be attached to another tenant.
- Sanitize domain values before placing them in Traefik configuration or Docker labels.
- Log issuance and routing changes, while keeping verification tokens and ACME account material out of normal logs.
- Serve only a neutral error for unknown or suspended domains so one tenant cannot impersonate another.

## Billing and Lifecycle Rules

Custom domains are a Paid entitlement. Suggested behavior:

- **Upgrade:** the customer may create and verify a custom domain immediately after paid entitlement is confirmed.
- **Payment failure or cancellation:** keep the domain active through the paid period and a documented grace period.
- **Downgrade:** disable the custom route after the grace period but preserve the claim for a limited recovery window. The platform URL continues to work.
- **Workspace deletion:** remove ingress routing first, then delete the domain claim after the workspace deletion retention period.
- **Domain removal:** stop serving the hostname and remove its dynamic route; ACME certificate material can expire naturally unless the ingress provides safe certificate cleanup.

Communicate these rules before the customer changes DNS so a billing problem does not unexpectedly make their workspace unreachable.

## Monitoring and Support

Track at least:

- domains by lifecycle state;
- DNS verification duration and failures;
- ACME issuance and renewal failures by reason;
- certificates expiring within 30, 14, and 7 days;
- requests to unknown custom hosts;
- domain-triggered wake success and latency;
- Let's Encrypt rate-limit responses.

Provide an operator view showing the observed TXT, CNAME, A/AAAA, CAA, current route, certificate issuer, expiry, and recent sanitized errors. Add a runbook for failed issuance, stale DNS, ingress migration, lost ACME state, and customer domain takeover reports.

## Implementation Phases

### Phase 1: Exact Subdomains

- Add `TenantDomain`, Paid entitlement checks, validation, uniqueness, and domain settings UI.
- Support exact customer subdomains using CNAME.
- Verify TXT ownership and ingress routing.
- Configure Traefik HTTP-01 issuance and persistent ACME storage.
- Route active and stopped tenants by custom hostname.
- Add staging-CA integration tests and production monitoring.

### Phase 2: Apex Domains and Primary URLs

- Support ALIAS/ANAME/flattening plus A/AAAA fallback instructions.
- Add CAA diagnostics.
- Add primary-domain selection, trusted absolute URL generation, and optional canonical redirects.
- Complete fixed OAuth callback and signed tenant handoff behavior.

## Acceptance Criteria

- A Paid customer can attach `habits.theirdomain.com` using a CNAME and TXT record without giving the platform DNS credentials.
- A Paid customer can attach `theirdomain.com` using supported apex DNS records.
- An unverified domain never receives a tenant route or triggers an ACME production request.
- The first HTTPS request serves a publicly trusted certificate, and renewal occurs automatically before expiry.
- HTTP redirects to HTTPS after validation and certificate provisioning.
- The custom hostname wakes a stopped tenant and reaches the correct workspace.
- Unknown, malformed, duplicate, suspended, and cross-tenant hostnames cannot start or reach a tenant.
- Removing or downgrading a domain removes its route while the platform URL remains usable.
- Authentication, passwordless links, OAuth handoff, uploads, API calls, and PWA behavior are tested on a custom hostname.
- Certificate state survives a Traefik restart and a documented recovery from backup.

## Main Risks

- DNS support varies significantly across providers, especially at apex domains.
- A broken certificate renewal can make a customer's workspace appear completely offline.
- ACME rate limits turn validation bugs or aggressive retries into a platform-wide incident.
- Dynamic customer domains complicate OAuth callbacks, cookies, absolute URLs, and scale-to-zero routing.
- Domain reassignment without strict routing teardown can expose the wrong tenant.

These risks make custom domains more than a settings field, but the existing per-tenant Traefik architecture is a good foundation. Exact customer subdomains with TXT verification and HTTP-01 certificates are the smallest production-worthy version.
