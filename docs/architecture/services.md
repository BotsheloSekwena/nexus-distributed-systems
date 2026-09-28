# NEXUS Service Architecture

## Overview

**NEXUS** is designed as a distributed backend platform consisting of independently structured services with clearly separated responsibilities.

The current Phase 2 architecture contains:

- **ASP**.**NET** Core **API**
- Node.js Authentication Service
- PostgreSQL
- Separate application and authentication databases

This architecture establishes the foundation for later phases involving containerization, network segmentation, **API** gateway integration, authentication, service communication, and observability.

---

## Current Architecture

```text
    Client
    |
    **HTTP** Requests
    |
    +-----------+-----------+
    |                       |
    v                       v
    **ASP**.**NET** Core **API**       Node.js Auth Service
    Port **5160**                Port **3000**
    |                       |
    |                       |
    v                       v
    nexus DB              nexus_auth DB
    \                       /
    \                     /
    +--------+-----------+
             |
             v
      PostgreSQL Server

Both databases currently reside on the same PostgreSQL server instance.

The database separation is intentional and establishes a logical boundary between application data and authentication data.

---

## ASP.NET Core API

### Responsibility

The **ASP**.**NET** Core **API** is the primary application service within **NEXUS**.

Its current responsibilities include:

- Application **API** endpoints
- Service registration
- Entity Framework Core database access
- Application database health monitoring
- OpenAPI and Swagger documentation

### Technology

- **ASP**.**NET** Core
- .**NET** 10
- Entity Framework Core
- Npgsql
- PostgreSQL

### Endpoint

```text [http://localhost:**5160**](http://localhost:**5160**)

### Health Endpoint

**GET** /health

Expected response:

{
    *status*: *healthy*,
    *service*: *nexus-api*
}
### Database Health Endpoint
**GET** /health/database

Expected response:

{
    *status*: *healthy*,
    *database*: *nexus*
}

The database health endpoint verifies that the **API** can establish a connection to PostgreSQL and execute a database query.

Node.js Authentication Service Responsibility

The Authentication Service handles identity-related operations separately from the main application **API**.

Its current responsibilities include:

User registration User lookup Password hashing Password verification User authentication **JWT** generation Technology Node.js Express PostgreSQL bcrypt **JSON** Web Tokens Endpoint [http://localhost:**3000**](http://localhost:**3000**) ### Health Endpoint **GET** /health

Expected response:

{
    *status*: *healthy*,
    *service*: *nexus-auth*
}
### Database Health Endpoint
**GET** /health/database

Expected response:

{
    *status*: *healthy*,
    *database*: *nexus_auth*
}
### Authentication Endpoints

Registration:

**POST** /auth/register

Login:

**POST** /auth/login

The login endpoint authenticates the supplied credentials and generates a **JSON** Web Token when authentication succeeds.

### Database Architecture

**NEXUS** currently uses one PostgreSQL server containing two separate databases.

PostgreSQL Server
│
├── nexus
│   └── **ASP**.**NET** Core application data
│
└── nexus_auth
    └── Authentication and identity data

The **ASP**.**NET** Core **API** connects to:

Database: nexus

The Authentication Service connects to:

Database: nexus_auth

This prevents authentication data from being directly mixed with the application service's database schema.

### Database Ownership

Each service has an explicit database ownership boundary.

**ASP**.**NET** Core **API**
    |
    +---- owns ----> nexus

Node.js Auth Service
    |
    +---- owns ----> nexus_auth

The services do not directly share database tables.

If information needs to be exchanged between services in future phases, the preferred approach will be service-to-service communication rather than direct access to another service's database.

Configuration

Database connection information is supplied through environment variables and local development configuration.

Sensitive development values are stored in:

.env

The .env file is excluded from Git through .gitignore.

A .env.example file is committed as a safe configuration template so that another developer can understand the required environment variables without receiving the actual development secrets.

### Health Monitoring

Each service exposes a basic health endpoint.

The distinction between service health and database health is intentional.

### Service Health

A service health endpoint verifies that the application process is running and responding to **HTTP** requests.

### Database Health

A database health endpoint verifies that the application can communicate with its PostgreSQL database.

This creates two separate diagnostic layers:

### Service Process

    |
    v
    /health
    |
    v
Application responding?

### Database Connection

    |
    v
/health/database
    |
    v
PostgreSQL reachable?

This distinction becomes increasingly useful as the **NEXUS** architecture grows into multiple containers and networks.

### Current Security Boundary

The Phase 2 implementation establishes logical service and data boundaries but does not yet provide complete network isolation.

Currently:

**ASP**.**NET** Core **API**
    |
    +----> nexus

Node.js Auth Service
    |
    +----> nexus_auth

Both services communicate with the PostgreSQL server through the local development environment.

Physical network segmentation will be introduced during the networking and containerization phases.

### Current Limitations

The current Phase 2 architecture is intentionally development-oriented.

The following capabilities have not yet been introduced:

Docker container networking Network segmentation Reverse proxy or **API** gateway **HTTPS** termination Authentication middleware on protected **API** routes Message broker communication Distributed tracing Centralized logging Metrics collection Failure injection and resilience testing

These capabilities are planned for later phases of the **NEXUS** roadmap.

### Future Architecture

The current service architecture will evolve toward:

    Internet / Client
    |
    **HTTPS**
    |
    v
    Reverse Proxy / **API** Gateway
    |
    Security Boundary
    |
    +---------------+---------------+
    |                               |
    v                               v
    **ASP**.**NET** Core **API**              Node.js Auth Service
    |                               |
    |                               |
    +---------------+---------------+
    |
    Internal Services
    |
    v
    RabbitMQ
    |
    +---------------+---------------+
    |                               |
    v                               v
    Application DB                    Auth DB
    |                               |
    +---------------+---------------+
    |
    Observability
    |
    +----------------+----------------+
    |                |                |
    v                v                v
    Prometheus         Loki           OpenTelemetry
    |                |                |
    +----------------+----------------+
    |
    v
    Grafana

The final architecture will introduce explicit network boundaries between public-facing services, application services, internal infrastructure, and databases.