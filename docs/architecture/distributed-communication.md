# Distributed Service Communication

**Project:** NEXUS Distributed Systems Laboratory  
**Phase:** 7 — Distributed Service Communication  
**Status:** Implementation and integration testing complete

---

## 1. Overview

Phase 7 introduces asynchronous communication into the NEXUS platform using RabbitMQ and a dedicated .NET Telemetry Worker.

Previously, communication between services relied primarily on synchronous HTTP requests. Phase 7 extends this architecture by introducing message-based communication for telemetry processing.

The implementation supports two telemetry categories:

- Infrastructure telemetry
- Sensor telemetry

Both categories use a shared event contract and messaging infrastructure.

## 2. Architectural Objective

The objective was to introduce reliable asynchronous communication while maintaining the existing synchronous service communication model.

The implementation demonstrates:

- Message-based communication.
- Event-driven architecture.
- Shared message contracts.
- Independent background processing.
- Durable RabbitMQ exchanges and queues.
- Message acknowledgements.
- Retry handling.
- Dead-letter processing.
- Database persistence.

## 3. Distributed Communication Architecture

The telemetry processing pipeline follows this structure:

```text
              External Client
                    |
                    v
               Traefik v3.6
                    |
                    v
                NEXUS API
                    |
             JWT Authorization
                    |
                    v
            Telemetry Publisher
                    |
                    v
          RabbitMQ: nexus.telemetry
                    |
          +---------+---------+
          |                   |
          v                   v
    Infrastructure         Sensor
        Queue               Queue
          |                   |
          +---------+---------+
                    |
                    v
           Telemetry Worker
                    |
                    v
              PostgreSQL
                    |
                    v
          TelemetryRecords
```

The API publishes telemetry events without waiting for the background worker to complete database persistence.

The API responds with HTTP 202 Accepted once the message has been successfully published and confirmed by RabbitMQ.

## 4. Synchronous and Asynchronous Communication

NEXUS now demonstrates two communication patterns.

### Synchronous communication

The API communicates with the Authentication Service through internal HTTP requests.

The Authentication Service validates JWT tokens and returns the validation result.

```text
Client
  |
  v
NEXUS API
  |
  | HTTP POST
  v
Authentication Service
  |
  | Validation response
  v
NEXUS API
```

### Asynchronous communication

Telemetry events are published to RabbitMQ and processed independently by the Telemetry Worker.

```text
NEXUS API
    |
    | Publish event
    v
RabbitMQ
    |
    | Consume event
    v
Telemetry Worker
    |
    | Persist event
    v
PostgreSQL
```

This separates the API's request-handling responsibilities from background telemetry processing.

## 5. Shared Event Contracts

A dedicated .NET class library was introduced:

```text
src/
  Contracts/
    Contracts.csproj
    TelemetryEvent.cs
```

The Contracts project is referenced by both the API and Telemetry Worker.

### TelemetryCategory

The event category is represented by an enum:

```csharp
public enum TelemetryCategory
{
    Infrastructure,
    Sensor
}
```

### TelemetryEvent

The shared event contains:

| Property | Purpose |
|---|---|
| EventId | Unique event identifier |
| Category | Infrastructure or Sensor |
| SchemaVersion | Event contract version |
| OccurredAt | Original event timestamp |
| Source | Originating service or sensor |
| Data | Telemetry measurement |

### TelemetryMeasurement

The measurement contains:

| Property | Purpose |
|---|---|
| Metric | Measurement name |
| Value | Numeric measurement |
| Unit | Measurement unit |
| Dimensions | Additional contextual metadata |

The flexible Dimensions dictionary allows different telemetry sources to provide additional information without requiring a separate event class for every measurement type.

## 6. RabbitMQ Infrastructure

RabbitMQ 4 Management was introduced as the messaging broker.

The container:

- Uses dedicated persistent storage.
- Uses environment-based credentials.
- Connects to `nexus-app-network`.
- Does not publish AMQP or management ports to the host.
- Uses a health check to verify broker availability.

The API and Telemetry Worker connect using Docker's internal DNS hostname:

```text
rabbitmq:5672
```

No container IP addresses are hardcoded.

## 7. Exchange and Queue Topology

The implementation uses three exchanges.

| Exchange | Type | Purpose |
|---|---|---|
| nexus.telemetry | Topic | Main telemetry distribution |
| nexus.telemetry.retry | Direct | Retry message routing |
| nexus.telemetry.dlx | Direct | Dead-letter routing |

