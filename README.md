# Company Assistant Template

Configurable corporate assistant foundation: ASP.NET Core, React and PostgreSQL/pgvector.

## Run locally
Requires Docker Engine with Compose.
1. Copy `.env.example` to `.env` and set a strong local database password. The fictional Entra placeholders start only the anonymous shell; replace them before attempting real sign-in.
2. Run `docker compose up --build -d --wait`.
3. Open http://localhost:8080.
4. Stop with `docker compose down`. Database data persists; do not use `-v` unless you intend to delete it.

The interface is an authenticated policy assistant. Entra sign-in, a current-user session, authorization policies, permission-aware policy ingestion and retrieval, and grounded streaming chat with citations are implemented. Chat and embeddings are configurable and remain disabled until their deployments are supplied. The local interface displays fictional data only. Do not deploy publicly or load company documents yet. Real sign-in requires HTTPS and the [identity setup](docs/identity.md); the loopback HTTP Compose URL is only for the anonymous shell and smoke checks.

## Development
Node 22+; .NET 10 SDK.
- `cd frontend && npm ci --no-audit --no-fund && npm run build`
- `dotnet build src/CompanyAssistant.Web -c Release`
- `dotnet test tests/CompanyAssistant.Web.Tests -c Release`
- `python tests/branding_logo_test.py` after the Release build

Database migrations and ingestion are separate commands:
- `dotnet src/CompanyAssistant.Web/bin/Release/net10.0/CompanyAssistant.Web.dll migrate`
- `dotnet src/CompanyAssistant.Web/bin/Release/net10.0/CompanyAssistant.Web.dll ingest`

Docker uses the same frontend build and serves its output from ASP.NET Core; no Node server runs in production.

## Configuration
Public branding and non-secret identity defaults are in `src/CompanyAssistant.Web/appsettings.json`. Never place secrets there. Available interface languages: es, en. The logo is a local static asset. Configuration is read at startup; missing or invalid Entra settings stop startup.

Knowledge settings (`Database`, `Ingestion` and `Embeddings`) and chat settings (`Chat`) are also read at startup; embedding, database and chat secrets are supplied through the deployment secret/configuration system, never `appsettings.json`. See [Entra ID setup and manual verification](docs/identity.md) for app roles, redirect URLs, local secrets, trusted proxies and production session/key storage, [knowledge ingestion and retrieval](docs/ingestion.md) for migrations, ingestion, embeddings and the retrieval API, and [chat](docs/chat.md) for the streaming chat API, grounding, citations and the real-Foundry smoke test.

## Roadmap
See [implementation plan](docs/implementation-plan.md), [architecture](docs/architecture.md), [security](docs/security-and-privacy.md), and [scope](docs/product-scope.md).

This template does not claim GDPR compliance or certification. Production readiness requires implementation, testing and organizational review.
