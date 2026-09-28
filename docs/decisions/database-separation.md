# Architecture Decision: Database Separation

## Status

Accepted

## Date

**2026**-09-28

---

## Context

**NEXUS** contains multiple backend services with different responsibilities.

The **ASP**.**NET** Core **API** manages application-level functionality, while the Node.js Authentication Service manages user identity and authentication.

A decision was required regarding whether both services should share a single database schema or use separate databases.

The goal is to establish clear service ownership while keeping the development environment manageable during the early stages of the project.

---

## Decision

**NEXUS** will use separate PostgreSQL databases for the **ASP**.**NET** Core **API** and the Node.js Authentication Service.

The current configuration is:

```text
PostgreSQL Server
│
├── nexus
│   └── **ASP**.**NET** Core application data
│
└── nexus_auth
    └── Authentication and identity data

Both databases currently run on the same PostgreSQL server instance during local development.

### Database Ownership

Each service owns its respective database.

**ASP**.**NET** Core **API**
    |
    +---- owns ----> nexus

Node.js Auth Service
    |
    +---- owns ----> nexus_auth

The **ASP**.**NET** Core **API** is responsible for the nexus database schema.

The Node.js Authentication Service is responsible for the nexus_auth database schema.

Neither service directly accesses the other service's database tables.

Rationale

Separating the databases provides a clearer ownership boundary between services.

The **ASP**.**NET** Core **API** owns application data, while the Authentication Service owns identity-related data.

This reduces direct coupling between the services at the database layer.

The separation also establishes a foundation for future infrastructure changes where services and databases can be placed into separate containers, networks, or deployment environments.

Advantages ### Clear Data Ownership

Each service has a defined database that it is responsible for managing.

**API** Service
    |
    +----> nexus

### Auth Service

    |
    +----> nexus_auth

This makes it easier to determine which service is responsible for a given piece of data.

### Reduced Database Coupling

The services do not depend on shared tables.

A change to the Authentication Service's database schema does not require the **ASP**.**NET** Core **API** to reference those tables directly.

### Service Independence

The Authentication Service can evolve independently from the main application service.

Future versions could potentially deploy, scale, or migrate the authentication infrastructure separately.

### Future Network Segmentation

The database separation provides a useful foundation for future network architecture.

During later phases, the services and databases can be placed into separate Docker networks with controlled communication paths.

Trade-offs

Database separation introduces additional operational complexity.

Multiple Schemas to Maintain

Each database requires its own migration and schema management process.

The **ASP**.**NET** Core **API** uses Entity Framework Core migrations for the nexus database.

The Authentication Service currently uses **SQL** migration scripts for the nexus_auth database.

### Increased Configuration

Each service requires its own database connection configuration.

Developers must therefore configure both databases when setting up the complete **NEXUS** development environment.

Cross-Service Data Access

A service cannot directly query another service's database.

If information owned by another service is required, the preferred approach is service-to-service communication.

For example:

**ASP**.**NET** Core **API**
    |
    | HTTP / internal service communication
    v
Node.js Auth Service
    |
    v
nexus_auth

This introduces additional communication and **API** design requirements.

### Alternatives Considered

Alternative 1: Shared Database

Both services could use a single PostgreSQL database containing tables for application data and authentication data.

Example:

PostgreSQL Server
│
└── nexus
    ├── application tables
    └── authentication tables
Advantages
Simpler initial setup
One database connection infrastructure
Easier direct queries between related tables
Fewer database migrations to manage
Disadvantages
Stronger coupling between services
Less explicit ownership of data
Services could become dependent on each other's tables
More difficult to establish independent service boundaries later

This approach was not selected for **NEXUS**.

Alternative 2: Separate PostgreSQL Server Instances

Each service could run against a completely independent PostgreSQL server.

Example:

**ASP**.**NET** Core **API**
    |
    v
PostgreSQL Server 1
    |
    nexus

Node.js Auth Service
    |
    v
PostgreSQL Server 2
    |
    nexus_auth
Advantages
Stronger infrastructure isolation
Independent database server configuration
Clearer infrastructure boundaries
Easier future independent deployment
Disadvantages
Higher resource usage
More infrastructure to operate
More configuration for local development
Unnecessary complexity at the current development stage

This approach was not selected for the current phase.

### Current Implementation

The current development environment uses:

PostgreSQL Server
    |
    +-----------------------+
    |                       |
    v                       v
    nexus                  nexus_auth
    |                       |
    v                       v
 **ASP**.**NET** Core **API**        Node.js Auth Service

The PostgreSQL server is currently running locally through Docker.

The databases are therefore logically separated while sharing the same PostgreSQL server instance.

### Security Considerations

Database separation should not be considered complete network isolation.

During Phase 2, the separation is primarily a logical data ownership boundary.

The current architecture does not yet provide:

Docker network isolation Firewall rules between services Private database networks Reverse proxy enforcement Production-grade secret management **TLS** between internal services

These controls will be addressed during later infrastructure and networking phases.

### Future Evolution

As **NEXUS** moves toward containerized infrastructure, the database architecture can evolve into explicit network boundaries.

A future architecture may resemble:

    Client
    |
    v
    Reverse Proxy / Gateway
    |
    Application Network
    |
    +-------------+-------------+
    |                           |
    v                           v
    **ASP**.**NET** **API**                Auth Service
    |                           |
    +-------------+-------------+
    |
    Internal Boundary
    |
    v
    Database Network
    |
    +-------------+-------------+
    |                           |
    v                           v
    PostgreSQL                  Auth Database

The exact network topology will be defined during the networking phase.

Consequence

The **NEXUS** architecture now treats application data and authentication data as separate service-owned resources.

This decision provides a clear foundation for:

Containerization Network segmentation Service-to-service communication Authentication boundaries Independent service evolution Observability Failure testing Future deployment to a Linux VM or cloud environment

The additional operational complexity is accepted because explicit service boundaries are a core objective of the **NEXUS** distributed-systems project.