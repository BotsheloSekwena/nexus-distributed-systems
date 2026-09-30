
# NEXUS Network Segmentation

## Overview

Phase 4 introduces explicit Docker network segmentation to strengthen the architectural and security boundaries established during the earlier phases.

The Phase 3 architecture placed the API, Authentication Service, and PostgreSQL on a single Docker bridge network. While this allowed service communication, it did not provide a meaningful network boundary between application workloads and database infrastructure.

Phase 4 introduces separate application and database networks.

## Network Architecture

The implemented architecture consists of two Docker bridge networks:

- `nexus-app-network` 
- `nexus-db-network` 

The API and Authentication Service participate in both networks.

PostgreSQL participates only in the database network.

                     Host
                      |
            Published Application Ports
                      |
          +-----------+-----------+
          |                       |
         API                     Auth
       :5160                    :3000
          |                       |
          +-----------+-----------+
                      |
             nexus-app-network
                      |
          Application Boundary
                      |
                 API <-> Auth
                      |
              nexus-db-network
                      |
                      v
                 PostgreSQL
                 :5432

## Service Membership

| Service    | Application Network | Database Network | Purpose                                          |
| ---------- | ------------------- | ---------------- | ------------------------------------------------ |
| API        | Yes                 | Yes              | Application service requiring database access    |
| Auth       | Yes                 | Yes              | Authentication service requiring database access |
| PostgreSQL | No                  | Yes              | Database infrastructure                          |

## Verified Runtime Membership

The running Docker networks were inspected after deployment.

### Application Network


nexus-app-network

nexus-api
IPv4: 172.20.0.2/16

nexus-auth
IPv4: 172.20.0.3/16


### Database Network


nexus-db-network

nexus-postgres
IPv4: 172.19.0.2/16

nexus-api
IPv4: 172.19.0.3/16

nexus-auth
IPv4: 172.19.0.4/16


PostgreSQL is therefore not attached to `nexus-app-network`.

## Network Responsibilities

### Application Network

The application network provides the shared communication environment for application-level services.

Current members:

-  API 
-  Authentication Service 

This network is intended to support communication between application services and future infrastructure such as the reverse proxy/API gateway.

### Database Network

The database network provides the internal communication path between application services and PostgreSQL.

Current members:

-  API 
-  Authentication Service 
-  PostgreSQL 

PostgreSQL is intentionally excluded from the application network.

## Database Exposure

PostgreSQL no longer exposes port `5432` to the host.

The Docker Compose configuration retains PostgreSQL's internal port:


5432/tcp


but does not publish:


5432:5432


Normal application database communication therefore uses the Docker network:


postgres:5432


This was verified through both the API and Authentication Service database health endpoints.

## Database Connectivity Tests

### API

Endpoint:


GET /health/database


Result:


status: healthy
database: nexus
dataSource: tcp://postgres:5432
canConnect: true


### Authentication Service

Endpoint:


GET /health/database


Result:


status: healthy
database: nexus_auth


These tests demonstrate that removing the host database port did not prevent the application services from communicating with PostgreSQL.

## Network Isolation Test

A temporary BusyBox container was connected only to `nexus-app-network`.

The following command was executed:


docker run --rm --network nexus-app-network busybox:1.36 nslookup postgres


Result:


** server can't find postgres: NXDOMAIN


This demonstrates that the PostgreSQL service is not discoverable through Docker's internal DNS from the application network.

A second temporary container was connected to `nexus-db-network`:


docker run --rm --network nexus-db-network busybox:1.36 nslookup postgres


Result:


Name: postgres
Address: 172.19.0.2


This demonstrates that PostgreSQL is discoverable through the intended database network.

## Security Boundary

The network segmentation creates an infrastructure-level connectivity boundary.

The resulting architecture limits PostgreSQL's network membership to the database network while allowing only the required application services to access it.

This does not replace:

-  Application authentication 
-  Authorization 
-  TLS 
-  Database permissions 
-  Secrets management 
-  Input validation 
-  Application-level security controls 

Instead, network segmentation provides an additional infrastructure-level control that reduces unnecessary connectivity.

## Host Exposure Verification

The PostgreSQL container was inspected using:


docker port nexus-postgres


No host port mapping was returned.

The running Compose state showed:


nexus-postgres postgres:18 5432/tcp


while the application services retained only their required host-facing ports:


nexus-api 0.0.0.0:5160->8080/tcp
nexus-auth 0.0.0.0:3000->3000/tcp


## Runtime Status

The final Phase 4 runtime state was verified with:


docker compose ps


All three services were running:


nexus-api
nexus-auth
nexus-postgres


PostgreSQL reported:


healthy


## Architecture Result

The Phase 4 implementation establishes the following connectivity model:

                nexus-app-network
               +------------------+
               |                  |
               |  API <-> Auth    |
               |                  |
               +--------+---------+
                        |
                  Required Access
                        |
               +--------v---------+
               | nexus-db-network |
               |                  |
               | API <-> PostgreSQL |
               | Auth <-> PostgreSQL |
               |                  |
               +------------------+

PostgreSQL is not connected to the application network and is not exposed through a host port.

## Phase 4 Outcome

Phase 4 successfully implemented and verified Docker network segmentation.

The implementation demonstrates:

-  Explicit application and database networks 
-  Dual-homed application services 
-  Database isolation from the application network 
-  Internal database communication 
-  Removal of unnecessary database host exposure 
-  Successful application-to-database connectivity 
-  Successful authentication-service-to-database connectivity 
-  Verified DNS isolation between networks 

## Phase 4 Status

**Complete**

The next phase will introduce a reverse proxy/API gateway and establish a controlled entry point into the application services.
