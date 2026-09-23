# Local infrastructure

This directory contains local development infrastructure only. It is not a
deployment configuration.

## PostgreSQL 18

1. Copy `.env.example` to `.env` and set `EVERTORCH_POSTGRES_PASSWORD`. The
   `.env` file is ignored by Git; Compose refuses to start without a password.
2. Start the database: `docker compose -f docker/compose.yaml up -d --wait`
3. Stop it: `docker compose -f docker/compose.yaml down`
4. Discard all local data: `docker compose -f docker/compose.yaml down -v`

The database listens on `127.0.0.1` only.

## Schema

The schema is created by EF Core migrations in `Evertorch.Persistence/Migrations`.
The game server never applies them: it refuses to start while one is pending.

- Apply pending migrations to this database: `scripts/db-migrate.ps1` (it
  builds the connection from `.env`; pass `-ConnectionString` for another
  database).
- `scripts/run-server.cmd` starts this database, migrates it, and gives the
  server its connection string, so local play needs nothing else.
- Database tests start their own throwaway `postgres:18` containers with
  Testcontainers and never touch this database. `scripts/verify.ps1` therefore
  needs Docker running.

## Application configuration

Runtime configuration is external to compiled code: JSON files for non-secret
defaults, overridden by environment variables. Secrets are supplied through
environment variables or .NET user secrets and are never committed.

| Setting | JSON key | Environment variable |
| --- | --- | --- |
| Database connection | `ConnectionStrings:Evertorch` | `ConnectionStrings__Evertorch` |

The server refuses to start without it. The IDE launch profiles do not set it:
set the environment variable first, or use `scripts/run-server.cmd`. Example
for a local shell, using the values chosen in `.env`:

```text
ConnectionStrings__Evertorch=Host=127.0.0.1;Port=5432;Database=evertorch_dev;Username=evertorch;Password=<local password>
```
