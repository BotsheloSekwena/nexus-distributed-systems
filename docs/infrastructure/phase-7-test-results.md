# Phase 7 — Distributed Communication Test Results

**Project:** NEXUS Distributed Systems Laboratory  
**Phase:** 7 — Distributed Service Communication  
**Status:** Integration testing complete

---

## 1. Testing Objective

Phase 7 testing verifies that telemetry events can travel through the complete NEXUS messaging architecture.

The primary processing path is:

```text
NEXUS API
    |
    v
RabbitMQ
    |
    v
Telemetry Worker
    |
    v
PostgreSQL
```

Testing covers infrastructure telemetry, sensor telemetry, database persistence, retry handling, dead-letter processing and recovery following a database connectivity failure.

## 2. Runtime Environment Verification

The Docker Compose environment was inspected using:

```powershell
docker compose ps
```

The environment contained:

- nexus-api
- nexus-auth
- nexus-postgres
- nexus-rabbitmq
- nexus-telemetry-worker
- nexus-traefik

PostgreSQL and RabbitMQ reported healthy states.

The Telemetry Worker and API were running without published host ports.

## 3. Application Health Verification

The following endpoints were tested through Traefik.

| Endpoint | Expected result | Actual result |
|---|---|---|
| `/api/health` | API healthy | Healthy |
| `/api/health/database` | Database connected | Healthy |
| `/auth/health` | Auth healthy | Healthy |
| `/auth/health/database` | Auth database connected | Healthy |

All four endpoints returned successful health responses.

The API reported:

```json
{
  "status": "healthy",
  "database": "nexus",
  "dataSource": "tcp://postgres:5432",
  "canConnect": true
}
```

## 4. Infrastructure Telemetry Integration Test

An infrastructure telemetry event was submitted through:

```http
POST /api/telemetry
```

The test used:

| Property | Value |
|---|---|
| Category | Infrastructure |
| Source | nexus-api |
| Metric | cpu_usage |
| Value | 28.4 |
| Unit | % |
| Environment | development |

The API returned:

```text
HTTP 202 Accepted
```

Event ID:

```text
faca8cdd-b824-4516-bb85-79a5f7f40689
```

The Telemetry Worker logged:

```text
Processed telemetry event
faca8cdd-b824-4516-bb85-79a5f7f40689
in category Infrastructure.
```

The PostgreSQL query confirmed that the record was persisted successfully.

Result:

| Category | Source | Metric | Value | Unit |
|---|---|---|---:|---|
| Infrastructure | nexus-api | cpu_usage | 28.4 | % |

**Result: Passed.**

## 5. Sensor Telemetry Integration Test

A second event was submitted using the Sensor category.

The test used:

| Property | Value |
|---|---|
| Category | Sensor |
| Source | sensor-001 |
| Metric | temperature |
| Value | 24.8 |
| Unit | Celsius |
| Location | classroom-01 |
| Device type | temperature-sensor |

The API returned:

```text
HTTP 202 Accepted
```

Event ID:

```text
2e291128-f835-49b1-a5d3-d339a38a15c4
```

The Telemetry Worker logged:

```text
Processed telemetry event
2e291128-f835-49b1-a5d3-d339a38a15c4
in category Sensor.
```

PostgreSQL confirmed that the record was persisted.

The DimensionsJson column retained the additional metadata:

```json
{
  "test": "phase-7-recovery",
  "location": "classroom-01",
  "deviceType": "temperature-sensor"
}
```

**Result: Passed.**

## 6. Retry Mechanism Test

The retry mechanism was tested by temporarily disconnecting the Telemetry Worker from the database network while keeping RabbitMQ available.

The following command was used:

```powershell
docker network disconnect nexus-db-network nexus-telemetry-worker
```

The API and Authentication Service remained operational.

A new infrastructure telemetry event was submitted successfully and received HTTP 202 Accepted.

The worker attempted to persist the event but could not resolve the PostgreSQL hostname because its database network connection had been removed.

The worker logged:

```text
Telemetry message scheduled for retry 1/3.
Retry queue: nexus.telemetry.infrastructure.retry.
```

