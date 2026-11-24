# NEXUS — Known Limitations

This document records known architectural and operational limitations identified during the implementation of the NEXUS Distributed Systems Laboratory.

These limitations are documented transparently to distinguish implemented functionality from areas requiring further development before production deployment.

## 1. Authentication Availability

The API contacts the Auth Service for every protected request.

**Impact:** If Auth is unavailable, protected requests fail with HTTP 503.

**Future improvement:** Introduce a carefully designed validation cache or highly available Auth deployment without weakening token revocation.

## 2. Token Revocation Granularity

Token versioning invalidates all previously issued tokens belonging to a user when their role, activation status, or password changes.

**Impact:** Individual sessions cannot currently be revoked independently.

**Future improvement:** Introduce session identifiers and individual token/session revocation.

## 3. Internal API Key Management

The API and Auth Service share an internal validation key.

**Impact:** Rotation currently requires coordinated configuration updates and service recreation.

**Future improvement:** Introduce a dedicated secrets-management solution and documented rotation procedure.

## 4. Rate Limiting

The Auth login rate limiter uses in-memory storage.

**Impact:** Rate-limit state is not shared between multiple Auth replicas and is lost when the process restarts.

**Future improvement:** Use shared storage for distributed rate limiting.

## 5. Reverse Proxy Trust

The Auth Service trusts one reverse-proxy hop.

**Impact:** Changes to the proxy topology may require updating the trusted proxy configuration.

**Future improvement:** Explicitly configure and verify trusted proxy addresses and deployment topology.

## 6. Docker Socket Permissions

Traefik accesses the Docker socket through a read-only volume mount.

**Impact:** A read-only mount does not guarantee that Docker API operations are read-only.

**Future improvement:** Consider a restricted Docker socket proxy or another provider configuration that limits Docker API access.

## 7. ASP.NET Core DataProtection

DataProtection keys are currently stored inside the API container and are not persisted or encrypted through a configured XML encryptor.

**Impact:** Container recreation may affect protected data that depends on those keys.

**Future improvement:** Persist DataProtection keys in a protected shared location and configure appropriate encryption at rest.

## 8. Production Deployment Hardening

The current environment is a development and learning deployment.

Before production deployment, additional work is required around:

- HTTPS and certificate management
- Secrets rotation
- Alerting, on-call notification and long-term telemetry storage
- Backup and recovery procedures
- Database access permissions
- Distributed rate limiting
- High availability and service recovery

## 9. Transactional Message Publishing

The API publishes telemetry events directly to RabbitMQ without using a transactional outbox.

**Impact:** A successful database operation and successful message publication cannot currently be guaranteed as one atomic transaction. If future functionality requires both operations, a failure between them could create inconsistent application state.

**Future improvement:** Introduce a transactional outbox pattern if telemetry publishing becomes dependent on database transactions or requires stronger delivery guarantees.

## 10. Dead-Letter Message Management

Failed telemetry messages are transferred to the RabbitMQ dead-letter queue after exhausting their retry attempts.

**Impact:** The current implementation does not include a dedicated administrative interface or automated workflow for inspecting, correcting and replaying dead-letter messages.

**Future improvement:** Introduce dead-letter monitoring, message inspection and controlled replay procedures.

## 11. At-Least-Once Message Processing

The Telemetry Worker acknowledges messages after successful persistence and uses EventId-based duplicate protection.

**Impact:** Message delivery may occur more than once, particularly when a consumer or connection fails around acknowledgement processing. The current design protects database insertion against duplicate event identifiers, but does not guarantee exactly-once processing across every possible external side effect.

**Future improvement:** Extend idempotency controls to any future external processing operations and document delivery guarantees throughout the messaging pipeline.

## 12. Event Schema Evolution

The shared telemetry contract currently uses schema version 1.

**Impact:** A formal compatibility and migration strategy has not yet been implemented for future event contract changes.

**Future improvement:** Introduce explicit schema compatibility rules, version migration handling and automated contract tests before introducing breaking event changes.

## 13. Messaging Integration Test Automation

Phase 7 messaging functionality was verified through manual integration testing using PowerShell, Docker commands, worker logs and PostgreSQL queries.

