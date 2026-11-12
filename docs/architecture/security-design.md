# NEXUS — Authentication and Security Architecture

**Phase:** 6 — Authentication and Security  
**Status:** Implemented and tested

## 1. Objective

Phase 6 strengthens NEXUS authentication and introduces token revocation, role-based authorization, and additional HTTP security controls.

The objective is to ensure that authenticated requests are validated against the current state of the user account rather than relying exclusively on the JWT's original contents.

## 2. Authentication Architecture

NEXUS uses a dedicated Node.js Authentication Service backed by PostgreSQL.

The authentication flow is:

1. A client submits registration or login credentials through Traefik.
2. The Auth Service validates the input.
3. Passwords are stored using bcrypt hashing.
4. Successful authentication produces a signed JWT.
5. The API validates the JWT signature, issuer, audience, and expiration.
6. The API contacts the internal Auth validation endpoint to verify the token against the current database state.
7. Access is granted only when both JWT and account-state validation succeed.

## 3. JWT Validation

The API validates:

- JWT signature
- Issuer (`nexus-auth`)
- Audience (`nexus-api`)
- Token expiration
- User identifier
- User role
- Token version

Inbound claim mapping is disabled to preserve the expected claim names.

The API uses the `role` claim for role-based authorization.

## 4. Token Versioning and Revocation

Each user has a `token_version` database field.

New JWTs include the user's current token version.

A PostgreSQL trigger increments the version when any of these fields change:

- Role
- Account activation status
- Password hash

The Auth Service compares the token version against the current database value.

If the values differ, the token is rejected.

This means previously issued tokens become invalid after a relevant account change.

## 5. Internal Token Validation

The Auth Service exposes:

`POST /internal/tokens/validate`

The endpoint checks:

- Internal API key
- User identifier
- Token version
- Role
- Current account activation status

The API communicates with this endpoint using an internal shared API key.

The endpoint is not exposed through a Traefik router. It is intended for service-to-service communication over the Docker network.

The API fails closed when validation is unavailable, returning HTTP 503 rather than accepting an unverified token.

## 6. Role-Based Access Control

The database enforces the following allowed roles:

- `User`
- `Administrator`

Registration always assigns the `User` role. Client-supplied roles cannot be used to escalate privileges.

The API demonstrates authorization through:

| Endpoint | Required role |
|---|---|
| `/api/secure/user` | User or Administrator |
| `/api/secure/admin` | Administrator |

An authenticated user without the required role receives HTTP 403.

Invalid or revoked tokens receive HTTP 401.

## 7. Authentication Service Hardening

The Auth Service includes:

- bcrypt password hashing with 12 rounds
- Dummy bcrypt comparison for unknown or inactive users
- Generic login failure responses
- Input validation
- A 10 KB JSON request limit
- Helmet security headers
- Login rate limiting: five attempts per 15 minutes
- Sanitized error responses
- Constant-time comparison of internal API keys

The service also requires the internal API key to be configured before startup.

## 8. Network Security

The existing Phase 4 network segmentation remains in place.

- API and Auth are connected to the application and database networks.
- PostgreSQL is restricted to the database network.
- PostgreSQL has no published host port.
- The internal token validation endpoint is not routed through Traefik.

This provides an additional infrastructure-level security boundary.

## 9. Verification Results

The following tests were completed:

| Test | Result |
|---|---|
| User registration | HTTP 201 |
| Duplicate registration | HTTP 409 |
| Invalid registration | HTTP 400 |
| Oversized JSON body | HTTP 413 |
| Malformed JSON | HTTP 400 |
| Login rate limit | HTTP 429 after five attempts |
| Valid User authentication | HTTP 200 |
| Administrator authorization | HTTP 200 |
| Insufficient role | HTTP 403 |
| Role-change token revocation | HTTP 401 |
| Account deactivation revocation | HTTP 401 |
| Tampered JWT | HTTP 401 |
| Auth service unavailable | HTTP 503 |
| Auth service recovery | HTTP 200 |

The API and Auth database health endpoints also returned healthy responses.

## 10. Security Considerations

The implementation provides several complementary controls, but it is not a complete production security solution.

Important considerations include:

- Every protected API request depends on Auth availability.
- Token versioning revokes all existing tokens for an affected user rather than individual sessions.
- The internal API key requires secure storage and controlled rotation.
- The current login rate limiter uses in-memory storage.
- Proxy trust is configured for one Traefik hop.
- The Docker socket's read-only volume mount does not guarantee read-only Docker API permissions.
- ASP.NET Core DataProtection keys are not yet persisted or encrypted.

Further operational hardening is required before production deployment.

## 11. Outcome

Phase 6 successfully introduced JWT validation, database-backed token revocation, role-based authorization, account-state validation, and additional Auth Service security controls.

The implementation was verified through the Traefik gateway and the running Docker environment.
