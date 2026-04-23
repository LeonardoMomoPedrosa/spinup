# Epic 2 Development Plan: Process Lifecycle Engine

## Goal

Implement reliable local process lifecycle management for each configured service, including start, stop, restart, bulk operations, and accurate runtime status transitions.

Epic 2 stories from `docs/product-spec.md`:
- Story 2.1: Implement per-service start flow
- Story 2.2: Implement stop and graceful shutdown with timeout fallback
- Story 2.3: Implement restart flow and start-all/stop-all orchestration
- Story 2.4: Track runtime status transitions and process exits

## Scope

### In Scope
- Lifecycle orchestration for registered services
- Runtime in-memory state tracking per service
- API endpoints for start/stop/restart and bulk operations
- Graceful stop behavior with forced termination fallback
- Core tests for lifecycle logic and endpoint behavior

### Out of Scope
- Log streaming and persistence (Epic 3)
- Frontend UX implementation (Epic 4)
- Windows service install and host packaging details (Epic 5)

## Prerequisites

1. Epic 1 complete and stable (CRUD + validation + persistence).
2. Service registry available for lookup by ID.
3. Runtime host can execute local processes from configured path/command.

## Key Design Decisions

1. **Runtime state location:** in-memory state map keyed by `serviceId`.
2. **Single instance policy:** one running process per service definition.
3. **Process API abstraction:** wrap `System.Diagnostics.Process` with a lifecycle service interface.
4. **State model:** `Down`, `Starting`, `Up`, `Stopping`, `Error`.
5. **Stop timeout:** configurable graceful timeout before force kill (for example, 10s).
6. **Concurrency control:** per-service lock/semaphore to prevent overlapping commands.

## Proposed Runtime Components

- `IServiceRuntimeManager`
  - `StartAsync(serviceId)`
  - `StopAsync(serviceId, forceAfterTimeout)`
  - `RestartAsync(serviceId)`
  - `StartAllAsync()`
  - `StopAllAsync()`
  - `GetRuntimeState(serviceId)` / `GetAllRuntimeStates()`
- `ServiceRuntimeStateStore`
  - Thread-safe state snapshot map
  - PID, start time, last exit code, last error
- `ProcessSupervisor`
  - Spawns child processes
  - Watches exit events
  - Updates runtime state transitions

## API Additions

- `POST /api/services/{id}/start`
- `POST /api/services/{id}/stop`
- `POST /api/services/{id}/restart`
- `POST /api/services/start-all`
- `POST /api/services/stop-all`
- `GET /api/services/runtime` (optional aggregated runtime state endpoint)

Suggested response fields:
- `serviceId`
- `status`
- `pid` (nullable)
- `startedAt` (nullable)
- `lastExitCode` (nullable)
- `lastError` (nullable)

## Implementation Phases

## Phase 0 - Contracts and State Model

### Deliverables
- Runtime DTOs and state enums
- Lifecycle manager interfaces
- Error contract for runtime operations

### Tasks
- Define runtime state enum and transition rules.
- Define lifecycle commands/result contracts.
- Document idempotency behavior:
  - Start when already `Up` -> no-op or conflict (choose and document).
  - Stop when already `Down` -> no-op or conflict (choose and document).

### Exit Criteria
- Lifecycle behavior matrix approved.

## Phase 1 - Start Flow (Story 2.1)

### Deliverables
- Per-service start capability with process creation

### Tasks
- Resolve service definition from Epic 1 persistence.
- Validate working directory exists before start.
- Build process start info using `path`, `command`, `args`, and env variables.
- Launch process and capture PID/start time.
- Apply state transitions: `Down/Error -> Starting -> Up`.

### Exit Criteria
- Start endpoint launches process and returns runtime state reliably.

## Phase 2 - Stop Flow with Graceful Timeout (Story 2.2)

### Deliverables
- Stop operation with graceful-first and force-fallback behavior

### Tasks
- Implement graceful termination strategy:
  - Try close/interrupt signal first (platform-appropriate).
  - Wait for timeout.
  - Force kill if still alive.
- Apply state transitions: `Up -> Stopping -> Down` or `Error`.
- Store termination metadata (`lastExitCode`, `lastError` when applicable).

### Exit Criteria
- Stop endpoint reliably terminates managed processes even when unresponsive.

## Phase 3 - Restart and Bulk Orchestration (Story 2.3)

### Deliverables
- Restart and bulk lifecycle operations

### Tasks
- Restart as atomic composed operation (`Stop` then `Start`) with safe sequencing.
- Implement `start-all` and `stop-all`:
  - Sequential first for predictability.
  - Optionally evolve to bounded parallelism later.
- Return per-service results in bulk operations (success/failure + reason).

### Exit Criteria
- Bulk operations complete with deterministic results and clear reporting.

## Phase 4 - Status Transitions and Exit Tracking (Story 2.4)

### Deliverables
- Process exit monitoring and runtime state consistency

### Tasks
- Subscribe to process exited events.
- Update runtime status on unexpected exits to `Error` (or `Down` with error metadata, per contract).
- Ensure state map cleanup when process ends.
- Expose current runtime state via API.

### Exit Criteria
- Runtime state remains accurate across normal and abnormal process exits.

## Phase 5 - Testing and Hardening

### Deliverables
- Unit and integration tests for lifecycle engine

### Tasks
- Unit tests:
  - State transitions
  - Idempotency and conflict behavior
  - Timeout fallback path
- Integration tests:
  - Start/stop/restart endpoints
  - Start-all/stop-all behavior
  - Unexpected process exit detection
- Negative tests:
  - Invalid path
  - Missing executable/command failure
  - Double start and double stop requests

### Exit Criteria
- Lifecycle tests pass consistently with no flaky process-leak behavior.

## Suggested PR Breakdown (AI-Assisted)

1. PR 1: Runtime state model + lifecycle interfaces + contracts
2. PR 2: Start flow implementation + endpoint
3. PR 3: Stop flow with timeout/force fallback + endpoint
4. PR 4: Restart and bulk orchestration endpoints
5. PR 5: Exit monitoring + runtime state API
6. PR 6: Integration tests and resilience fixes

## Acceptance Criteria

- User can start a configured service and receive `Up` status with PID.
- User can stop a running service gracefully, with force fallback when needed.
- User can restart a running service successfully.
- User can start and stop all configured services from bulk endpoints.
- Runtime states remain correct during transitions and after unexpected exits.
- Duplicate concurrent lifecycle operations on the same service are safely handled.

## Risks and Mitigations

- Risk: Process tree not fully terminated on force kill.
  - Mitigation: Use process-tree kill option where supported and validate in tests.
- Risk: Race conditions between exit events and API commands.
  - Mitigation: Per-service synchronization and atomic state updates.
- Risk: Command parsing issues (`dotnet run` style commands).
  - Mitigation: Explicit parsing strategy and test matrix for command/args combinations.
- Risk: Resource leaks (zombie processes, handle leaks).
  - Mitigation: Centralized process disposal and lifecycle cleanup hooks.

## Dependencies for Epic 3

Epic 3 (logs/streaming) depends on Epic 2 outputs:
- Stable process supervisor
- Reliable runtime state transitions
- Standard lifecycle event hooks for log/event emission
