Version 1.0

Create a new application called SpinUp.

SpinUp is a local application to manage backend services running on a developer machine. Developers often work across multiple systems (for example, e-commerce, ERP, and order management) and repeatedly need to start, stop, restart, and inspect service console output.

The app should provide a simple page where the developer can add, edit, and remove service definitions.

Example service:
- Name: `ERPCOM`
- Path: `C:\projects\erpcom`
- Command: `dotnet run` or `dotnet watch run`

Each configured service should be visible on screen with its current status (`up` or `down`). If a service is running, the developer should be able to view its console output, then stop or restart it.

SpinUp must run as a Windows service so it is always available in the background.

No login page is required for now.

Deliverable:
- Create a product specification document.
- Include architecture, proposed technologies, and functional description.
- Break the implementation into epics and stories for AI-assisted development.

Reference: see `docs/product-spec.md`.