# Phase 8 — Observability Test Results

**Project:** NEXUS Distributed Systems Laboratory  
**Phase:** 8 — Observability & Monitoring  
**Status:** Runtime verification complete

---

## 1. Testing Objective

Phase 8 testing verifies that metrics, traces and logs from the NEXUS services reach the observability stack. It also checks that the Telemetry Worker's custom metrics reflect real processing behaviour, including failure handling.

Configuration existing in source was not treated as evidence. Each capability was verified by producing real activity and observing the result at each stage of the pipeline:

```text
Service → OpenTelemetry Collector → Prometheus / Tempo / Loki → Grafana
```

All tests used existing application flows (the authenticated API) or standard operator tooling (the RabbitMQ management API). No test-only code was added.

## 2. Infrastructure Verification

The following were validated during implementation:

| Check | Method | Result |
|---|---|---|
| Docker Compose configuration | `docker compose config --quiet` | Valid |
| Prometheus configuration | `promtool check config` | Valid |
| OpenTelemetry Collector configuration | Collector validation | Valid |
| Tempo, Loki and Grafana provisioning | Container start-up and log inspection | Valid |
| Prometheus scrape targets | Prometheus targets view | 5 of 5 `UP` |
| RabbitMQ Prometheus plugin | `enabled_plugins` and scrape | Enabled |
| PostgreSQL Exporter | Exporter connection | Connected to PostgreSQL 18.6 |
| Grafana access through Traefik | `http://localhost:8080/grafana/` | Login successful |

## 3. End-to-End Telemetry Processing

### 3.1 Authenticated event submission

A user registered in Phase 6 authenticated through `POST /auth/login`. The JWT was held in a local PowerShell variable and never written to disk or logs.

One infrastructure event was submitted:

```http
POST http://localhost:8080/api/telemetry
```

| Stage | Evidence | Result |
|---|---|---|
| API | HTTP `202 Accepted`, `eventId` `ad6cadb5-6bb6-4f1f-9ec4-95e956bfc1e5` | Pass |
| Worker | `Processed telemetry event ad6cadb5-… in category Infrastructure.` | Pass |
| PostgreSQL | Row in `TelemetryRecords`: `cpu_usage`, `42.5` | Pass |

### 3.2 Custom metrics in the Collector

Earlier inspection had found no `nexus_telemetry_*` metrics in the Collector, although runtime metrics from the worker were present.

**Cause:** the OpenTelemetry .NET SDK exports an instrument only after it records its first measurement. No telemetry had been processed since the instrumentation was deployed. The pipeline itself was working.

After the first event, the Collector exposed:

```text
nexus_telemetry_messages_processed_total{category="infrastructure",job="nexus-telemetry-worker",...} 1
nexus_telemetry_processing_duration_milliseconds_count{category="infrastructure",...} 1
nexus_telemetry_processing_duration_milliseconds_sum{category="infrastructure",...} 7907.958
```

The first event took about 7.9 seconds, although its two database commands took about 340 ms. This is cold-start cost: EF Core model building, the first database connection and JIT compilation.

### 3.3 Prometheus

After three further events:

| Query | Result |
|---|---|
| `nexus_telemetry_messages_processed_total` | `4` |
| `…_sum / …_count` (cumulative average) | `2140.39` ms |

Subtracting the cold-start event gives about 218 ms per warm event, consistent with the database command timings.

Prometheus labelled the series `job="otel-collector"` and preserved the service as `exported_job="nexus-telemetry-worker"`. Dashboard queries were designed around this.

### 3.4 Grafana Explore

Each dashboard query was tested in Grafana Explore before being added to the dashboard:

| Query | Observed |
|---|---|
| Processed (`increase`, 15 min) | About 5, matching the events in the window |
| Throughput (`rate`, 5 min) | 0.010 and 0.0175 msg/s for bursts of 3 and 5 events |
| Average duration (5 min) | About 218 ms, then about 153 ms |
| p95 duration (5 min) | About 490 ms, then about 690 ms (bucket-interpolated) |

Between bursts, the windowed average returns `NaN` (0 ÷ 0), which Grafana renders as a gap. This is expected.

## 4. Failure-Handling Verification

