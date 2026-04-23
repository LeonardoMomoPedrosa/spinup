# Epic 5 Development Plan: Windows Service Hosting and Setup

## Goal

Run SpinUp backend reliably as a Windows Service that starts automatically with Windows, can be installed/uninstalled with a simple setup flow, and exposes health/diagnostic information for support.

Epic 5 stories from `docs/product-spec.md`:
- Story 5.1: Host backend as Windows Service with auto-start
- Story 5.2: Provide setup/install script for developer machines
- Story 5.3: Add health endpoint and startup diagnostics

## Scope

### In Scope
- Windows Service hosting configuration for `SpinUp.Api`
- Service install/update/uninstall scripts
- Startup checks, health endpoints, and diagnostics output
- Documentation for running in service mode vs dev mode

### Out of Scope
- MSI/GUI installer packaging (can be future improvement)
- Cross-platform daemon/service support
- Cloud deployment/orchestration

## Prerequisites

1. Epic 1-4 features are stable in normal process mode.
2. Backend runs cleanly without interactive console dependency.
3. API and SQLite storage path strategy is defined for service account context.

## Key Design Decisions

1. **Hosting model:** ASP.NET Core + `UseWindowsService()`.
2. **Service account:** start with `LocalService` or configurable account for dev machines.
3. **Startup type:** `Automatic` (or `Automatic (Delayed Start)` if needed).
4. **Data/log paths:** explicit application data folder (avoid relative working-dir assumptions).
5. **Install strategy:** PowerShell scripts using `sc.exe` or `New-Service`.

## Runtime Architecture in Service Mode

- `SpinUp.Api` runs as:
  - normal `dotnet run` in dev
  - Windows Service in installed mode
- Same API endpoints and runtime manager logic.
- Frontend continues connecting to local API URL.
- Service-mode logging writes to:
  - Windows Event Log and/or
  - rolling file logs in app data folder.

## Configuration Requirements

- `appsettings.json` + optional `appsettings.Production.json`:
  - API binding (localhost only)
  - SQLite database absolute path
  - log buffer limits
  - service-specific settings (timeouts, diagnostics)
- Optional environment variables for install-time overrides.

## Implementation Phases

## Phase 0 - Service Readiness Audit

### Deliverables
- Gap list for service-host constraints

### Tasks
- Validate no UI/interactive dependency in backend startup.
- Validate process launch paths work under service account context.
- Validate write permissions for DB and log directories.

### Exit Criteria
- Backend is safe to host as Windows Service.

## Phase 1 - Windows Service Hosting (Story 5.1)

### Deliverables
- Backend bootstrapped for service hosting

### Tasks
- Enable Windows Service host integration in `Program.cs`.
- Add environment-aware startup behavior for service mode.
- Ensure graceful start/stop behavior and cancellation handling.
- Set Kestrel URL binding for localhost in service mode.

### Exit Criteria
- App starts and stops correctly under Windows Service Control Manager.

## Phase 2 - Install/Uninstall/Update Scripts (Story 5.2)

### Deliverables
- PowerShell scripts for local machine setup

### Tasks
- Create scripts:
  - `install-service.ps1`
  - `uninstall-service.ps1`
  - `restart-service.ps1`
  - `update-spinup.ps1` (in `scripts/windows`, recommended)
- Script responsibilities:
  - publish/build binaries
  - create service
  - set startup type automatic
  - start service
  - trigger health check immediately after start (no grace period)
- Add clear rollback/error messages in scripts.

### Exit Criteria
- One-command install/uninstall flow works on clean dev machines.

## Phase 3 - Health and Startup Diagnostics (Story 5.3)

### Deliverables
- Health and diagnostics endpoints and startup checks

### Tasks
- Add health endpoints:
  - `GET /health/live`
  - `GET /health/ready`
- Readiness checks:
  - DB connectivity
  - writable storage paths
  - essential service configuration validity
- Add startup diagnostics payload/log:
  - environment
  - resolved DB path
  - bound URLs
  - runtime options snapshot (safe subset)

### Exit Criteria
- Operators can quickly diagnose service startup and readiness failures.

## Phase 4 - Operational Hardening

### Deliverables
- Production-like service behavior on local machines

### Tasks
- Configure recovery options (restart on failure).
- Add timeout tuning for startup/shutdown.
- Ensure process cleanup on service stop.
- Add log retention policy for file logs (if enabled).

### Exit Criteria
- Service recovers from common failure cases with minimal manual intervention.

## Phase 5 - Documentation and Validation

### Deliverables
- Setup/runbook docs and validation checklist

### Tasks
- Document:
  - install/uninstall steps
  - where logs/DB are stored
  - troubleshooting guide (port in use, permission denied, failed start)
- Validation matrix:
  - fresh install
  - reboot auto-start
  - upgrade flow
  - uninstall cleanup

### Exit Criteria
- New developer can install and validate service mode in minutes.

## Suggested PR Breakdown (AI-Assisted)

1. PR 1: Service host integration + config adjustments
2. PR 2: Install/uninstall scripts + script tests
3. PR 3: Health/readiness endpoints + startup diagnostics
4. PR 4: Recovery/hardening + docs/runbook

## Acceptance Criteria

- Backend can run as a Windows Service and starts automatically with Windows.
- Setup script installs and starts the service on a clean machine.
- Uninstall script removes the service cleanly.
- Health endpoints clearly report liveness and readiness.
- Startup diagnostics make configuration/runtime failures actionable.

## Risks and Mitigations

- Risk: Service account lacks filesystem permissions for DB/log paths.
  - Mitigation: preflight permission checks and clear setup script guidance.
- Risk: Service starts before dependent resources are ready.
  - Mitigation: readiness checks + retry/backoff strategy.
- Risk: Port binding conflicts with existing local processes.
  - Mitigation: configurable ports + startup diagnostic messages.
- Risk: Relative path assumptions fail in service context.
  - Mitigation: normalize all critical paths to absolute values.

## Dependencies for Epic 6

Epic 6 quality/hardening will build on Epic 5 outputs:
- stable service-mode lifecycle behavior
- actionable diagnostics and health signals
- repeatable install/upgrade/uninstall workflows
