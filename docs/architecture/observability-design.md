# Observability Design

**Project:** NEXUS Distributed Systems Laboratory  
**Phase:** 8 — Observability & Monitoring  
**Status:** Implementation and runtime verification complete

---

## 1. Overview

Phase 8 introduces centralised observability into NEXUS.

Before this phase, runtime behaviour could only be inspected through individual container logs, RabbitMQ's management tooling and direct database queries. Diagnosing a problem meant inspecting each service separately and correlating events by hand.

Phase 8 adds the three observability signals:

| Signal | Question it answers | Backend |
|---|---|---|
| Metrics | How much, how fast, how often? | Prometheus |
| Traces | Where did one request go, and where was time spent? | Tempo |
| Logs | What exactly happened at a point in time? | Loki |

Grafana provides a single interface over all three.

The dashboard is an operational, infrastructure-facing view. It is not a product-facing application dashboard.

## 2. Architectural Objective

The objective was to make the behaviour of the distributed system observable without changing its service boundaries or weakening its network isolation.

Specific goals:

- Collect application telemetry through a vendor-neutral standard (OpenTelemetry).
- Collect infrastructure metrics from Traefik, RabbitMQ and PostgreSQL.
- Expose application-specific telemetry-processing metrics from the Telemetry Worker.
- Follow a single request across the API, Auth Service, RabbitMQ and Telemetry Worker as one distributed trace.
- Keep every observability backend internal, with Grafana as the only externally reachable component.

## 3. Architecture

```text
                  +-------------+   +-------------+   +------------------+
                  |  NEXUS API  |   | Auth Service|   | Telemetry Worker |
                  |  (.NET 10)  |   | (Node.js 24)|   |    (.NET 10)     |
                  +------+------+   +------+------+   +--------+---------+
                    OTLP gRPC        OTLP HTTP           OTLP gRPC
                         \               |                  /
                          v              v                 v
                       +----------------------------------------+
                       |        OpenTelemetry Collector         |
                       +-----+-------------+----------------+---+
                             |             |                |
                     metrics :8889      traces           logs
                             |             |                |
                             v             v                v
 Traefik :8082 -----> +------------+  +---------+     +---------+
 RabbitMQ :15692 ---> | Prometheus |  |  Tempo  |     |  Loki   |
 PG Exporter :9187 -> +-----+------+  +----+----+     +----+----+
                            \              |               /
                             +-------------+--------------+
                                           |
                                      +---------+
                                      | Grafana |  <-- Traefik /grafana
                                      +---------+
```

Applications push telemetry to the Collector over OTLP. Prometheus pulls metrics from the Collector and from the infrastructure exporters.

## 4. Components

| Component | Image | Responsibility |
|---|---|---|
| OpenTelemetry Collector | `otel/opentelemetry-collector-contrib:0.165.0` | Receives OTLP; routes metrics, traces and logs |
| Prometheus | `prom/prometheus:v3.8.0` | Scrapes and stores metrics (15-day retention) |
| Tempo | `grafana/tempo:2.9.0` | Stores distributed traces (168-hour retention) |
| Loki | `grafana/loki:3.6.3` | Stores logs (168-hour retention) |
| Grafana | `grafana/grafana:12.3.0` | Dashboards and exploration across all signals |
| PostgreSQL Exporter | `prometheuscommunity/postgres-exporter:v0.17.1` | Exposes PostgreSQL metrics |

RabbitMQ exposes metrics through its built-in `rabbitmq_prometheus` plugin. Traefik exposes metrics on a dedicated internal `metrics` entry point (`:8082`).

## 5. Telemetry Pipelines

### 5.1 Collector configuration

File: `infrastructure/observability/otel/collector.yaml`

| Pipeline | Receiver | Processors | Exporter |
|---|---|---|---|
| Metrics | OTLP | `memory_limiter`, `batch` | Prometheus exporter on `:8889` |
| Traces | OTLP | `memory_limiter`, `batch` | `otlp/tempo` → `tempo:4317` |
| Logs | OTLP | `memory_limiter`, `batch` | `otlphttp/loki` → `http://loki:3100/otlp` |

`memory_limiter` protects the Collector from memory exhaustion under load. `batch` reduces export overhead.

### 5.2 Service instrumentation