Failure scenarios were produced by publishing directly to the `nexus.telemetry` exchange through the RabbitMQ management API. The request came from a temporary container on `nexus-app-network`, so RabbitMQ was not exposed on the host. The API's validation would correctly reject these payloads, so they could not be sent through it.

### 4.1 Dead-letter path: malformed message

| | |
|---|---|
| Input | Payload `this is not valid telemetry json`, routing key `infrastructure` |
| Expected | Permanent failure; dead-lettered without retry |
| Worker log | `Failed to process delivery 3.` → `Message 3 routed to the dead-letter queue. Reason: … invalid JSON literal …` |
| Metrics | `failed_total{category="unknown"} 1`, `dead_lettered_total 1` |
| Result | **Pass** |

### 4.2 Duplicate detection: same `EventId` twice

| | |
|---|---|
| Input | Identical valid event (`d2258fd4-5b85-4cdc-9c84-71c6e063d2e1`) published twice |
| Expected | Persisted once; second delivery acknowledged without re-insertion |
| Worker log | `Processed …` → `Telemetry event d2258fd4-… was already processed.` → `Processed …` |
| Metrics | `duplicates_total{category="infrastructure"} 1`; `processed_total` increased by 1, not 2 |
| Result | **Pass** |

### 4.3 Retry path: database outage

| | |
|---|---|
| Input | `nexus-postgres` stopped; valid event (`f30dfce6-7b9a-4731-8eb1-914e8b783793`) published; PostgreSQL restarted after about 5 seconds |
| Expected | Transient failure → retry queue → successful processing after recovery |
| Worker log | `Failed to process delivery 6.` → `scheduled for retry 1/3. Retry queue: nexus.telemetry.infrastructure.retry.` → `Processed telemetry event f30dfce6-…` |
| Metrics | `retried_total{routing_key="infrastructure"} 1`, `failed_total{category="infrastructure"} 1`, `processed_total` 3 → 4 |
| Result | **Pass** |

### 4.4 Dashboard confirmation

The failure-handling panels showed **Failed 2, Retried 1, Dead-lettered 1, Duplicates 1**. The cumulative chart stepped up at the times of each test.

## 5. Distributed Tracing

### 5.1 Initial state: traces not linked

API and worker traces were both present in Tempo, but every worker trace had `rootServiceName: nexus-telemetry-worker`. The worker's span started a new trace instead of continuing the API's trace.

**Cause:** trace context was not carried across RabbitMQ. HTTP propagates it automatically; AMQP messages do not.

**Fix:** W3C `traceparent` propagation through RabbitMQ message headers, with a producer span in the API. See [Observability Design §8](../architecture/observability-design.md).

### 5.2 After propagation

| Check | Result |
|---|---|
| `dotnet build` (Api, TelemetryWorker) | Succeeded |
| Worker trace search | Root `nexus-api` · `POST /telemetry` (trace `1f4415af2afaaf5d8da4a01cf3e9119b`) |

Spans in the trace:

| Instrumentation scope | Span |
|---|---|
| `Microsoft.AspNetCore` | `POST /telemetry` |
| `System.Net.Http` | `POST` (token validation call to Auth) |
| `Nexus.Api.Messaging` | `nexus.telemetry publish` |
| `Nexus.TelemetryWorker` | `telemetry.process` |

### 5.3 Auth Service instrumentation defect

No `nexus-auth` traces existed in Tempo. The Collector's `target_info` also listed only `nexus-api` and `nexus-telemetry-worker`.

**Cause:** the Auth Dockerfile started the service with `CMD ["node", "server.js"]`. The OpenTelemetry SDK is loaded only by the `npm start` script (`node --require ./instrumentation.js server.js`), so it never ran inside the container.

**Fix:** `CMD ["node", "--require", "./instrumentation.js", "server.js"]`. Node runs as PID 1 and receives `SIGTERM` directly, so the SDK's shutdown handler can flush pending telemetry.

| Check | Result |
|---|---|
| Auth start-up logs | No OpenTelemetry errors |
| `POST /auth/login` | Traced (root `nexus-auth`) |
| `pg.connect` | Traced (automatic `pg` instrumentation) |
| API telemetry request | Auth spans present inside the API's trace (`91946402b2206b2604eebf3dffa571d8`) |
| Collector `target_info` | `nexus-api`, `nexus/nexus-auth`, `nexus-telemetry-worker` |

