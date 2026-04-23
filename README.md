# SpinUp

SpinUp is an app to control local servers on your machine. It helps developers manage local development environments from one place.

## Run in development

Backend API:

`dotnet run --project .\src\SpinUp.Api`

Frontend UI:

`cd .\web\spinup-ui`

`npm run dev`

To serve the built frontend from the API (single process), build the UI first and then run the API:

`cd .\web\spinup-ui`

`npm run build`

`cd ..\..`

`dotnet run --project .\src\SpinUp.Api`

Then open `http://localhost:5042`.

## Windows Service scripts (Epic 5)

Scripts are available in `scripts/windows` and should be run from an elevated PowerShell prompt:

- Install: `.\scripts\windows\install-service.ps1`
- Restart: `.\scripts\windows\restart-service.ps1`
- Uninstall: `.\scripts\windows\uninstall-service.ps1`

Canonical update command (elevated PowerShell):
- `.\scripts\windows\update-spinup.ps1`

When installed via script, the Windows Service serves both:
- frontend UI at `/`
- API at `/api/*`

Service-mode database location:
- `C:\ProgramData\SpinUp\spinup.db`

## Health endpoints

- Liveness: `/health/live`
- Readiness: `/health/ready`
- Startup diagnostics: `/health/startup`