The message subsequently exhausted its configured retry attempts and was transferred to the dead-letter queue.

RabbitMQ reported:

```text
nexus.telemetry.dead-letter     1     0
```

The message was retained rather than silently discarded.

**Result: Passed.**

## 7. Dead-Letter Queue Verification

The dead-letter queue was inspected using:

```powershell
docker exec nexus-rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged
```

The queue contained one message from the deliberate failure test.

This confirmed that the failed event was retained after exhausting its retry attempts.

The message was deliberately left in the queue for evidence and future inspection.

**Result: Passed.**

## 8. Database Connectivity Recovery Test

After the retry test, the Telemetry Worker was reconnected to the database network:

```powershell
docker network connect nexus-db-network nexus-telemetry-worker
```

The worker's network membership was inspected using:

```powershell
docker inspect nexus-telemetry-worker --format '{{json .NetworkSettings.Networks}}'
```

Both networks were present:

- nexus-app-network
- nexus-db-network

A fresh infrastructure event was submitted.

The worker successfully processed the event and persisted it in PostgreSQL.

This confirmed that the worker recovered after database connectivity was restored.

**Result: Passed.**

## 9. Final Queue Verification

The final RabbitMQ queue inspection returned:

| Queue | Ready messages | Unacknowledged messages |
|---|---:|---:|
| nexus.telemetry.infrastructure | 0 | 0 |
| nexus.telemetry.sensor | 0 | 0 |
| nexus.telemetry.infrastructure.retry | 0 | 0 |
| nexus.telemetry.sensor.retry | 0 | 0 |
| nexus.telemetry.dead-letter | 1 | 0 |

The infrastructure and sensor queues contained no pending or unacknowledged messages.

The retry queues were empty.

The single dead-letter message was the intentionally retained failure-test event.

**Result: Passed.**

## 10. Build and Configuration Verification

The following commands were executed.

### API build

```powershell
dotnet build .\src\Api\Api.csproj
```

Result:

```text
Build succeeded.
```

### Telemetry Worker build

```powershell
dotnet build .\src\TelemetryWorker\TelemetryWorker.csproj
```

Result:

```text
Build succeeded.
```

### Docker Compose validation

```powershell
docker compose config --quiet
```

Result:

```text
Configuration valid.
```

### Git whitespace verification

```powershell
git diff --check
```

No whitespace errors were reported. Git displayed line-ending warnings for two files, which were not build failures.

## 11. Test Summary

| Test | Result |
|---|---|
| Docker runtime verification | Passed |
| API health | Passed |
| API database health | Passed |
| Auth health | Passed |
| Auth database health | Passed |
| Infrastructure event publishing | Passed |
| Infrastructure event consumption | Passed |
| Infrastructure database persistence | Passed |
| Sensor event publishing | Passed |
| Sensor event consumption | Passed |
| Sensor database persistence | Passed |
| Retry handling | Passed |
| Dead-letter handling | Passed |
| Database connectivity recovery | Passed |
| Final queue verification | Passed |
| API build | Passed |
| Worker build | Passed |
| Docker Compose validation | Passed |

## 12. Observations and Engineering Lessons

### Asynchronous processing

The API can accept telemetry without waiting for the Telemetry Worker to complete database persistence.

### Service independence

The Telemetry Worker operates independently of the API request lifecycle.

### Failure handling

Temporary database connectivity failures do not require immediate message loss. The worker retries failed processing and eventually transfers exhausted messages to the dead-letter queue.

### Idempotency

Event identifiers and database primary-key enforcement provide protection against duplicate persistence.

### Infrastructure troubleshooting

During testing, disconnecting the worker from the database network allowed retry behaviour to be tested without stopping PostgreSQL or interrupting the Authentication Service.

This provided a more isolated failure test.

## 13. Final Outcome

Phase 7 successfully demonstrated asynchronous distributed service communication within NEXUS.

Both infrastructure and sensor telemetry were accepted by the API, routed through RabbitMQ, consumed by the dedicated Telemetry Worker and persisted in PostgreSQL.

Retry handling, dead-letter processing and recovery after connectivity restoration were also verified.

**Phase 7 — Implementation and integration testing complete.**