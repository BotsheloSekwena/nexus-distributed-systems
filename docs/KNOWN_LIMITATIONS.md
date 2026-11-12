# NEXUS — Known Limitations

This document records known architectural and operational limitations identified during implementation.

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

## 9. Review Status

These limitations are documented for transparency and should be reviewed as the NEXUS architecture evolves.