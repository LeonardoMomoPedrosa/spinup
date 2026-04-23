# Epic 3 Development Plan: Log Capture and Streaming

## Goal

Capture stdout/stderr from managed service processes, store recent logs in bounded buffers, and expose both historical and real-time log/status streams to clients.

Epic 3 stories from `docs/product-spec.md`:
- Story 3.1: Capture stdout/stderr for managed processes
- Story 3.2: Implement bounded log buffering per service
- Story 3.3: Expose live event stream to clients

## Scope

### In Scope
- Log capture from processes launched by Epic 2 runtime manager
- In-memory bounded log storage per service
- API endpoints for recent logs
- Real-time streaming channel for logs and runtime events
- Test coverage for capture, buffering, and stream behavior

### Out of Scope
- Full-text search across logs
- Persistent long-term log retention
- Frontend log viewer UX (Epic 4)
- External observability stack integration

## Prerequisites

1. Epic 2 lifecycle manager is stable and used as the single process launcher.
2. Runtime state events are available from start/stop/exit transitions.
3. API can host long-lived HTTP connections (for SSE) in development and service mode.

## Key Design Decisions

1. **Streaming transport:** Server-Sent Events (SSE) first (simple and sufficient for one-way updates).
2. **Log model:** append-only, timestamped entries with stream type (`stdout`/`stderr`/`system`).
3. **Buffering policy:** ring buffer per service, fixed max lines (for example, 1000 lines/service).
4. **Event envelope:** single stream event schema for both log lines and runtime transitions.
5. **Ordering:** preserve per-service arrival order; global order is best-effort.
6. **Retention reset:** clear in-memory buffer only on explicit clear operation or app restart.

## Proposed Components

- `IServiceLogStore`
  - `Append(serviceId, entry)`
  - `GetRecent(serviceId, take, since?)`
  - `Clear(serviceId)` (optional)
- `ILogEventBroadcaster`
  - Publishes log and runtime events to active SSE subscribers
- `ServiceLogEntry`
  - `serviceId`, `timestamp`, `stream`, `message`, `sequence`
- `ServiceEvent`
  - `type` (`log`, `runtime`), payload, correlation metadata

## API Additions

- `GET /api/services/{id}/logs?take=200&since=<iso8601>`
- `DELETE /api/services/{id}/logs` (optional, if clear behavior is desired)
- `GET /api/stream` (SSE)

SSE events (initial):
- `event: log`
- `event: runtime`
- `event: heartbeat` (optional keepalive)

## Integration with Epic 2

1. Attach output handlers when process starts:
   - `OutputDataReceived` -> stdout log entry
   - `ErrorDataReceived` -> stderr log entry
2. Begin async read with `BeginOutputReadLine` and `BeginErrorReadLine`.
3. Publish lifecycle events (`Starting`, `Up`, `Stopping`, `Down`, `Error`) via broadcaster.
4. On process exit, emit final runtime event with exit code and message context.

## Implementation Phases

## Phase 0 - Contracts and Event Schema

### Deliverables
- Log/event DTOs and stream contract
- Buffer sizing config and defaults

### Tasks
- Define `ServiceLogEntry` contract.
- Define SSE payload envelope.
- Define query parameters for recent-log endpoint.
- Add config section:
  - `LogBuffer:MaxLinesPerService`
  - `Stream:HeartbeatSeconds` (optional)

### Exit Criteria
- Contracts are documented and consistent across store, API, and stream payloads.

## Phase 1 - Log Capture (Story 3.1)

### Deliverables
- Process output capture wired into runtime manager

### Tasks
- Add stdout/stderr handlers for every started process.
- Normalize and filter empty lines.
- Include timestamp and source stream on each captured line.
- Add safe detach/disposal on process stop/exit.

### Exit Criteria
- Running services generate visible log lines through backend APIs.

## Phase 2 - Bounded Log Buffer (Story 3.2)

### Deliverables
- Thread-safe per-service ring buffer implementation

### Tasks
- Implement bounded queue/ring buffer for each service.
- Ensure append/read are lock-safe under concurrent output.
- Add API read endpoint with `take` and optional `since` filtering.
- Enforce upper bounds and defaults for `take`.

### Exit Criteria
- Buffer size never exceeds configured max lines per service.
- Recent log retrieval is stable and performant.

## Phase 3 - Live Streaming (Story 3.3)

### Deliverables
- SSE endpoint with subscriber fan-out

### Tasks
- Implement subscriber management (connect/disconnect/cancel).
- Broadcast log and runtime events to all subscribers.
- Add heartbeat/keepalive events for idle periods.
- Ensure SSE endpoint handles cancellation cleanly.

### Exit Criteria
- Connected clients receive live log lines and runtime changes in near real-time.

## Phase 4 - Testing and Hardening

### Deliverables
- Unit and integration tests for log and streaming behavior

### Tasks
- Unit tests:
  - Ring buffer overflow behavior
  - Ordering guarantees per service
  - Event payload serialization
- Integration tests:
  - Start service -> logs appear in `/logs`
  - Runtime transitions emitted to stream
  - `stderr` and `stdout` both captured
- Reliability checks:
  - Subscriber disconnect handling
  - High-volume output resilience

### Exit Criteria
- Tests validate capture, buffering, and streaming under realistic output patterns.

## Suggested PR Breakdown (AI-Assisted)

1. PR 1: Contracts + config + log store abstraction
2. PR 2: Runtime manager log capture wiring
3. PR 3: `/api/services/{id}/logs` endpoint + bounded buffering
4. PR 4: `/api/stream` SSE endpoint + broadcaster
5. PR 5: Integration tests + resilience improvements

## Acceptance Criteria

- Stdout/stderr from managed services is captured by backend.
- Each service has bounded recent-log history with configured max size.
- API returns recent logs with predictable ordering and schema.
- SSE clients receive live log and runtime events.
- Stream and buffers remain stable under sustained output.

## Risks and Mitigations

- Risk: Output bursts overwhelm memory or subscribers.
  - Mitigation: Strict per-service buffer limits and lightweight event payloads.
- Risk: Blocking IO in output handlers impacts process supervision.
  - Mitigation: Non-blocking append and asynchronous broadcast fan-out.
- Risk: SSE clients silently disconnect behind proxies.
  - Mitigation: Heartbeat events and cancellation-aware cleanup.
- Risk: Event ordering confusion between runtime and log messages.
  - Mitigation: Include timestamps and sequence IDs in event payloads.

## Dependencies for Epic 4

Epic 4 (frontend UI) depends on Epic 3 outputs:
- Stable `/logs` retrieval endpoint
- Stable `/api/stream` event schema
- Consistent runtime + log payload contracts for UI rendering
