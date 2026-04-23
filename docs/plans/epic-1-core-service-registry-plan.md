# Epic 1 Development Plan: Core Service Registry

## Goal

Deliver a reliable, persistent service registry that allows users to create, read, update, and delete service definitions with strong validation and clear error handling.

Epic 1 stories from `docs/product-spec.md`:
- Story 1.1: Create persistent schema for service definitions
- Story 1.2: Implement CRUD API for service definitions
- Story 1.3: Validate path/command inputs with clear error responses

## Scope

### In Scope
- Backend persistence model for `ServiceDefinition`
- Storage implementation (SQLite recommended)
- CRUD endpoints for service definitions
- Validation rules and standardized API error responses
- Unit and integration tests for Epic 1 behavior

### Out of Scope
- Process start/stop/restart runtime management (Epic 2)
- Log capture/streaming (Epic 3)
- Full frontend implementation (Epic 4)
- Windows service packaging concerns beyond local API runtime

## Prerequisites and Decisions

1. Confirm backend stack: .NET 8 + ASP.NET Core.
2. Confirm persistence choice: SQLite for v1.
3. Decide migration strategy:
   - Use EF Core migrations, or
   - Use lightweight SQL bootstrap script.
4. Define API versioning strategy (`/api/services` for v1).

## Implementation Phases

## Phase 0 - Design and Contracts

### Deliverables
- `ServiceDefinition` domain model contract
- DTO contracts for create/update/response
- Validation rule catalog and error schema

### Tasks
- Define canonical fields:
  - `id`, `name`, `path`, `command`, `args`, `env`, `createdAt`, `updatedAt`
- Decide uniqueness constraints:
  - `name` unique (case-insensitive recommended)
- Define validation constraints:
  - Required: `name`, `path`, `command`
  - Max lengths and allowed characters
  - Path format checks for Windows-style paths
- Define response format:
  - Success payload shape
  - Error payload shape (`code`, `message`, `details`)

### Exit Criteria
- API request/response shapes documented and reviewed.
- Validation/error behavior agreed before coding.

## Phase 1 - Persistence Schema (Story 1.1)

### Deliverables
- Persistent storage schema for `ServiceDefinition`
- Repository/data-access layer
- Migration or bootstrap initialization

### Tasks
- Create table/entity for `ServiceDefinition`.
- Add unique index on service name.
- Add created/updated timestamp behavior.
- Implement data-access abstraction:
  - `GetAll`, `GetById`, `Create`, `Update`, `Delete`, `ExistsByName`
- Add startup DB initialization (safe and idempotent).

### Exit Criteria
- Service definitions persist across app restarts.
- Schema initialization works on a clean machine.

## Phase 2 - CRUD API (Story 1.2)

### Deliverables
- Fully functional REST endpoints for service definitions

### Tasks
- Implement endpoints:
  - `GET /api/services`
  - `GET /api/services/{id}` (optional but recommended)
  - `POST /api/services`
  - `PUT /api/services/{id}`
  - `DELETE /api/services/{id}`
- Map DTOs to domain model.
- Return proper status codes:
  - `200`, `201`, `204`, `400`, `404`, `409`, `500`
- Ensure API is localhost-only per product scope.

### Exit Criteria
- All CRUD operations work against persistent store.
- API returns stable, documented response shapes.

## Phase 3 - Validation and Error Handling (Story 1.3)

### Deliverables
- Input validation layer
- Consistent error handling middleware/filters

### Tasks
- Implement field-level validation:
  - Empty/null handling
  - Invalid path format
  - Missing/invalid command
  - Duplicate service name
- Add domain validation for update operations.
- Standardize API error format for all failures.
- Add actionable messages for user-facing failures.

### Exit Criteria
- Invalid requests always return structured, useful errors.
- Duplicate and malformed inputs are handled predictably.

## Phase 4 - Testing and Hardening

### Deliverables
- Unit tests and integration tests for Epic 1
- Basic resilience checks

### Tasks
- Unit tests:
  - Validation rules
  - Repository behaviors
  - Name uniqueness logic
- Integration tests:
  - End-to-end CRUD flow
  - Persistence across restart simulation
  - Error response contract assertions
- Negative tests:
  - Invalid path, empty command, duplicate name, unknown ID

### Exit Criteria
- Test suite passes reliably in local CI/dev workflow.
- Core registry behavior is stable for Epic 2 dependencies.

## Suggested Work Breakdown (AI-Assisted)

1. PR 1: Domain model + schema + migration/bootstrap
2. PR 2: Repository/data-access implementation
3. PR 3: CRUD endpoints + DTO mapping
4. PR 4: Validation layer + standardized error middleware
5. PR 5: Integration tests + polish

Each PR should include:
- Updated tests
- Brief API contract notes
- Backward-compatibility note (if route/DTO changed)

## Acceptance Criteria

- A service definition can be created and persisted.
- Listing services returns persisted records accurately.
- Updating a service modifies stored values and `updatedAt`.
- Deleting a service removes it from subsequent list calls.
- Duplicate service names are rejected with clear `409` error.
- Invalid `path`/`command` inputs are rejected with clear `400` errors.
- Restarting the backend preserves stored service definitions.

## Risks and Mitigations

- Risk: Path validation too strict for legitimate local setups.
  - Mitigation: Validate format and existence separately; only enforce required format in Epic 1.
- Risk: Name uniqueness collisions due to case sensitivity differences.
  - Mitigation: Normalize names for uniqueness checks.
- Risk: Error shapes drift between controllers/endpoints.
  - Mitigation: Centralize error handling in middleware.

## Dependencies for Next Epic

Epic 2 depends on Epic 1 outputs:
- Stable `ServiceDefinition` persistence
- Reliable lookup by service ID/name
- Predictable validation and error contracts
