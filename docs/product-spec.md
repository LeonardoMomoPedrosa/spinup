# SpinUp Product Specification

## 1. Product Overview

SpinUp is a local developer-operations application for managing multiple backend services from a single interface. The product helps developers start, stop, restart, and monitor local services used across systems such as e-commerce, ERP, and order management.

The core goal is to reduce context switching and manual terminal work while making service state and logs visible in one place.

## 2. Goals and Non-Goals

### Goals
- Centralize local service lifecycle management in one UI.
- Provide real-time status for each configured service.
- Expose service logs for quick debugging.
- Run continuously on Windows hosts as a background service.
- Support easy CRUD management of service definitions.

### Non-Goals (Phase 1)
- User authentication and role-based access.
- Remote server/container orchestration.
- CI/CD deployment integration.
- Multi-tenant team collaboration features.

## 3. Target Users

- Primary: Developers running several local services daily.
- Secondary: QA engineers or support engineers reproducing issues in local environments.

## 4. Functional Requirements

### 4.1 Service Configuration Management
- Add a service with:
  - Display name (e.g., `ERPCOM`)
  - Local path (e.g., `C:\projects\erpcom`)
  - Start command (e.g., `dotnet run`, `dotnet watch run`)
  - Optional environment variables
- Edit existing service configuration.
- Remove a service configuration.
- Persist configurations between restarts.

### 4.2 Service Lifecycle Controls
- Start an individual service.
- Stop an individual service.
- Restart an individual service.
- Start all configured services.
- Stop all running services.

### 4.3 Service Status and Health
- Show status per service:
  - `Down` (not running)
  - `Starting`
  - `Up` (running)
  - `Error` (process exited unexpectedly)
- Show process metadata (PID, start time, uptime).

### 4.4 Logs and Console Output
- Stream stdout/stderr output from each managed service.
- Show logs in near real-time in the UI.
- Keep recent log history per service (bounded buffer).
- Provide clear indication of process exits/errors.

### 4.5 Background Runtime
- SpinUp backend must run as a Windows Service.
- Service auto-starts with Windows.
- UI communicates with the service through local API.

## 5. Non-Functional Requirements

- **OS Support:** Windows (initial target).
- **Reliability:** Survive UI reloads without stopping managed processes.
- **Performance:** Handle at least 20 concurrently managed services on a typical developer machine.
- **Usability:** Main controls are available within two clicks for common actions.
- **Security (local scope):** Bind APIs to localhost only in Phase 1.

## 6. Proposed Architecture

### 6.1 High-Level Components
- **Windows Service Host (Backend)**
  - Runs continuously.
  - Manages child processes.
  - Stores service definitions.
  - Exposes a local HTTP API and optional WebSocket/SSE for live status and logs.
- **Web UI (Frontend)**
  - Displays configured services and states.
  - Triggers lifecycle actions.
  - Displays logs in real time.
- **Local Storage**
  - Lightweight database or structured file storage for service configurations and optional runtime metadata.

### 6.2 Data Flow
1. User creates/updates service definitions in UI.
2. UI sends requests to local backend API.
3. Backend persists config and executes process commands.
4. Backend streams status/log events to UI.
5. UI updates each service card/table row in real time.

## 7. Technology Recommendations

### Backend
- **Language/Runtime:** .NET 8 (Windows service support, process APIs, strong fit with `dotnet` workflows).
- **Hosting:** ASP.NET Core + Windows Service integration (`UseWindowsService`).
- **Real-time updates:** SignalR or Server-Sent Events (SSE).
- **Process control:** `System.Diagnostics.Process`.
- **Persistence:** JSON file for v1 simplicity.

### Frontend
- **Framework:** React + TypeScript.
- **UI stack:** Vite + component library (e.g., MUI or shadcn/ui).
- **State/data:** React Query + lightweight local state.

### Packaging/Operations
- Installable Windows service with a scripted setup flow.
- Optional tray launcher for opening the UI.

## 8. Data Model (Initial)

### ServiceDefinition
- `id` (GUID/string)
- `name` (string, unique)
- `path` (string)
- `command` (string)
- `args` (string, optional)
- `env` (key-value map, optional)
- `createdAt` (datetime)
- `updatedAt` (datetime)

### RuntimeState (in-memory, optionally persisted)
- `serviceId`
- `status`
- `pid` (nullable)
- `startedAt` (nullable)
- `lastExitCode` (nullable)
- `lastError` (nullable)

## 9. API Surface (Initial Draft)

- `GET /api/services` - list service definitions + runtime state
- `POST /api/services` - create service
- `PUT /api/services/{id}` - update service
- `DELETE /api/services/{id}` - delete service
- `POST /api/services/{id}/start` - start service
- `POST /api/services/{id}/stop` - stop service
- `POST /api/services/{id}/restart` - restart service
- `POST /api/services/start-all` - start all services
- `POST /api/services/stop-all` - stop all services
- `GET /api/services/{id}/logs` - read recent logs
- `GET /api/stream` - real-time status/log event stream

## 10. UX Scope (Phase 1)

- Main page with list/grid of service cards.
- Each card shows:
  - Name
  - Current status
  - Start/Stop/Restart buttons
  - Quick view of recent logs
- Global controls:
  - Add service
  - Start all / Stop all
- Modal/form for create/edit service definition.

## 11. Epic and Story Breakdown

### Epic 1: Core Service Registry
- Story 1.1: Create persistent schema for service definitions.
- Story 1.2: Implement CRUD API for service definitions.
- Story 1.3: Validate path/command inputs with clear error responses.

### Epic 2: Process Lifecycle Engine
- Story 2.1: Implement per-service start flow.
- Story 2.2: Implement stop and graceful shutdown with timeout fallback.
- Story 2.3: Implement restart flow and start-all/stop-all orchestration.
- Story 2.4: Track runtime status transitions and process exits.

### Epic 3: Log Capture and Streaming
- Story 3.1: Capture stdout/stderr for managed processes.
- Story 3.2: Implement bounded log buffering per service.
- Story 3.3: Expose live event stream to clients.

### Epic 4: Frontend Management UI
- Story 4.1: Build service listing page with status badges.
- Story 4.2: Add controls for start/stop/restart and bulk actions.
- Story 4.3: Build add/edit/delete service forms.
- Story 4.4: Add per-service log viewer with auto-scroll toggle.

### Epic 5: Windows Service Hosting and Setup
- Story 5.1: Host backend as Windows Service with auto-start.
- Story 5.2: Provide setup/install script for developer machines.
- Story 5.3: Add health endpoint and startup diagnostics.

### Epic 6: Quality and Hardening
- Story 6.1: Unit tests for lifecycle and validation logic.
- Story 6.2: Integration tests for API + process management.
- Story 6.3: Failure-mode handling (invalid path, command crash, port conflicts).
- Story 6.4: Performance test with 20 concurrent local services.

## 12. AI-Assisted Development Plan

- Use AI pair programming for each story with:
  - Story context
  - Acceptance criteria
  - Done checklist
- Keep PRs small (one story or sub-story each).
- Require test updates in every PR touching runtime/process logic.
- Use generated scaffolding for API contracts and UI components, then manually review lifecycle and error-handling paths.

## 13. Acceptance Criteria (Phase 1 Release)

- User can create at least one service and persist it.
- User can start/stop/restart each service from UI.
- User can start/stop all services from UI.
- User can view real-time status and logs for running services.
- Backend runs as Windows Service and is available after reboot.
- No login required.