### Main queues

| Queue | Routing key |
|---|---|
| nexus.telemetry.infrastructure | infrastructure |
| nexus.telemetry.sensor | sensor |

### Retry queues

| Queue | Purpose |
|---|---|
| nexus.telemetry.infrastructure.retry | Infrastructure retry processing |
| nexus.telemetry.sensor.retry | Sensor retry processing |

The retry queues use a configurable message TTL before returning messages to the main telemetry exchange.

### Dead-letter queue

```text
nexus.telemetry.dead-letter
```

Messages that exhaust their retry attempts are moved to this queue.

The topology uses durable exchanges and queues to preserve messaging infrastructure definitions across broker restarts.

## 8. API Message Publishing

The API uses RabbitMQ.Client 7.2.2.

The publisher is implemented through:

```text
src/Api/Services/ITelemetryPublisher.cs
src/Api/Services/RabbitMqTelemetryPublisher.cs
```

The publisher:

- Serializes telemetry events into JSON.
- Uses the event category as the routing key.
- Publishes persistent messages.
- Uses publisher confirmations.
- Uses mandatory publishing to detect unroutable messages.
- Associates the RabbitMQ message ID with the telemetry EventId.

The protected API endpoint is:

```http
POST /api/telemetry
```

The endpoint validates incoming telemetry, creates a shared TelemetryEvent and publishes it to RabbitMQ.

A successful publication returns:

```http
202 Accepted
```

The response includes the event ID and accepted status.

## 9. Telemetry Worker

The dedicated worker is implemented in:

```text
src/TelemetryWorker/
```

It is deployed as an independent Docker Compose service.

Its responsibilities include:

1. Establishing a RabbitMQ connection.
2. Initializing the messaging topology.
3. Consuming infrastructure and sensor queues.
4. Deserializing incoming events.
5. Validating event contents.
6. Checking for previously processed events.
7. Persisting telemetry records.
8. Acknowledging successful messages.
9. Retrying transient failures.
10. Forwarding exhausted or invalid messages to the dead-letter queue.

The worker connects to both application and database networks.

It does not expose any host ports.

## 10. Database Persistence

Telemetry records are stored in PostgreSQL using the `TelemetryRecords` table.

The persistence model includes:

- EventId
- Category
- SchemaVersion
- OccurredAt
- Source
- Metric
- Value
- Unit
- DimensionsJson
- ReceivedAt

The EventId is the primary key and provides a database-level duplicate protection mechanism.

Additional indexes support queries by event timestamp and telemetry category.

The DimensionsJson property uses PostgreSQL's JSONB data type.

The migration is:

```text
src/Api/Migrations/20261113003729_AddTelemetryRecords.cs
```

## 11. Message Acknowledgements and Duplicate Protection

The Telemetry Worker acknowledges messages after successful processing.

Messages are not acknowledged before database persistence completes.

Before inserting a telemetry record, the worker checks whether the EventId already exists.

The primary key provides an additional safeguard against duplicate insertion.

This supports idempotent processing when a message is delivered more than once.

## 12. Retry and Dead-Letter Handling

Transient processing failures are routed through dedicated retry queues.

The current configuration uses:

| Setting | Value |
|---|---:|
| Maximum retry attempts | 3 |
| Retry delay | 10,000 ms |
| Retry exchange | nexus.telemetry.retry |
| Dead-letter exchange | nexus.telemetry.dlx |

Invalid messages, including malformed or invalid event data, are routed directly to the dead-letter queue.

Transient failures are retried up to the configured limit. Messages that exhaust their retry attempts are sent to the dead-letter queue.

The original message is acknowledged only after its retry or dead-letter publication has been confirmed.

## 13. Configuration and Security

RabbitMQ connection settings are provided through environment variables.

Configuration includes:

- Broker hostname.
- Broker port.
- Username.
- Password.
- Exchange names.
- Queue names.
- Retry settings.

Sensitive credentials are stored in the local `.env` file, which is excluded from Git.

Safe placeholder values are maintained in `.env.example`.

## 14. Architectural Outcome

Phase 7 introduces asynchronous message-based communication into NEXUS while preserving the existing synchronous authentication architecture.

The resulting platform supports independent telemetry publishing and processing, durable messaging infrastructure, database persistence, duplicate protection and controlled failure handling.

**Phase 7 — Distributed Service Communication: Implementation and integration testing complete.**