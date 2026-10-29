# Company Assistant Template

Configurable corporate assistant foundation: ASP.NET Core, React and PostgreSQL/pgvector.

## Run locally
Requires Docker Engine with Compose.
1. Copy `.env.example` to `.env` and set a strong local database password.
2. Run `docker compose up --build -d --wait`.
3. Open http://localhost:8080.
4. Stop with `docker compose down`. Database data persists; do not use `-v` unless you intend to delete it.

This first delivery is an interface shell, not a functioning AI assistant. No login, model calls, retrieval or MCP are implemented. The local interface displays fictional data only. Do not deploy publicly or load company documents yet.

## Development
Node 22+; .NET 10 SDK.
`cd frontend && npm ci --no-audit --no-fund && npm run build`
`dotnet build src/CompanyAssistant.Web`
Docker uses the same frontend build and serves its output from ASP.NET Core; no Node server runs in production.

## Configuration
Public branding is in `src/CompanyAssistant.Web/appsettings.json`. Never place secrets there. Available interface languages: es, en. The logo is a local static asset. Configuration is read at startup.

## Roadmap
See [implementation plan](docs/implementation-plan.md), [architecture](docs/architecture.md), [security](docs/security-and-privacy.md), and [scope](docs/product-scope.md).

This template does not claim GDPR compliance or certification. Production readiness requires implementation, testing and organizational review.