**Impact:** These tests demonstrate successful runtime behaviour, but the complete messaging pipeline does not yet have an automated integration test suite covering publishing, consumption, retries, duplicate delivery and dead-letter handling.

**Future improvement:** Introduce automated integration tests using isolated RabbitMQ and PostgreSQL test environments.

## 14. Observability and Messaging Monitoring

Phase 8 introduced centralised metrics (Prometheus), distributed tracing (Tempo), log aggregation (Loki) and a provisioned Grafana dashboard. These were verified at runtime.

**Remaining gaps:**

- No alerting rules or notification channels are configured. Failures are visible on the dashboard but are not actively reported.
- Grafana data sources are not configured for direct navigation between a trace and its related logs.
- Observability data is stored on local Docker volumes with fixed retention (Prometheus 15 days; Loki and Tempo 168 hours) and no backup.

**Future improvement:** Add Prometheus or Grafana alert rules for dead-lettering, consumer absence and scrape failures. Configure trace-to-logs correlation. Define a storage and retention strategy appropriate to the deployment.

## 15. Retry Configuration and Recovery

The current messaging implementation uses a configurable retry delay and maximum retry count.

**Impact:** Retry handling follows a fixed-delay strategy rather than an adaptive backoff strategy. Dead-letter recovery also requires manual intervention.

**Future improvement:** Consider exponential backoff, operational alerting and controlled automated recovery procedures where appropriate.

## 16. Auth Service Database Resilience

The Auth Service uses a single long-lived `pg.Client` (`src/Auth/db.js`) with no `error` handler or reconnection. No Docker Compose service defines a restart policy.

**Impact:** Identified during Phase 8 failure testing. When PostgreSQL restarted, the Auth Service process terminated on an unhandled connection error (`57P01`) and remained stopped. All protected API requests then failed with HTTP 503 until Auth was started manually. Even with an error handler, the single connection would not recover on its own.

**Future improvement:** Replace the single client with a `pg.Pool`, handle pool errors, and define restart policies (for example `restart: unless-stopped`) for long-running services.

## 17. Auth Service Log Export

The Auth Service exports traces and metrics through OpenTelemetry, but its logs are written with `console` and are not exported.

**Impact:** Auth Service logs are available only through `docker compose logs` and are absent from Loki.

**Future improvement:** Adopt a structured logging library with an OpenTelemetry log bridge (for example Pino or Winston) and configure an OTLP log exporter.

## 18. Custom Metric Presentation

Several characteristics of the Telemetry Worker metrics affect how they are presented:

- Counters are created only on their first measurement, already holding the value 1. Prometheus' `increase()` cannot observe this first increment, so the "Telemetry Messages Processed" panel undercounts by one after each worker restart.
- Failure-handling panels show totals since the worker started, which reset when the worker restarts.
- Labels are inconsistent: `processed`, `failed` and `duplicates` use `category`, `retried` uses `routing_key`, and `dead_lettered` has no label.
- The processing-duration histogram uses the SDK's default bucket boundaries (for example 100 ms to 250 ms), so the p95 is an interpolated estimate and is noisy at low volume.
- The Auth Service sets `service.namespace`, so its metrics appear as `exported_job="nexus/nexus-auth"`, while the .NET services have no namespace prefix.

**Future improvement:** Initialise counters for known label sets at start-up, standardise labels, configure explicit histogram buckets for expected latencies, and use consistent service naming.

## 19. Telemetry Worker Health Check

The Telemetry Worker has no Docker health check. Its state is visible through logs, RabbitMQ consumer counts and the dashboard, but not through `docker compose ps`.

**Future improvement:** Add a health check reflecting broker connectivity and consumer state.

## 20. Dashboard Provisioning on Docker Desktop

Grafana reloads provisioned dashboards from a bind-mounted directory. On Docker Desktop for Windows, changes made from the host were not picked up automatically during Phase 8.

**Impact:** Dashboard changes require `docker compose restart grafana` to take effect in this environment.

## 21. Review Status

These limitations are documented for transparency and should be reviewed as the NEXUS architecture evolves.

They do not invalidate the functionality demonstrated during the completed implementation phases. However, they identify areas requiring additional engineering before the platform could be considered production-ready.