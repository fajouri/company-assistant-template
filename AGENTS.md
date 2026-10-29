# Contributor instructions
Read docs before changes. Keep deliveries runnable and narrow.
Use ASP.NET Core + React/TypeScript, one company per deployment.
Prefer explicit use cases and small modules; no speculative abstractions, generic repositories or agent swarms.
Never commit secrets, actual employee data or conversation content.
Deny access by default. Enforce permissions before retrieval and tool execution; model instructions are not authorization.
Demo authentication must never work in production.
Do not claim compliance or implemented functionality without evidence.
Run frontend build, backend build and relevant integration/security tests. State checks not run.
Preserve accessible keyboard navigation, semantic HTML, focus, contrast and reduced motion.
Use versioned migrations; never destructive database initialization in production.