A single request is now traced across the API, the Auth Service, RabbitMQ and the Telemetry Worker.

## 6. Log Aggregation

| Check | Result |
|---|---|
| Loki labels | `service_name`, `service_instance_id` |
| `service_name` values | `nexus-api`, `nexus-telemetry-worker` |

Logs from the .NET services reach Loki through OTLP. Auth Service logs do not, because its `console` output is not bridged to OpenTelemetry (see Known Limitations).

## 7. Npgsql GSSAPI Warning

The worker logged `Cannot load library libgssapi_krb5.so.2` on database connection.

**Cause:** Npgsql's default `GSS Encryption Mode` is `Prefer`, so it attempts GSS encryption by loading the Kerberos library. The library is not present in the .NET runtime images. Npgsql then fell back to password authentication.

**Fix:** `GSS Encryption Mode=Disable` was appended to the API and worker connection strings in `docker-compose.yml`. NEXUS uses password authentication on an internal network, so Kerberos is not required.

| Check | Result |
|---|---|
| `git diff --stat -- docker-compose.yml` | 2 insertions, 2 deletions |
| Event `a41dc212-12cc-48bd-a6f0-e1d1a6991d85` | Processed |
| `libgssapi` in API and worker logs | None |

## 8. Unplanned Finding: Auth Service Database Resilience

During the retry test (§4.3), stopping PostgreSQL caused the Auth Service to exit:

```text
nexus-auth   Exited (1)
code: '57P01'   (admin_shutdown)
```

**Cause:** `src/Auth/db.js` uses a single long-lived `pg.Client` with no `error` handler. When PostgreSQL closed the connection, the unhandled `error` event terminated the Node process. No compose service defines a restart policy, so the container remained stopped.

**Effect:** subsequent API requests returned `503`. The API could not validate tokens (HttpClient timeout after 3 seconds) and correctly failed closed.

**Comparison:** the Telemetry Worker survived the same outage and recovered through its retry queue, because EF Core obtains connections from a pool for each operation.

This is outside the scope of Phase 8 and is recorded in [Known Limitations](../KNOWN_LIMITATIONS.md).

## 9. Test Summary

| Area | Result |
|---|---|
| Infrastructure metrics (5 scrape targets) | Pass |
| Authenticated end-to-end processing | Pass |
| Custom metrics: processed, duration | Pass |
| Custom metrics: failed, dead-lettered | Pass |
| Custom metrics: duplicates | Pass |
| Custom metrics: retried | Pass |
| Grafana dashboard (18 panels) | Pass |
| Traces: API, worker, Auth | Pass (after fixes in §5) |
| Cross-service trace linking | Pass (after fix in §5.1) |
| Logs: API, worker | Pass |
| Logs: Auth | Not implemented |
| GSSAPI warning | Resolved |

## 10. Observations and Engineering Lessons

1. **Absence of a metric is not evidence of a broken pipeline.** OpenTelemetry exports an instrument only after its first measurement. Producing real activity separated pipeline faults from idle instrumentation.
2. **Instrumentation code is not instrumentation at runtime.** The Auth Service's OpenTelemetry setup was correct, but the container never loaded it. Verifying the output, not the configuration, exposed the defect.
3. **Asynchronous boundaries break traces unless context is carried explicitly.** HTTP propagation is automatic; message-broker propagation must be designed.
4. **Cold starts distort cumulative statistics.** One 7.9-second first event would have skewed a lifetime average indefinitely. Windowed rates give a truer picture.
5. **`increase()` cannot see a series' first increment.** For rarely changing counters, current totals are more honest than windowed increases.
6. **Failure testing reveals more than the component under test.** The database outage verified the worker's retry path and also exposed a resilience gap in the Auth Service.
7. **On Docker Desktop for Windows, bind-mounted file changes may not trigger Grafana's provisioning reload.** Restarting Grafana applied the updated dashboard.

## 11. Final Outcome

Phase 8 delivers working metrics, traces and logs across the NEXUS platform. Custom telemetry-processing metrics are verified for every processing outcome: success, duplicate, transient failure with retry, and permanent failure with dead-lettering. A single request can be followed from the API gateway through authentication, RabbitMQ and the Telemetry Worker in one distributed trace.

Remaining gaps are documented in [Known Limitations](../KNOWN_LIMITATIONS.md).