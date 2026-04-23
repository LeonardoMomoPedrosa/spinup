# Epic 4 Development Plan: Frontend Management UI

## Goal

Build a usable React-based frontend for SpinUp that allows users to manage service definitions, control lifecycle operations, and monitor runtime/log output in real time.

Epic 4 stories from `docs/product-spec.md`:
- Story 4.1: Build service listing page with status badges
- Story 4.2: Add controls for start/stop/restart and bulk actions
- Story 4.3: Build add/edit/delete service forms
- Story 4.4: Add per-service log viewer with auto-scroll toggle

## Scope

### In Scope
- SPA frontend connected to existing backend APIs
- Service list/grid UI with runtime status and quick actions
- CRUD modals/forms for service definitions
- Lifecycle actions per service and global bulk controls
- Real-time updates via `/api/stream` + recent log fetch via `/api/services/{id}/logs`

### Out of Scope
- Authentication and authorization
- Team collaboration/multi-user controls
- Desktop installer or tray app integration
- Advanced analytics dashboards

## Prerequisites

1. Epic 1-3 backend endpoints stable.
2. Runtime and log contracts finalized enough for UI consumption.
3. Frontend project scaffolding decision finalized (React + TypeScript + Vite).

## Proposed Frontend Stack

- **Framework:** React + TypeScript
- **Bundler:** Vite
- **Data fetching/cache:** TanStack Query
- **UI components:** lightweight component library (e.g., shadcn/ui or MUI)
- **State:** local component state + query cache (no heavy global store required initially)
- **Realtime:** native `EventSource` SSE client for `/api/stream`

## UX and IA (Information Architecture)

### Main Screen Layout
- Header:
  - App title (`SpinUp`)
  - Global actions: `Add Service`, `Start All`, `Stop All`
- Body:
  - Service cards/table rows sorted by name
  - Status badge (`Down`, `Starting`, `Up`, `Stopping`, `Error`)
  - Per-service action buttons (`Start`, `Stop`, `Restart`, `Edit`, `Delete`, `Logs`)
- Side panel or expandable region:
  - Per-service log viewer with stream filter and auto-scroll toggle

### Key UX Behaviors
- Optimistic UI for quick action feedback where safe.
- Disable conflicting actions while requests are in-flight.
- Show inline error toasts/messages for failed lifecycle operations.
- Maintain selected service log view across refreshes where practical.

## API Integration Map

- Services:
  - `GET /api/services`
  - `POST /api/services`
  - `PUT /api/services/{id}`
  - `DELETE /api/services/{id}`
- Runtime:
  - `GET /api/services/runtime`
  - `GET /api/services/{id}/runtime`
  - `POST /api/services/{id}/start`
  - `POST /api/services/{id}/stop`
  - `POST /api/services/{id}/restart`
  - `POST /api/services/start-all`
  - `POST /api/services/stop-all`
- Logs:
  - `GET /api/services/{id}/logs`
- Streaming:
  - `GET /api/stream` (SSE: `log`, `runtime`, `heartbeat`)

## Frontend Modules

- `api/`:
  - typed clients for services/runtime/log endpoints
- `features/services/`:
  - service list, service card/row, status badge, action bar
- `features/forms/`:
  - add/edit modal form + validation helpers
- `features/logs/`:
  - log viewer, auto-scroll control, incremental append logic
- `realtime/`:
  - SSE connection manager and event dispatching
- `shared/ui/`:
  - reusable buttons, dialogs, empty states, toasts

## Implementation Phases

## Phase 0 - Bootstrap Frontend App

### Deliverables
- Frontend workspace scaffold
- Basic app shell and routing (single route is fine for v1)

### Tasks
- Create React + TS app (`web/spinup-ui` or similar).
- Add lint/format/test baseline.
- Set API base URL config and development proxy.
- Implement base layout and theme tokens.

### Exit Criteria
- Frontend runs locally and can call backend health/basic endpoint.

## Phase 1 - Service Listing and Status (Story 4.1)

### Deliverables
- Service list page with runtime status badges

