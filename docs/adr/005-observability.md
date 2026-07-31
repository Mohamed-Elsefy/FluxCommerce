# ADR-005: Observability Approach

## Status

Accepted

## Context

The platform spans an API, multiple modules, a database, an outbox, a message broker, and future consumers. Debugging and auditing business operations requires the ability to trace a single operation across these boundaries, and to correlate every log entry and metric with a request or message. Observability must therefore be a first-class architectural foundation rather than an afterthought.

The observability requirements are documented:

- `PROJECT_CONTEXT.md` §13 — the system must support structured logging, correlation IDs, trace IDs, distributed tracing, metrics, and health checks; the technology direction is "Serilog + OpenTelemetry"; "The goal is to trace a business operation across asynchronous boundaries."
- `ARCHITECTURE.md` §20 — the system uses structured logging, correlation IDs, trace IDs, OpenTelemetry, metrics, and health checks; "Every request should be traceable"; a future distributed workflow must be traceable across API -> Module -> Database -> Outbox -> RabbitMQ -> Consumer -> Another Module.
- `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.10 — Phase 0 establishes the observability foundation (structured logging, correlation IDs, trace IDs, OpenTelemetry, metrics, health checks, distributed tracing readiness); detailed business workflow instrumentation is added in later phases.
- `PHASE_0_ARCHITECTURE_FOUNDATION.md` §0.6 and `ARCHITECTURE.md` §18 — correlation ID handling is part of the API foundation.

## Decision

FluxCommerce establishes the following observability approach:

- **Structured logging with Serilog**: Serilog is registered as the logging infrastructure, reading configuration and enriching from the log context; console output uses a structured template that surfaces properties.
- **Correlation IDs**: a dedicated middleware assigns/propagates a correlation ID per request, makes it available to the request pipeline, and includes it in the log context so every log entry of a request carries the same CorrelationId. The correlation ID also feeds the current activity (span tag) for OpenTelemetry correlation.
- **Trace IDs**: request logs include TraceId and SpanId from the active activity, aligning structured logs with OpenTelemetry traces.
- **OpenTelemetry**: base configuration includes tracing (application activity source + ASP.NET Core instrumentation), metrics (ASP.NET Core + runtime instrumentation), and resource attributes (service name, version, instance, environment). Phase 0 uses console exporters; no business instrumentation is added.
- **Health checks**: infrastructure connectivity (application, SQL Server, Redis, RabbitMQ) is exposed through health-check endpoints, including a liveness endpoint scoped to the application.
- Phase 0 scope is limited to the foundation: request-level logging, correlation, and base telemetry. Cross-boundary tracing of business workflows (outbox -> broker -> consumer) is prepared for but not instrumented.

## Consequences

Positive:

- Every request is traceable through logs, traces, and metrics with a consistent correlation ID.
- Logs and traces are aligned (TraceId/SpanId present on log entries), which will make distributed workflow debugging possible in later phases.
- The foundation is generic infrastructure; it requires no per-module work.

Negative:

- OpenTelemetry console exporters are a Phase 0 baseline only; production-grade exporters and backend storage are not configured yet.
- Correlation IDs must be explicitly propagated across asynchronous boundaries (future outbox dispatcher and consumers) to fulfill the full traceability goal.
- Structured logging configuration adds a small bootstrap layer that all future modules inherit.

## Alternatives Considered

- The technology direction is prescribed by the documentation ("Serilog + OpenTelemetry", `PROJECT_CONTEXT.md` §13); no alternative logging or tracing stacks are documented in the source material.
