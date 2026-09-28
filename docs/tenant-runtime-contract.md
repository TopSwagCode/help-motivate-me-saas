# Tenant Runtime Contract

The hosted control plane consumes the public Help Motivate Me application as a container image. It does not reference or compile tenant source.

## Runtime

- The image listens for HTTP traffic on port `8080`.
- `GET /health/live` reports process health.
- `GET /health/ready` reports application and SQLite readiness.
- All mutable state is stored below `/data`.
- The SQLite database, uploads, and data-protection keys survive container replacement through the `/data` volume.
- Tenant containers do not receive Docker-socket access.
- The control plane must stop a tenant gracefully before backup or restore reads or replaces its volume.

## Configuration

The control plane may supply these environment variables:

- `SingleUser__Username`
- `SingleUser__Password`
- `SingleUser__DisplayName`
- `AI__Provider`
- `AI__ApiKey`
- `AI__BaseUrl`
- `AI__ChatModel`
- `AI__EnableTranscription`
- `AI__TranscriptionModel`

Secrets must not be logged or committed. Production deployments must set `HMM_TENANT_IMAGE` to a reviewed immutable tag or digest. The `main` tag is a development fallback only.

## Compatibility

Changes to ports, health endpoints, persistence paths, configuration names, shutdown behavior, or image startup semantics must remain backward compatible or introduce a versioned contract. The public application CI should smoke-test these requirements; SaaS CI should test provisioning and lifecycle behavior against its pinned image.