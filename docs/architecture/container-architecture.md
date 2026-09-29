# NEXUS Container Architecture

## Overview

Phase 3 containerizes the core NEXUS platform using Docker and Docker Compose.

The platform consists of three primary containers:

- ASP.NET Core API
- Node.js Authentication Service
- PostgreSQL Database

Docker Compose provides service orchestration, internal networking, health checks, startup dependencies, environment configuration, and persistent database storage.

---

## Container Architecture

The NEXUS container architecture consists of three services:

- `nexus-api`
- `nexus-auth`
- `nexus-postgres`

The services communicate through the Docker Compose bridge network.

---

## Containers

### API

The API is an ASP.NET Core application targeting .NET 10.

| Property | Value |
|---|---|
| Container | `nexus-api` |
| Image | `nexus-distributed-systems-api` |
| Container port | `8080` |
| Host port | `5160` |
| Database | `nexus` |
| Health endpoint | `/health` |
| Database health endpoint | `/health/database` |

The API connects to PostgreSQL using the Docker Compose service name `postgres`.

### Authentication Service

The authentication service is a Node.js and Express application.

| Property | Value |
|---|---|
| Container | `nexus-auth` |
| Image | `nexus-distributed-systems-auth` |
| Container port | `3000` |
| Host port | `3000` |
| Database | `nexus_auth` |
| Health endpoint | `/health` |
| Database health endpoint | `/health/database` |

The authentication service connects to PostgreSQL using the Docker Compose service name `postgres`.

### PostgreSQL

PostgreSQL provides persistent relational storage for the platform.

| Property | Value |
|---|---|
| Container | `nexus-postgres` |
| Image | `postgres:18` |
| Container port | `5432` |
| Host port | `5432` |
| Persistent volume | `nexus-postgres-data` |
| Database 1 | `nexus` |
| Database 2 | `nexus_auth` |

The databases remain logically separated while sharing the same PostgreSQL server.

---

## Docker Networking

Docker Compose creates the network `nexus-distributed-systems_default`.

The network uses the Docker `bridge` driver.

### Network Configuration

| Property | Value |
|---|---|
| Network | `nexus-distributed-systems_default` |
| Driver | `bridge` |
| Subnet | `172.18.0.0/16` |
| Gateway | `172.18.0.1` |

### Container Network Addresses

| Container | Internal IP |
|---|---|
| `nexus-postgres` | `172.18.0.2` |
| `nexus-api` | `172.18.0.3` |
| `nexus-auth` | `172.18.0.4` |

Container IP addresses are dynamically assigned and should not be treated as permanent identifiers.

---

## Service Discovery

Applications communicate with PostgreSQL using `postgres:5432`.

The hostname `postgres` is the Docker Compose service name.

Docker's internal DNS resolves the service name to the PostgreSQL container.

This means the applications do not need to know PostgreSQL's dynamically assigned IP address.

### API Connection

The API connects using `Host=postgres`, `Port=5432`, and `Database=nexus`.

### Authentication Connection

The authentication service uses `POSTGRES_HOST=postgres`, `POSTGRES_PORT=5432`, and `POSTGRES_DB=nexus_auth`.

This demonstrates service-name-based container-to-container communication.

---

## Host Port Mappings

| Host Port | Container Port | Service |
|---|---|---|
| `5160` | `8080` | ASP.NET Core API |
| `3000` | `3000` | Authentication Service |
| `5432` | `5432` | PostgreSQL |

### Host Access

The services can be accessed from the host using `http://localhost:5160`, `http://localhost:3000`, and `localhost:5432`.

### Internal Container Access

Inside the Docker network, services communicate using Docker service names and container ports.

For example: `postgres:5432`.

---

## Persistent Storage

PostgreSQL uses the named Docker volume `nexus-postgres-data`.

The volume is mounted into the PostgreSQL container at `/var/lib/postgresql`.

### Volume Configuration

| Property | Value |
|---|---|
| Volume | `nexus-postgres-data` |
| Type | `volume` |
| Driver | `local` |
| Mount destination | `/var/lib/postgresql` |
| Read/write | `true` |