### Tasks
- Fetch `GET /api/services`.
- Fetch runtime state (`/runtime`) and merge with service definitions by `serviceId`.
- Render cards/table with:
  - name, path, command
  - runtime badge and process metadata
- Add loading, empty, and error states.

### Exit Criteria
- User sees all configured services with accurate runtime status.

## Phase 2 - Lifecycle Controls (Story 4.2)

### Deliverables
- Per-service and bulk lifecycle controls

### Tasks
- Wire `start`, `stop`, `restart` actions with mutation hooks.
- Wire `start-all`, `stop-all` global actions.
- Handle in-flight UI locking and action conflicts.
- Show outcome notifications (success/failure with message).

### Exit Criteria
- User can control service lifecycle directly from UI.

## Phase 3 - CRUD Forms (Story 4.3)

### Deliverables
- Add/edit/delete service definition flows

### Tasks
- Build add/edit modal form with validation:
  - required fields
  - path format hints
- Implement create/update/delete mutations.
- Display backend validation errors cleanly in form.
- Refresh service list and runtime views after mutations.

### Exit Criteria
- User can fully manage service definitions from UI.

## Phase 4 - Log Viewer (Story 4.4)

### Deliverables
- Per-service log panel with auto-scroll toggle

### Tasks
- Load recent logs via `/api/services/{id}/logs`.
- Create log console component:
  - stream tag (`stdout`/`stderr`)
  - timestamp
  - message text
- Add auto-scroll toggle:
  - on: follow latest line
  - off: preserve user scroll position
- Add clear visual state when no logs exist.

### Exit Criteria
- User can inspect recent logs for selected service with controlled scrolling.

## Phase 5 - Real-time Event Wiring

### Deliverables
- SSE-driven live runtime/log updates

### Tasks
- Connect `EventSource` to `/api/stream`.
- Handle `runtime` events:
  - update badges and runtime metadata in cache/state.
- Handle `log` events:
  - append to visible log buffers by service.
- Handle reconnect strategy and transient disconnect messaging.

### Exit Criteria
- UI updates in near real-time without manual refresh.

## Phase 6 - Testing and Hardening

### Deliverables
- Automated tests + QA checklist

### Tasks
- Unit/component tests:
  - service row rendering
  - status badge mapping
  - form validation and submission
  - log viewer auto-scroll behavior
- Integration tests (Playwright or equivalent):
  - create service -> start -> stop
  - runtime badge transitions
  - logs appear while running command
- Resilience:
  - SSE reconnect behavior
  - API error handling coverage

### Exit Criteria
- Core user flows pass consistently in local CI.

## Suggested PR Breakdown (AI-Assisted)

1. PR 1: Frontend scaffold + app shell + API client base
2. PR 2: Service list + runtime badges
3. PR 3: Lifecycle controls (per-service + bulk)
4. PR 4: CRUD modals/forms + validation UX
5. PR 5: Log viewer + recent logs endpoint integration
6. PR 6: SSE live updates + reconnect handling
7. PR 7: Test suite + UX polish

## Acceptance Criteria

- Service list displays all configured services with status badges.
- User can start/stop/restart individual services.
- User can start/stop all services from global controls.
- User can create/edit/delete service definitions from UI.
- User can open a service log view and read recent logs.
- UI receives and applies live runtime/log updates from `/api/stream`.

## Risks and Mitigations

- Risk: Runtime state and service list drift out of sync.
  - Mitigation: canonical merge by `serviceId` and frequent cache updates.
- Risk: SSE reconnect causes duplicate log lines.
  - Mitigation: dedupe by `sequence` per service where possible.
- Risk: Large log rendering degrades UI performance.
  - Mitigation: cap in-memory UI log list and consider virtualization.
- Risk: User confusion during transient states (`Starting`/`Stopping`).
  - Mitigation: explicit badges, disabled controls, and action progress indicators.

## Dependencies for Epic 5+

Epic 5/6 will benefit from Epic 4 outputs:
- Stable interaction patterns for lifecycle operations
- Clear UX feedback patterns for failures and state transitions
- Baseline frontend test harness for future hardening work
