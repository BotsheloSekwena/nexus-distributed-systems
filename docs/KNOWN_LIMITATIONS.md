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
- Centralized logging and monitoring
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

The Telemetry Worker currently provides application logs and RabbitMQ exposes queue information through its management tooling.

**Impact:** Centralized metrics, distributed tracing, alerting and a dedicated operational monitoring dashboard have not yet been implemented.

**Future improvement:** Introduce OpenTelemetry instrumentation, Prometheus metrics, Grafana dashboards and centralized log aggregation as part of the planned observability phase.

## 15. Retry Configuration and Recovery

The current messaging implementation uses a configurable retry delay and maximum retry count.

**Impact:** Retry handling follows a fixed-delay strategy rather than an adaptive backoff strategy. Dead-letter recovery also requires manual intervention.

**Future improvement:** Consider exponential backoff, operational alerting and controlled automated recovery procedures where appropriate.

## 16. Review Status

These limitations are documented for transparency and should be reviewed as the NEXUS architecture evolves.

They do not invalidate the functionality demonstrated during the completed implementation phases. However, they identify areas requiring additional engineering before the platform could be considered production-ready.