The volume is declared as external in Docker Compose.

This ensures that Docker Compose uses the existing persistent volume instead of creating a project-prefixed replacement volume.

---

## Container Lifecycle vs Data Lifecycle

The PostgreSQL container is disposable, while the database data is stored independently in the named volume.

The following lifecycle was tested:

1. `docker compose down`
2. Containers were destroyed.
3. The Docker network was removed.
4. `docker compose up -d`
5. New containers were created.
6. Existing PostgreSQL data was recovered.

After recreating the complete Compose stack, the databases `nexus` and `nexus_auth` remained available.

This verifies that PostgreSQL data survives container recreation.

---

## Health Checks and Startup Dependencies

PostgreSQL has a Docker health check using `pg_isready -U nexus -d nexus`.

The health check verifies that PostgreSQL is ready to accept connections.

The API and Authentication containers depend on PostgreSQL reaching a healthy state before they start.

Docker Compose uses `depends_on` with `condition: service_healthy`.

This prevents the application services from attempting database connections before PostgreSQL is ready.

### Verified Startup Behaviour

| Container | Status |
|---|---|
| `nexus-postgres` | Up (healthy) |
| `nexus-api` | Up |
| `nexus-auth` | Up |

---

## Environment Configuration

Environment-specific configuration is supplied through the root `.env` file.

The configuration includes PostgreSQL settings and JWT settings.

Sensitive values such as the PostgreSQL password and JWT secret are not committed to Git.

The repository contains `.env.example` as a template for the required environment variables.

Docker Compose resolves the environment variables when the stack starts.

### Authentication Environment

The authentication service receives `POSTGRES_HOST=postgres`, `POSTGRES_PORT=5432`, `POSTGRES_DB=nexus_auth`, `POSTGRES_USER=nexus`, and `JWT_EXPIRES_IN=1h`.

### API Environment

The API receives its PostgreSQL connection string through `ConnectionStrings__NexusDatabase`.

The connection string points to `Host=postgres`, `Port=5432`, and `Database=nexus`.

---

## Database Separation

The PostgreSQL server currently hosts two application databases:

- `nexus`
- `nexus_auth`

The ASP.NET Core API owns the `nexus` database.

The Node.js Authentication Service owns the `nexus_auth` database.

This provides logical database ownership while the applications currently share one PostgreSQL server.

Network-level database isolation will be addressed in a later NEXUS phase.

---

## Verification

The containerized platform was verified using service health checks, database health checks, Docker network inspection, volume inspection, and container recreation.

### API Service

`GET http://localhost:5160/health` returned `healthy / nexus-api`.

### API Database

`GET http://localhost:5160/health/database` returned `healthy / nexus`.

### Authentication Service

`GET http://localhost:3000/health` returned `healthy / nexus-auth`.

### Authentication Database

`GET http://localhost:3000/health/database` returned `healthy / nexus_auth`.

### PostgreSQL Databases

The PostgreSQL container contains `nexus` and `nexus_auth` in addition to the standard PostgreSQL databases.

### Docker Network

All three NEXUS containers were verified on `nexus-distributed-systems_default`.

### Persistent Volume

The PostgreSQL container was verified to mount `nexus-postgres-data` at `/var/lib/postgresql`.

### Persistence Test

The Compose stack was stopped with `docker compose down` and recreated with `docker compose up -d`.

After recreation, the PostgreSQL databases were still present.

This confirms that database data persisted independently of the PostgreSQL container lifecycle.

---

## Phase 3 Outcome

Phase 3 establishes the first containerized version of the NEXUS platform.

The system now demonstrates:

- Containerized ASP.NET Core API
- Containerized Node.js Authentication Service
- Containerized PostgreSQL
- Docker Compose orchestration
- Docker bridge networking
- Container-to-container service discovery
- PostgreSQL health checks
- Startup dependency management
- Environment-based configuration
- Named persistent storage
- Database persistence across container recreation
- Logical database separation

The next architectural phase introduces stronger network segmentation and security boundaries between application and database workloads.