| Service | Mechanism | Protocol | Metrics | Traces | Logs |
|---|---|---|---|---|---|
| API | OpenTelemetry .NET (`AddOpenTelemetry()` + `UseOtlpExporter()`) | gRPC `:4317` | ASP.NET Core, Kestrel, HttpClient, runtime | ASP.NET Core, HttpClient, `Nexus.Api.Messaging` | Yes |
| Telemetry Worker | OpenTelemetry .NET | gRPC `:4317` | Runtime, `Nexus.TelemetryWorker` | `Nexus.TelemetryWorker` | Yes |
| Auth Service | OpenTelemetry Node SDK (`instrumentation.js`) | HTTP `:4318` | Auto-instrumentation | Auto-instrumentation (HTTP, Express, `pg`) | No — see limitations |

In OpenTelemetry .NET, `AddOpenTelemetry()` registers the OpenTelemetry logging provider, and `UseOtlpExporter()` exports every enabled signal. Logs from the .NET services therefore reach Loki without an explicit `WithLogging()` call.

The Auth Service loads its instrumentation with `node --require ./instrumentation.js server.js`, so the SDK is initialised before Express, `http` and `pg` are loaded.

### 5.3 Prometheus scrape targets

File: `infrastructure/observability/prometheus/prometheus.yml` (15-second scrape and evaluation intervals)

| Job | Target | Source |
|---|---|---|
| `prometheus` | `prometheus:9090` | Prometheus self-monitoring |
| `otel-collector` | `otel-collector:8889` | Application metrics via the Collector |
| `traefik` | `traefik:8082` | Gateway metrics |
| `rabbitmq` | `rabbitmq:15692` | Broker and queue metrics |
| `postgresql` | `postgres-exporter:9187` | Database metrics |

Because application metrics are scraped through the Collector, Prometheus assigns `job="otel-collector"`. The originating service is preserved as `exported_job` (for example `exported_job="nexus-telemetry-worker"`), and dashboard queries filter on that label.

## 6. Network Placement

The observability stack preserves the segmentation introduced in Phase 4.

| Component | App network | DB network | Observability network |
|---|---|---|---|
| API, Auth, Telemetry Worker | Yes | Yes | No |
| OpenTelemetry Collector | Yes | No | Yes |
| Prometheus | Yes | No | Yes |
| Traefik | Yes | No | Yes |
| PostgreSQL Exporter | No | Yes | Yes |
| Loki, Tempo | No | No | Yes |
| Grafana | Yes | No | Yes |

Application services reach the Collector over `nexus-app-network`. The Collector, which is attached to both networks, forwards telemetry to Loki and Tempo over `nexus-observability-network`. Application services therefore never join the observability network, and Loki and Tempo are not reachable from the application tier.

Only Traefik publishes a host port (`8080`). Grafana is reached through Traefik at `/grafana/`. Prometheus, Loki, Tempo, the Collector and the exporters have no host port exposure.

## 7. Custom Telemetry Worker Metrics

File: `src/TelemetryWorker/Observability/TelemetryWorkerInstrumentation.cs`  
Meter and activity source: `Nexus.TelemetryWorker`

| Instrument | Prometheus name | Labels | Recorded when |
|---|---|---|---|
| `nexus.telemetry.messages.processed` | `nexus_telemetry_messages_processed_total` | `category` | Event persisted and acknowledged |
| `nexus.telemetry.messages.duplicates` | `nexus_telemetry_messages_duplicates_total` | `category` | Event already stored; acknowledged without re-insertion |
| `nexus.telemetry.messages.failed` | `nexus_telemetry_messages_failed_total` | `category` | Processing throws (category is `unknown` if the message cannot be parsed) |
| `nexus.telemetry.messages.retried` | `nexus_telemetry_messages_retried_total` | `routing_key` | Message republished to a retry queue |
| `nexus.telemetry.messages.dead_lettered` | `nexus_telemetry_messages_dead_lettered_total` | none | Message published to the dead-letter exchange |
| `nexus.telemetry.processing.duration` (ms) | `nexus_telemetry_processing_duration_milliseconds_*` | `category` | Every delivery, in a `finally` block |

The OpenTelemetry unit `ms` is translated to the `_milliseconds` suffix, and counters gain `_total`, when exported to Prometheus.

Processing duration is recorded in a `finally` block so that it is captured even if failure routing itself throws. Message acknowledgement and routing behaviour are unchanged by the instrumentation.

## 8. Distributed Trace Propagation

### 8.1 Problem

HTTP calls between .NET and Node.js propagate trace context automatically. RabbitMQ does not: a message is a hand-off between two independent processes, and the trace context must be carried in the message itself.

Initial verification showed that API traces and worker traces were recorded as separate, unrelated traces. Tempo could not show a single request travelling from the API to the worker.

