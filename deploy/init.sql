-- Runs once as the bootstrap superuser before the application migrations.
-- The knowledge schema (tables, roles and grants) is created by the versioned
-- `dotnet CompanyAssistant.Web.dll migrate` command, not here.
CREATE EXTENSION IF NOT EXISTS vector;