### 8.2 Design

Trace context is carried in RabbitMQ message headers using the W3C Trace Context standard (`traceparent` / `tracestate`), through OpenTelemetry's configured propagator.

**Publisher** (`src/Api/Services/RabbitMqTelemetryPublisher.cs`):

1. Starts a `Producer` span named `nexus.telemetry publish` from the `Nexus.Api.Messaging` activity source.
2. Tags it with OpenTelemetry messaging semantic conventions: system, operation type, exchange, routing key and message ID.
3. Injects the span's context into the message headers.

**Consumer** (`src/TelemetryWorker/Services/TelemetryConsumer.cs`):

1. Extracts the context from the message headers. RabbitMQ delivers string headers as UTF-8 byte arrays, so these are decoded first.
2. Starts the `telemetry.process` `Consumer` span as a child of the extracted context.
3. Messages without trace context, such as messages published before this change, start a new trace as before.

The worker's retry and dead-letter paths already copy all incoming headers. Retried messages therefore carry the original `traceparent`, and every processing attempt appears in the original trace.

### 8.3 Resulting trace

```text
POST /telemetry                      nexus-api         (ASP.NET Core)
├── POST                             nexus-api         (HttpClient → Auth token validation)
│   └── token validation spans        nexus-auth        (Node HTTP / Express auto-instrumentation)
└── nexus.telemetry publish          nexus-api         (Producer)
    └── telemetry.process            nexus-telemetry-worker (Consumer)
```

The gap between the end of the publish span and the start of `telemetry.process` represents time spent in RabbitMQ.

### 8.4 Alternative considered

RabbitMQ.Client 7 includes its own `ActivitySource` instrumentation. Explicit propagation was chosen because it is transparent, independent of client-library instrumentation details, and demonstrates the propagation mechanism directly.

## 9. Grafana

### 9.1 Provisioning

| Item | Location |
|---|---|
| Data sources | `infrastructure/observability/grafana/provisioning/datasources/` |
| Dashboard provider | `infrastructure/observability/grafana/provisioning/dashboards/` |
| Dashboard JSON | `infrastructure/observability/grafana/dashboards/nexus-overview.json` |

Data sources (Prometheus, Loki and Tempo) use fixed UIDs so that dashboard JSON can reference them reliably. Administrative credentials are supplied through environment variables and are not committed.

### 9.2 Dashboard: NEXUS — Infrastructure Overview

The dashboard contains 18 panels.

**Infrastructure (panels 1–13):** monitoring targets, PostgreSQL size, connections, transaction rate and deadlocks; RabbitMQ ready messages, queue depth, consumers and dead-letter rate; .NET memory, CPU and exceptions; Traefik open connections.

**Telemetry processing (panels 14–16):**

| Panel | Query basis |
|---|---|
| Telemetry Messages Processed | `increase()` of the processed counter over the dashboard range |
| Telemetry Processing Throughput | `rate()` over 5 minutes, by category |
| Telemetry Processing Duration | 5-minute rate-based average and `histogram_quantile(0.95, …)` |

**Telemetry failure handling (panels 17–18):** failed, retried, dead-lettered and duplicate counts as current totals since the worker started, plus a cumulative time series.

### 9.3 Query design decisions

- **Filter on `exported_job`**, not `job`, because application metrics arrive through the Collector's scrape job.
- **Avoid `exported_instance`**, which changes on every container restart.
- **Use `rate()` and `increase()` for counters,** which handle counter resets on restart.
- **Use windowed duration averages.** A cumulative `_sum / _count` average would be permanently skewed by the cold-start outlier observed during testing (about 7.9 seconds, compared with about 150–220 ms when warm).
- **Use lifetime totals for failure counters.** These counters change rarely and are created already holding the value 1 on first use. `increase()` cannot observe a series' first increment, so it would report 0 for these counters.

## 10. Security Considerations

- Only Traefik is exposed on the host. Observability backends remain internal.
- Grafana requires authentication. Its credentials come from environment variables.
- PostgreSQL Exporter uses a dedicated database account (`POSTGRES_EXPORTER_USER`) rather than the application credentials.
- Telemetry contains no credentials or tokens. Message headers carry only W3C trace identifiers.

## 11. Related Documents

- [Phase 8 Test Results](../infrastructure/phase-8-test-results.md)
- [Distributed Service Communication](distributed-communication.md)
- [Network Segmentation](../networking/network-segmentation.md)
- [Known Limitations](../KNOWN_LIMITATIONS.md)